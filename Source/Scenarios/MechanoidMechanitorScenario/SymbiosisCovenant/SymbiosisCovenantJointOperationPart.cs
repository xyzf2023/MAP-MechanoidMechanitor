using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 联合军事行动的唯一权威状态机。随 Quest 存档，所有行动状态
    /// （目标、参与派系、阶段、倒计时、援军生成状态）只存在于此，
    /// 不写入 GameComponent，避免 Quest 与 GameComponent 两套状态漂移。
    /// 继承 QuestPartActivable：接取任务（InitiateSignal）时启用，之后每 tick 自检，
    /// 并通过 ProcessQuestSignal 处理目标地图的 MapGenerated / NoActiveThreats / AllEnemiesDefeated 信号。
    /// </summary>
    public class QuestPart_SymbiosisCovenantJointOperation : QuestPartActivable
    {
        public enum SymbiosisCovenantJointOperationStage : byte
        {
            OfferPending,
            OperationActive,
            TargetMapEntered,
            ReinforcementsDeployed,
            Succeeded,
            Failed,
            InvalidEnded
        }

        public string? actionId;
        public string? targetQuestTag;
        public SymbiosisCovenantJointOperationStage stage = SymbiosisCovenantJointOperationStage.OfferPending;
        public WorldObject? targetWorldObject;
        public Faction? targetFaction;
        public Faction? proposerFaction;
        public List<Faction>? participantFactions;
        public int offerExpireTick;
        public int operationExpireTick;
        public bool targetMapWasGeneratedByThisOperation;
        public bool reinforcementsGenerated;
        public bool successApplied;
        public bool failureApplied;
        public bool invalidEndApplied;
        public bool declinedOrExpiredApplied;
        public int targetThreatPointsAtDeployment;
        public float totalSupportPointsAtDeployment;
        public List<SymbiosisCovenantJointOperationFactionSupportRecord>? supportRecords;
        public List<string>? spawnedAidTags;

        // 额外运行期字段（随存档）
        public SymbiosisCovenantJointOperationDef? jointOperationDef;
        public int rewardValue;
        public bool playerEngaged;

        private const int ActiveCheckInterval = 2500;
        private const string AidQuestTagPrefix = "MAP_SymbiosisCovenantJointOp";

        public bool IsActive =>
            stage != SymbiosisCovenantJointOperationStage.Succeeded
            && stage != SymbiosisCovenantJointOperationStage.Failed
            && stage != SymbiosisCovenantJointOperationStage.InvalidEnded;

        /// <summary>
        /// 是否已接取（OfferPending 不算；仅 OperationActive 及之后阶段算）。
        /// 用于 Settlement 窄范围 Patch 只在已接取且尚未结束的行动上生效。
        /// </summary>
        public bool IsOperationAccepted =>
            stage == SymbiosisCovenantJointOperationStage.OperationActive
            || stage == SymbiosisCovenantJointOperationStage.TargetMapEntered
            || stage == SymbiosisCovenantJointOperationStage.ReinforcementsDeployed;

        public override IEnumerable<GlobalTargetInfo> QuestLookTargets
        {
            get
            {
                if (targetWorldObject != null && !targetWorldObject.Destroyed)
                {
                    yield return new GlobalTargetInfo(targetWorldObject);
                }
            }
        }

        public override IEnumerable<Faction> InvolvedFactions
        {
            get
            {
                if (targetFaction != null)
                {
                    yield return targetFaction;
                }

                if (proposerFaction != null)
                {
                    yield return proposerFaction;
                }

                if (participantFactions != null)
                {
                    foreach (Faction faction in participantFactions)
                    {
                        if (faction != null)
                        {
                            yield return faction;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 玩家接取任务时调用：OfferPending → OperationActive，并设置行动超时（15 天）。
        /// </summary>
        public override void PreQuestAccept()
        {
            base.PreQuestAccept();
            if (stage == SymbiosisCovenantJointOperationStage.OfferPending)
            {
                stage = SymbiosisCovenantJointOperationStage.OperationActive;
                SymbiosisCovenantJointOperationDef? def = ResolveDef();
                operationExpireTick = Find.TickManager.TicksGame
                    + (def?.operationTimeoutTicks ?? 900000);
            }
        }

        public override void QuestPartTick()
        {
            base.QuestPartTick();
            if (quest == null || !IsActive || Find.TickManager == null)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;

            if (stage == SymbiosisCovenantJointOperationStage.OperationActive
                || stage == SymbiosisCovenantJointOperationStage.TargetMapEntered
                || stage == SymbiosisCovenantJointOperationStage.ReinforcementsDeployed)
            {
                // 行动超时（15 天）直接判失败；邀请超时由 Quest 原生 acceptanceExpireTick 处理。
                if (operationExpireTick > 0 && now > operationExpireTick)
                {
                    BeginFailure("timeout");
                    return;
                }

                if (now % ActiveCheckInterval != 0)
                {
                    return;
                }

                TickActiveOrDeployed(now);
            }
        }

        /// <summary>
        /// 处理目标地图的原版 QuestTag 信号（F1）。
        /// 信号由 Site/MapParent 通过 questTags 自动发出：
        /// - &lt;targetQuestTag&gt;.MapGenerated：玩家已进入目标地图 → 部署援军（仅一次）。
        /// - &lt;targetQuestTag&gt;.NoActiveThreats / .AllEnemiesDefeated：站点敌人已清除 → 成功结算。
        /// Settlement 的成功不在此处理，由 SymbiosisCovenantJointOperationSettlementPatch 专用 Patch 触发。
        /// </summary>
        protected override void ProcessQuestSignal(Signal signal)
        {
            base.ProcessQuestSignal(signal);
            if (!IsActive || targetQuestTag == null)
            {
                return;
            }

            string tag = signal.tag;

            if (tag == targetQuestTag + ".MapGenerated")
            {
                // 玩家进入目标地图：部署联合援军（reinforcementsGenerated 防止重复/读档后重复）。
                EnsureDeployedIfMapReady();
                return;
            }

            if (tag == targetQuestTag + ".NoActiveThreats"
                || tag == targetQuestTag + ".AllEnemiesDefeated")
            {
                // 仅对 Site / Outpost / WorkSite 生效；必须已接取且援军已部署阶段。
                if (targetWorldObject is Site && IsOperationAccepted)
                {
                    BeginSuccess();
                }
            }
        }

        private void TickActiveOrDeployed(int now)
        {
            // 部署（读档后地图已存在但尚未部署时也会在此触发，最多延迟一个检查间隔）。
            EnsureDeployedIfMapReady();

            if (!reinforcementsGenerated)
            {
                // 未部署阶段的无效结束：目标被第三方移除、目标不再敌对玩家、
                // 参与派系全部失效、盟约降到 L3 以下。
                if (targetWorldObject == null || targetWorldObject.Destroyed)
                {
                    BeginInvalidEnd("targetRemoved");
                    return;
                }

                if (targetFaction == null
                    || Faction.OfPlayer == null
                    || !targetFaction.HostileTo(Faction.OfPlayer))
                {
                    BeginInvalidEnd("targetNoLongerHostile");
                    return;
                }

                if (!AnyValidParticipantRemaining())
                {
                    BeginInvalidEnd("noValidParticipant");
                    return;
                }

                if (!GameComponent_SymbiosisCovenantState.IsActive
                    || GameComponent_SymbiosisCovenantState.CurrentComponent?.CovenantLevel < 4)
                {
                    BeginInvalidEnd("covenantLevelTooLow");
                    return;
                }

                return;
            }

            Map? targetMap = (targetWorldObject as MapParent)?.Map;
            if (targetMap == null || !Find.Maps.Contains(targetMap))
            {
                // 目标地图已不存在：成功应由 NoActiveThreats / AllEnemiesDefeated（Site）
                // 或 SettlementDefeatUtility Patch（Settlement）在更早时机结算；
                // 若目标仍存活但地图消失，按无效结束处理。
                if (targetWorldObject != null && !targetWorldObject.Destroyed)
                {
                    BeginInvalidEnd("targetMapRemovedTargetAlive");
                }

                return;
            }

            // 已部署：目标被第三方移除 / 不再敌对 → 无效结束。
            if (targetWorldObject == null || targetWorldObject.Destroyed)
            {
                BeginInvalidEnd("targetRemoved");
                return;
            }

            if (targetFaction == null || !targetFaction.HostileTo(Faction.OfPlayer))
            {
                BeginInvalidEnd("targetNoLongerHostile");
                return;
            }

            // E：目标地图已无对玩家有效的敌对威胁时，命令本行动援军撤离。
            // （成功结算由 NoActiveThreats / AllEnemiesDefeated / Settlement Patch 另行触发。）
            if (!GenHostility.AnyHostileActiveThreatToPlayer(targetMap))
            {
                CommandReinforcementsLeave();
            }
        }

        /// <summary>
        /// 若本次行动尚未部署、且目标地图已存在，则部署联合援军。
        /// 被 MapGenerated 信号与每 tick 自检共用，保证读档后也能正确部署（不依赖 FreeColonistsSpawnedCount）。
        /// </summary>
        private void EnsureDeployedIfMapReady()
        {
            if (reinforcementsGenerated)
            {
                return;
            }

            if (stage != SymbiosisCovenantJointOperationStage.OperationActive
                && stage != SymbiosisCovenantJointOperationStage.TargetMapEntered)
            {
                return;
            }

            Map? map = (targetWorldObject as MapParent)?.Map;
            if (map == null || !Find.Maps.Contains(map))
            {
                return;
            }

            stage = SymbiosisCovenantJointOperationStage.TargetMapEntered;
            DeployReinforcements(map);
            stage = SymbiosisCovenantJointOperationStage.ReinforcementsDeployed;
        }

        // ===== 援军生成（B / C / D / H） =====

        private void DeployReinforcements(Map targetMap)
        {
            SymbiosisCovenantJointOperationDef? def = ResolveDef();
            if (def == null)
            {
                reinforcementsGenerated = true;
                return;
            }

            // H：真实目标威胁点（不再使用 StorytellerUtility.DefaultThreatPointsNow）。
            int threat = SymbiosisCovenantJointOperationUtility.TryGetTargetThreatPointsAtDeployment(
                targetWorldObject, targetMap);
            targetThreatPointsAtDeployment = threat;

            // B：所有参与派系“合计”的援军点数 = 目标威胁点 × supportPointsFactor。
            float total = threat * def.supportPointsFactor;
            totalSupportPointsAtDeployment = total;

            supportRecords ??= new List<SymbiosisCovenantJointOperationFactionSupportRecord>();
            spawnedAidTags ??= new List<string>();

            // 只将点数分给部署时仍有效、且能生成战斗编组的参与派系（最多 maxParticipants）。
            List<Faction> valid = ValidParticipantsNow();
            if (valid.Count == 0)
            {
                reinforcementsGenerated = true; // 已尝试部署，避免重复
                BeginInvalidEnd("noDeployableParticipant");
                return;
            }

            float[] assigned = AllocatePoints(valid, total);

            // C：是否使用快速空投，由“本次实际参与援军生成的派系”的最高科技统一决定。
            bool useQuick = valid
                .Where((faction, index) => assigned[index] > 0f && faction != null)
                .Any(faction => (int)faction.def.techLevel >= (int)def.industrialArrivalThreshold);

            int generated = 0;
            for (int i = 0; i < valid.Count; i++)
            {
                Faction participant = valid[i];
                float points = assigned[i];
                if (points <= 0f)
                {
                    continue;
                }

                List<Pawn>? pawns = GenerateCombatGroup(participant, points, targetMap, def);
                if (pawns == null || pawns.Count == 0)
                {
                    continue;
                }

                string aidTag = MakeAidTag(targetMap, participant);
                if (!DeployGroup(participant, pawns, points, targetMap, useQuick, aidTag, targetFaction))
                {
                    continue;
                }

                supportRecords.Add(new SymbiosisCovenantJointOperationFactionSupportRecord(
                    participant, points, pawns.Count, aidTag));
                spawnedAidTags.Add(aidTag);
                generated++;
            }

            reinforcementsGenerated = true;

            if (generated == 0)
            {
                // 没有任何可生成援军的参与派系：不要误设为“成功部署”，按无效结束安全收尾。
                BeginInvalidEnd("noReinforcementsGenerated");
                return;
            }

            if (spawnedAidTags.Count > 0 && targetWorldObject != null)
            {
                Find.LetterStack.ReceiveLetter(
                    "MAP_MechanoidMechanitor.Symbiosis.JointOp.Reinforcements.Letter.Label"
                        .Translate(),
                    "MAP_MechanoidMechanitor.Symbiosis.JointOp.Reinforcements.Letter.Text"
                        .Translate(targetMap.Parent.Label),
                    LetterDefOf.PositiveEvent,
                    new GlobalTargetInfo(targetWorldObject));
            }
        }

        /// <summary>
        /// B：将总援军点数平均分配给“能生成有效战斗编组”的参与派系；
        /// 不能生成的派系份额为 0（其预算被其余可生成派系均分）；
        /// 最后一个可生成派系吸收浮点余量，保证总和 == total。
        /// </summary>
        private float[] AllocatePoints(List<Faction> participants, float total)
        {
            float[] assigned = new float[participants.Count];
            bool[] canGenerate = new bool[participants.Count];
            int generatable = 0;
            for (int i = 0; i < participants.Count; i++)
            {
                canGenerate[i] = CanGenerateCombatGroup(participants[i]);
                if (canGenerate[i])
                {
                    generatable++;
                }
            }

            if (generatable == 0)
            {
                return assigned; // 全为 0
            }

            float per = total / generatable;
            float used = 0f;
            int lastIndex = -1;
            for (int i = 0; i < participants.Count; i++)
            {
                if (!canGenerate[i])
                {
                    assigned[i] = 0f;
                    continue;
                }

                assigned[i] = per;
                used += per;
                lastIndex = i;
            }

            // 最后一派吸收浮点余量，保证总和 == total。
            if (lastIndex >= 0)
            {
                assigned[lastIndex] += total - used;
            }

            return assigned;
        }

        /// <summary>
        /// 使用原版 PawnGroupMaker 链路为某个参与派系生成真实战斗编组（D）。
        /// 不调用 RaidFriendly，不直接创建 Lord。
        /// </summary>
        private static List<Pawn>? GenerateCombatGroup(
            Faction faction,
            float points,
            Map map,
            SymbiosisCovenantJointOperationDef def)
        {
            IncidentParms parms = new IncidentParms
            {
                target = map,
                faction = faction,
                points = points,
                raidStrategy = RaidStrategyDefOf.ImmediateAttackFriendly,
                spawnCenter = map.Center
            };

            PawnGroupMakerParms makerParms = IncidentParmsUtility.GetDefaultPawnGroupMakerParms(
                PawnGroupKindDefOf.Combat, parms, ensureCanGenerateAtLeastOnePawn: true);
            if (!PawnGroupMakerUtility.TryGetRandomPawnGroupMaker(makerParms, out _))
            {
                return null;
            }

            List<Pawn> pawns = PawnGroupMakerUtility.GeneratePawns(makerParms, warnOnZeroResults: false)
                .ToList();
            if (pawns.Count == 0)
            {
                return null;
            }

            return pawns;
        }

        /// <summary>
        /// 把生成的援军送入目标地图并创建本 MOD 自定义 Lord（D / C）。
        /// 抵达方式（快速空投或边缘步行）由 useQuick 统一决定。
        /// 每只援军写入唯一 aidTag，并通过 QuestUtility.AddQuestTag 标记到 Lord，
        /// 之后只通过 LordJob 类型 + aidTag 精确追踪，不误伤其他援军。
        /// </summary>
        private static bool DeployGroup(
            Faction faction,
            List<Pawn> pawns,
            float points,
            Map map,
            bool useQuick,
            string aidTag,
            Faction? enemyFaction)
        {
            PawnsArrivalModeDef arrivalMode = useQuick
                ? PawnsArrivalModeDefOf.CenterDrop
                : PawnsArrivalModeDefOf.EdgeWalkIn;

            IncidentParms parms = new IncidentParms
            {
                target = map,
                faction = faction,
                points = points,
                raidStrategy = RaidStrategyDefOf.ImmediateAttackFriendly,
                // EdgeWalkIn 只有在 spawnCenter 无效时才会寻找地图边缘入口。
                // 因此低科技步行援军必须从 Invalid 开始，不能预填 map.Center。
                spawnCenter = useQuick ? map.Center : IntVec3.Invalid,
                raidArrivalMode = arrivalMode,
                raidArrivalModeForQuickMilitaryAid = useQuick
            };

            // 步行抵达必须成功解析合法的地图边缘入口；失败时不得继续在中心生成。
            if (!useQuick && !arrivalMode.Worker.TryResolveRaidSpawnCenter(parms))
            {
                return false;
            }

            arrivalMode.Worker.Arrive(pawns, parms);

            LordJob_SymbiosisCovenantJointOperation job =
                new LordJob_SymbiosisCovenantJointOperation(faction, enemyFaction, map.Center);
            Lord? lord = LordMaker.MakeNewLord(faction, job, map, pawns);
            if (lord == null)
            {
                return false;
            }

            QuestUtility.AddQuestTag(lord, aidTag);
            return true;
        }

        /// <summary>
        /// E：仅向本行动自己的援军 Lord 发出专用撤离 memo（不 Destroy / Kill 任何 Pawn）。
        /// </summary>
        private void CommandReinforcementsLeave()
        {
            if (spawnedAidTags == null || spawnedAidTags.Count == 0)
            {
                return;
            }

            // 不依赖 targetWorldObject.Map：据点被原版摧毁时，旧 Settlement 已被
            // DestroyedSettlement 替换，旧目标引用将不再能找到原地图。
            // 通过 aidTag 在当前所有地图中寻找本行动自己的 Lord，才能保证结算后仍会撤离。
            foreach (Map map in Find.Maps)
            {
                foreach (string tag in spawnedAidTags)
                {
                    foreach (Lord lord in FindTaggedJointOpLords(map, tag))
                    {
                        LordJob_SymbiosisCovenantJointOperation.SendLeave(lord);
                    }
                }
            }
        }

        private static List<Lord> FindTaggedJointOpLords(Map map, string? tag)
        {
            if (string.IsNullOrEmpty(tag) || map?.lordManager == null)
            {
                return new List<Lord>();
            }

            return map.lordManager.lords
                .Where(lord =>
                    lord != null
                    && lord.LordJob is LordJob_SymbiosisCovenantJointOperation
                    && lord.questTags != null
                    && lord.questTags.Contains(tag))
                .ToList();
        }

        /// <summary>
        /// DEV 用：统计某个 aidTag 对应的本行动援军 Lord 数量（供状态快照展示）。
        /// </summary>
        public static int CountTaggedJointOpLordsPublic(Map map, string? tag)
        {
            return FindTaggedJointOpLords(map, tag).Count;
        }

        private void BeginSuccess()
        {
            if (successApplied || failureApplied || invalidEndApplied)
            {
                return;
            }

            stage = SymbiosisCovenantJointOperationStage.Succeeded;
            CommandReinforcementsLeave();
            ApplySuccessOutcome();
            // J：实物奖励本次暂不发放，仅保留 Unity / Trust 成功奖励（见 GrantReward 注释）。
            quest?.End(QuestEndOutcome.Success);
        }

        private void BeginFailure(string reason)
        {
            if (successApplied || failureApplied || invalidEndApplied)
            {
                return;
            }

            stage = SymbiosisCovenantJointOperationStage.Failed;
            CommandReinforcementsLeave();
            ApplyFailOutcome();
            quest?.End(QuestEndOutcome.Fail);
        }

        private void BeginInvalidEnd(string reason)
        {
            if (successApplied || failureApplied || invalidEndApplied)
            {
                return;
            }

            stage = SymbiosisCovenantJointOperationStage.InvalidEnded;
            CommandReinforcementsLeave();
            ApplyInvalidOutcome();
            quest?.End(QuestEndOutcome.Unknown);
        }

        /// <summary>
        /// F2：由 SymbiosisCovenantJointOperationSettlementPatch 在据点被原版正确摧毁后调用。
        /// 仅当引用一致且已接取、尚未结束时结算成功。
        /// </summary>
        public void NotifySettlementDestroyed(Settlement factionBase)
        {
            if (!IsOperationAccepted)
            {
                return;
            }

            if (targetWorldObject == null || !ReferenceEquals(targetWorldObject, factionBase))
            {
                return;
            }

            BeginSuccess();
        }

        /// <summary>
        /// 任务清理（无论何种原因结束都会调用）。这是未显式结算时的兜底（G7）：
        /// 邀请被拒绝/过期 → 5 天冷却，无奖惩；
        /// 已接取但未由本系统显式结算（非 timeout 失败）→ 默认无效结束（不加不减 Unity/Trust），
        /// 绝不因外部中断/未知原因误扣惩罚。
        /// </summary>
        public override void Notify_PreCleanup()
        {
            base.Notify_PreCleanup();
            if (successApplied || failureApplied || invalidEndApplied)
            {
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            SymbiosisCovenantJointOperationDef? def = ResolveDef();
            if (stage == SymbiosisCovenantJointOperationStage.OfferPending)
            {
                declinedOrExpiredApplied = true;
                if (def != null)
                {
                    SymbiosisCovenantJointOperationScheduler
                        .SetCooldownEndTick(now + def.declinedOrExpiredCooldownTicks);
                }
            }
            else
            {
                // 此处已在 Quest.End → CleanupQuestParts 调用链内，绝不能再次调用 quest.End，
                // 否则会重入清理并造成重复 Cleanup / 重复结束信件。
                stage = SymbiosisCovenantJointOperationStage.InvalidEnded;
                CommandReinforcementsLeave();
                ApplyInvalidOutcome();
            }
        }

        private void ApplySuccessOutcome()
        {
            successApplied = true;
            SymbiosisCovenantJointOperationDef? def = ResolveDef();
            if (def == null)
            {
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            SymbiosisCovenantJointOperationScheduler
                .SetCooldownEndTick(now + def.completedOrFailedCooldownTicks);
            GameComponent_SymbiosisCovenantState.TryAdjustUnity(
                def.successUnityDelta,
                "MAP_MechanoidMechanitor.Symbiosis.TrustReason.JointOperation");
            foreach (Faction faction in ValidParticipantsNow())
            {
                GameComponent_SymbiosisCovenantState.TryAdjustTrust(
                    faction,
                    def.successTrustDeltaPerValidParticipant,
                    "MAP_MechanoidMechanitor.Symbiosis.TrustReason.JointOperation",
                    SymbiosisCovenantTrustSource.Quest);
            }
        }

        private void ApplyFailOutcome()
        {
            failureApplied = true;
            SymbiosisCovenantJointOperationDef? def = ResolveDef();
            if (def == null)
            {
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            SymbiosisCovenantJointOperationScheduler
                .SetCooldownEndTick(now + def.completedOrFailedCooldownTicks);
            GameComponent_SymbiosisCovenantState.TryAdjustUnity(
                def.failUnityDelta,
                "MAP_MechanoidMechanitor.Symbiosis.TrustReason.JointOperation");
            foreach (Faction faction in ValidParticipantsNow())
            {
                GameComponent_SymbiosisCovenantState.TryAdjustTrust(
                    faction,
                    def.failTrustDeltaPerValidParticipant,
                    "MAP_MechanoidMechanitor.Symbiosis.TrustReason.JointOperation",
                    SymbiosisCovenantTrustSource.Quest);
            }
        }

        private void ApplyInvalidOutcome()
        {
            invalidEndApplied = true;
            SymbiosisCovenantJointOperationDef? def = ResolveDef();
            if (def == null)
            {
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            SymbiosisCovenantJointOperationScheduler
                .SetCooldownEndTick(now + def.invalidEndCooldownTicks);
        }

        /// <summary>
        /// J：实物奖励本次暂不发放。
        /// 原实现用 ThingSetMakerDefOf.Reward_ItemsStandard + GenPlace 直接放置物品，
        /// 不是完整的原版 Quest 奖励流程，可能在纯机械殖民地/目标地图移除等情况下错发。
        /// 本次修复优先保证行动/目标/援军/结算正确，实物奖励保留 Def 字段供未来以原版
        /// Quest reward 路径单独实现，此处故意留空。
        /// </summary>
        private void GrantReward()
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref actionId, "actionId");
            Scribe_Values.Look(ref targetQuestTag, "targetQuestTag");
            Scribe_Values.Look(ref stage, "stage", SymbiosisCovenantJointOperationStage.OfferPending);
            Scribe_References.Look(ref targetWorldObject, "targetWorldObject");
            Scribe_References.Look(ref targetFaction, "targetFaction");
            Scribe_References.Look(ref proposerFaction, "proposerFaction");
            Scribe_Collections.Look(ref participantFactions, "participantFactions", LookMode.Reference);
            Scribe_Values.Look(ref offerExpireTick, "offerExpireTick", 0);
            Scribe_Values.Look(ref operationExpireTick, "operationExpireTick", 0);
            Scribe_Values.Look(ref targetMapWasGeneratedByThisOperation, "targetMapWasGeneratedByThisOperation", false);
            Scribe_Values.Look(ref reinforcementsGenerated, "reinforcementsGenerated", false);
            Scribe_Values.Look(ref successApplied, "successApplied", false);
            Scribe_Values.Look(ref failureApplied, "failureApplied", false);
            Scribe_Values.Look(ref invalidEndApplied, "invalidEndApplied", false);
            Scribe_Values.Look(ref declinedOrExpiredApplied, "declinedOrExpiredApplied", false);
            Scribe_Values.Look(ref targetThreatPointsAtDeployment, "targetThreatPointsAtDeployment", 0);
            Scribe_Values.Look(ref totalSupportPointsAtDeployment, "totalSupportPointsAtDeployment", 0f);
            Scribe_Collections.Look(
                ref supportRecords,
                "supportRecords",
                LookMode.Deep);
            Scribe_Collections.Look(ref spawnedAidTags, "spawnedAidTags", LookMode.Value);
            Scribe_Defs.Look(ref jointOperationDef, "jointOperationDef");
            Scribe_Values.Look(ref rewardValue, "rewardValue", 0);
            Scribe_Values.Look(ref playerEngaged, "playerEngaged", false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                participantFactions ??= new List<Faction>();
                participantFactions.RemoveAll(f => f == null);
                supportRecords ??= new List<SymbiosisCovenantJointOperationFactionSupportRecord>();
                spawnedAidTags ??= new List<string>();
            }
        }

        // ===== 静态入口：供调度器在盟约降级时取消进行中的行动（G6） =====

        public static void NotifyCovenantLevelChanged(int level)
        {
            if (level >= 4)
            {
                return;
            }

            QuestPart_SymbiosisCovenantJointOperation? part =
                SymbiosisCovenantJointOperationUtility.FindActiveOperationPart();
            if (part != null && part.IsActive)
            {
                part.BeginInvalidEnd("covenantLevelTooLow");
            }
        }

        // ===== 内部辅助 =====

        private SymbiosisCovenantJointOperationDef? ResolveDef()
        {
            return jointOperationDef
                ?? SymbiosisCovenantJointOperationDefOf.MAP_SymbiosisCovenant_JointOperationConfig;
        }

        private List<Faction> ValidParticipantsNow()
        {
            List<Faction> result = new List<Faction>();
            if (participantFactions == null)
            {
                return result;
            }

            foreach (Faction faction in participantFactions)
            {
                if (IsParticipantStillValid(faction))
                {
                    result.Add(faction);
                }
            }

            return result;
        }

        private bool AnyValidParticipantRemaining()
        {
            return ValidParticipantsNow().Count > 0;
        }

        private bool IsParticipantStillValid(Faction? faction)
        {
            if (faction == null)
            {
                return false;
            }

            if (faction.defeated || faction.deactivated || faction.Hidden || faction.temporary)
            {
                return false;
            }

            if (Faction.OfPlayer != null && faction.HostileTo(Faction.OfPlayer))
            {
                return false;
            }

            GameComponent_SymbiosisCovenantState? state =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (state == null || state.GetRecord(faction)?.CovenantMember != true)
            {
                return false;
            }

            // 参与派系必须对本次行动的目标派系保持敌对。
            if (targetFaction == null || !faction.HostileTo(targetFaction))
            {
                return false;
            }

            return true;
        }

        private static bool CanGenerateCombatGroup(Faction faction)
        {
            if (faction.def?.raidsForbidden == true)
            {
                return false;
            }

            // 仅做能力判定，不真正生成；使用安全的最小参数（与共同防卫判定一致）。
            PawnGroupMakerParms makerParms = new PawnGroupMakerParms
            {
                faction = faction,
                groupKind = PawnGroupKindDefOf.Combat,
                points = 1000f
            };
            return PawnGroupMakerUtility.TryGetRandomPawnGroupMaker(makerParms, out _);
        }

        private static string MakeAidTag(Map map, Faction responder)
        {
            int tick = Find.TickManager?.TicksGame ?? 0;
            return AidQuestTagPrefix
                + "_"
                + map.GetUniqueLoadID()
                + "_"
                + tick
                + "_"
                + responder.GetUniqueLoadID();
        }
    }
}
