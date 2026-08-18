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
    /// 继承 QuestPartActivable：接取任务（InitiateSignal）时启用，之后每 tick 自检。
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
        /// 玩家接取任务时调用：OfferPending → OperationActive，并设置行动超时。
        /// </summary>
        public override void PreQuestAccept()
        {
            base.PreQuestAccept();
            if (stage == SymbiosisCovenantJointOperationStage.OfferPending)
            {
                stage = SymbiosisCovenantJointOperationStage.OperationActive;
                SymbiosisCovenantJointOperationDef def = ResolveDef()!;
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

        private void TickActiveOrDeployed(int now)
        {
            // 接取后、部署前的无效结束：目标被第三方移除、目标不再敌对玩家、参与派系全部失效、盟约降级。
            if (!reinforcementsGenerated)
            {
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
            }

            Map? targetMap = (targetWorldObject as MapParent)?.Map;
            if (targetMap == null || !Find.Maps.Contains(targetMap))
            {
                return;
            }

            bool playerOnMap = targetMap.mapPawns.FreeColonistsSpawnedCount > 0
                || targetMap.mapPawns.AnyColonistSpawned;

            if (!reinforcementsGenerated)
            {
                if (playerOnMap)
                {
                    // 玩家进入目标地图：进入阶段 → 生成真实混编联合援军 → 已部署阶段。
                    stage = SymbiosisCovenantJointOperationStage.TargetMapEntered;
                    playerEngaged = true;
                    DeployReinforcements(targetMap);
                    stage = SymbiosisCovenantJointOperationStage.ReinforcementsDeployed;
                }

                return;
            }

            // 已部署：检测成功（目标移除/摧毁即视为清剿完成）。
            if (targetWorldObject == null || targetWorldObject.Destroyed)
            {
                BeginSuccess();
                return;
            }

            if (playerEngaged && !AnyLivingTargetCombatant(targetMap))
            {
                BeginSuccess();
            }
        }

        private void DeployReinforcements(Map targetMap)
        {
            SymbiosisCovenantJointOperationDef def = ResolveDef()!;
            if (def == null)
            {
                return;
            }

            targetThreatPointsAtDeployment = (int)StorytellerUtility.DefaultThreatPointsNow(targetMap);
            supportRecords ??= new List<SymbiosisCovenantJointOperationFactionSupportRecord>();
            spawnedAidTags ??= new List<string>();

            float total = 0f;
            if (participantFactions != null)
            {
                foreach (Faction participant in participantFactions)
                {
                    if (!IsParticipantStillValid(participant))
                    {
                        continue;
                    }

                    float support = targetThreatPointsAtDeployment * def.supportPointsFactor;
                    string tag = MakeAidTag(targetMap, participant);
                    IncidentParms parms = BuildAidParms(targetMap, participant, support, tag, def);
                    bool executed = IncidentDefOf.RaidFriendly.Worker.TryExecute(parms);
                    List<Lord> lords = FindTaggedAidLords(targetMap, tag, participant);
                    if (executed && lords.Count > 0)
                    {
                        int pawnCount = lords.Sum(lord => lord.ownedPawns.Count);
                        supportRecords.Add(
                            new SymbiosisCovenantJointOperationFactionSupportRecord(
                                participant, support, pawnCount));
                        spawnedAidTags.Add(tag);
                        total += support;
                    }
                }
            }

            totalSupportPointsAtDeployment = total;
            reinforcementsGenerated = true;

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

        private void BeginSuccess()
        {
            if (successApplied || failureApplied || invalidEndApplied)
            {
                return;
            }

            stage = SymbiosisCovenantJointOperationStage.Succeeded;
            ApplySuccessOutcome();
            GrantReward();
            quest?.End(QuestEndOutcome.Success);
        }

        private void BeginFailure(string reason)
        {
            if (successApplied || failureApplied || invalidEndApplied)
            {
                return;
            }

            stage = SymbiosisCovenantJointOperationStage.Failed;
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
            ApplyInvalidOutcome();
            quest?.End(QuestEndOutcome.Unknown);
        }

        /// <summary>
        /// 任务清理（无论何种原因结束都会调用）。这是未显式结算时的兜底：
        /// 邀请被拒绝/过期 → 5 天冷却；已接取但未明确结算 → 按失败处理。
        /// </summary>
        public override void Notify_PreCleanup()
        {
            base.Notify_PreCleanup();
            if (successApplied || failureApplied || invalidEndApplied)
            {
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            SymbiosisCovenantJointOperationDef def = ResolveDef()!;
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
                failureApplied = true;
                if (def != null)
                {
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

                    SymbiosisCovenantJointOperationScheduler
                        .SetCooldownEndTick(now + def.completedOrFailedCooldownTicks);
                }
            }
        }

        private void ApplySuccessOutcome()
        {
            successApplied = true;
            SymbiosisCovenantJointOperationDef def = ResolveDef()!;
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
            SymbiosisCovenantJointOperationDef def = ResolveDef()!;
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
            SymbiosisCovenantJointOperationDef def = ResolveDef()!;
            if (def == null)
            {
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            SymbiosisCovenantJointOperationScheduler
                .SetCooldownEndTick(now + def.invalidEndCooldownTicks);
        }

        private void GrantReward()
        {
            SymbiosisCovenantJointOperationDef def = ResolveDef()!;
            if (def == null || rewardValue <= 0)
            {
                return;
            }

            ThingSetMakerParams makerParams = new ThingSetMakerParams
            {
                totalMarketValueRange = new FloatRange(rewardValue, rewardValue)
            };
            List<Thing> things = ThingSetMakerDefOf.Reward_ItemsStandard.root.Generate(makerParams);
            if (things == null || things.Count == 0)
            {
                return;
            }

            Map? dropMap = ChooseRewardDropMap();
            if (dropMap == null)
            {
                return;
            }

            IntVec3 center = DropCellFinder.FindRaidDropCenterDistant(dropMap);
            foreach (Thing thing in things)
            {
                GenPlace.TryPlaceThing(
                    thing,
                    center,
                    dropMap,
                    ThingPlaceMode.Near);
            }

            Find.LetterStack.ReceiveLetter(
                "MAP_MechanoidMechanitor.Symbiosis.JointOp.Reward.Letter.Label".Translate(),
                "MAP_MechanoidMechanitor.Symbiosis.JointOp.Reward.Letter.Text"
                    .Translate(dropMap.Parent.Label),
                LetterDefOf.PositiveEvent,
                new GlobalTargetInfo(dropMap.Parent));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref actionId, "actionId");
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

        // ===== 静态入口：供调度器在盟约降级时取消进行中的行动 =====

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

        private bool AnyLivingTargetCombatant(Map map)
        {
            if (map?.mapPawns == null)
            {
                return false;
            }

            foreach (Pawn pawn in map.mapPawns.AllPawns)
            {
                if (pawn.Faction == null || pawn.Dead || !pawn.Spawned)
                {
                    continue;
                }

                if (pawn.Faction == targetFaction && !pawn.Downed)
                {
                    return true;
                }
            }

            return false;
        }

        private Map? ChooseRewardDropMap()
        {
            Map? targetMap = (targetWorldObject as MapParent)?.Map;
            if (targetMap != null && Find.Maps.Contains(targetMap)
                && (targetMap.mapPawns.FreeColonistsSpawnedCount > 0
                    || targetMap.mapPawns.AnyColonistSpawned))
            {
                return targetMap;
            }

            foreach (Map map in Find.Maps)
            {
                if (map.IsPlayerHome && map.mapPawns.FreeColonistsSpawnedCount > 0)
                {
                    return map;
                }
            }

            return null;
        }

        private static IncidentParms BuildAidParms(
            Map map,
            Faction faction,
            float support,
            string tag,
            SymbiosisCovenantJointOperationDef def)
        {
            IncidentParms parms = new IncidentParms
            {
                target = map,
                faction = faction,
                points = support,
                raidStrategy = RaidStrategyDefOf.ImmediateAttackFriendly,
                questTag = tag
            };
            if ((int)faction.def.techLevel >= (int)def.industrialArrivalThreshold)
            {
                parms.raidArrivalModeForQuickMilitaryAid = true;
            }
            else
            {
                parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;
            }

            return parms;
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

        private static List<Lord> FindTaggedAidLords(Map map, string? tag, Faction? faction = null)
        {
            if (string.IsNullOrEmpty(tag) || map?.lordManager == null)
            {
                return new List<Lord>();
            }

            return map.lordManager.lords
                .Where(lord =>
                    lord != null
                    && lord.LordJob is LordJob_AssistColony
                    && lord.AnyActivePawn
                    && (faction == null || lord.faction == faction)
                    && lord.questTags != null
                    && lord.questTags.Contains(tag))
                .ToList();
        }
    }
}
