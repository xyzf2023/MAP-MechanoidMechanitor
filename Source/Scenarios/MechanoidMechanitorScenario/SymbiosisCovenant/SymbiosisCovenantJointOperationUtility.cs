using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 共生盟约「联合军事行动」的纯逻辑工具集。
    /// 负责：世界目标优先级筛选、发起者与参与派系选择、邀请文本构造、
    /// 当前进行中行动的查询、奖励价值估算。
    /// 所有外交/资格判定尽量复用共生盟约既有判断（普通派系、成员记录），
    /// 不另写一套与世界目标/派系过滤不一致的逻辑。
    /// </summary>
    public static class SymbiosisCovenantJointOperationUtility
    {
        /// <summary>
        /// 目标优先级：1=敌对哨站，2=Ideology 工作站，3=普通派系据点，0=不是合格目标。
        /// </summary>
        public static int GetTargetPriority(WorldObject obj)
        {
            if (obj == null)
            {
                return 0;
            }

            if (obj is Site site)
            {
                SitePartDef? mainDef = site.MainSitePartDef;
                SitePartWorker? worker = mainDef?.Worker;
                if (worker is SitePartWorker_Outpost)
                {
                    return 1;
                }

                if (worker is SitePartWorker_WorkSite
                    && mainDef?.tags != null
                    && mainDef.tags.Contains("WorkSite"))
                {
                    return 2;
                }

                return 0;
            }

            if (obj is Settlement)
            {
                return 3;
            }

            return 0;
        }

        /// <summary>
        /// 判断世界目标是否可作为本次联合军事行动目标。绝不凭空生成据点，
        /// 只从真实存在且仍有效的敌方世界对象中挑选。
        /// </summary>
        public static bool IsEligibleTarget(WorldObject obj, out Faction? targetFaction, out string invalidReason)
        {
            targetFaction = null;
            invalidReason = string.Empty;

            if (obj == null || !obj.Spawned || obj.Destroyed)
            {
                invalidReason = "worldObjectInvalid";
                return false;
            }

            Faction? faction = obj.Faction;
            if (faction == null)
            {
                invalidReason = "noFaction";
                return false;
            }

            if (faction.IsPlayer || faction.defeated || faction.deactivated)
            {
                invalidReason = "factionUnavailable";
                return false;
            }

            if (faction.Hidden || faction.temporary)
            {
                invalidReason = "factionHiddenOrTemporary";
                return false;
            }

            if (Faction.OfPlayer == null || !faction.HostileTo(Faction.OfPlayer))
            {
                invalidReason = "notHostileToPlayer";
                return false;
            }

            // 已经是本系统其它行动的目标则跳过，避免重复派发。
            if (IsTargetAlreadyInUse(obj))
            {
                invalidReason = "alreadyInUse";
                return false;
            }

            // 目标不得已有生成地图（玩家尚未进攻）。
            if (obj is MapParent mapParent && mapParent.HasMap)
            {
                invalidReason = "mapAlreadyGenerated";
                return false;
            }

            // 机械族/虫族等不应作为「普通派系联盟军事行动」对象，复用共生盟约普通派系判定。
            if (!MechanoidMechanitorOrdinaryFactionUtility.IsOrdinaryFaction(faction))
            {
                invalidReason = "notOrdinaryFaction";
                return false;
            }

            // 至少一名当前 CovenantMember 同时敌对目标派系，行动才有意义。
            if (!AnyCovenantMemberHostileTo(faction))
            {
                invalidReason = "noHostileMember";
                return false;
            }

            targetFaction = faction;
            return true;
        }

        /// <summary>
        /// 按优先级从真实世界对象中挑选一个目标：
        /// 有第一类候选就只从第一类抽取，没有才看第二类，没有才看第三类。
        /// </summary>
        public static WorldObject? SelectTargetWorldObject(
            out Faction? targetFaction)
        {
            targetFaction = null;
            if (Find.World == null)
            {
                return null;
            }

            List<WorldObject> candidates = new List<WorldObject>();
            foreach (WorldObject obj in Find.World.worldObjects.AllWorldObjects)
            {
                if (GetTargetPriority(obj) <= 0)
                {
                    continue;
                }

                if (IsEligibleTarget(obj, out _, out _))
                {
                    candidates.Add(obj);
                }
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            // 严格按优先级选择：只从优先级最高（数值最小）的合格候选中抽取。
            // 不使用容易读反的降序 CompareTo 排序。
            int bestPriority = candidates.Min(GetTargetPriority);
            List<WorldObject> bestClass = candidates
                .Where(c => GetTargetPriority(c) == bestPriority)
                .ToList();
            WorldObject chosen = bestClass.RandomElement();
            targetFaction = chosen.Faction;
            return chosen;
        }

        /// <summary>
        /// 判断候选派系是否可作为参与派系（含发起者）。
        /// </summary>
        public static bool IsEligibleParticipant(Faction? faction, Faction? targetFaction)
        {
            if (faction == null || targetFaction == null)
            {
                return false;
            }

            if (faction == targetFaction || faction.IsPlayer)
            {
                return false;
            }

            if (faction.defeated || faction.deactivated || faction.Hidden || faction.temporary)
            {
                return false;
            }

            // 参与派系不得敌对玩家，否则与「联盟」调性冲突。
            if (Faction.OfPlayer != null && faction.HostileTo(Faction.OfPlayer))
            {
                return false;
            }

            if (!faction.HostileTo(targetFaction))
            {
                return false;
            }

            return CanGenerateCombatGroup(faction);
        }

        /// <summary>
        /// 选择发起者（Trust 最高的成员，带随机扰动避免永远同一派系）与至多两名其它参与派系。
        /// 玩家不计入参与派系数量。
        /// </summary>
        public static bool SelectProposerAndParticipants(
            SymbiosisCovenantJointOperationDef def,
            Faction? targetFaction,
            out Faction? proposer,
            out List<Faction> participants)
        {
            proposer = null;
            participants = new List<Faction>();

            GameComponent_SymbiosisCovenantState? state =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (state == null || targetFaction == null)
            {
                return false;
            }

            List<Faction> eligible = new List<Faction>();
            IReadOnlyList<SymbiosisCovenantFactionRecord> records = state.GetRecordsSorted();
            for (int i = 0; i < records.Count; i++)
            {
                Faction? faction = records[i].Faction;
                if (records[i].CovenantMember
                    && faction != null
                    && IsEligibleParticipant(faction, targetFaction))
                {
                    eligible.Add(faction);
                }
            }

            // 至少一名即可生成（符合共生盟约「一个成员也可发起」的设计原则）。
            if (eligible.Count == 0)
            {
                return false;
            }

            // 按 Trust 加权选择，加入随机扰动避免长期锁定同一派系。
            proposer = PickWeightedByTrust(state, eligible);
            participants.Add(proposer);

            // proposer 是 out 参数，不能在 lambda 中直接捕获；先用局部变量承接。
            Faction? pickedProposer = proposer;
            List<Faction> others = eligible.Where(f => f != pickedProposer).ToList();
            while (participants.Count < def.maxParticipants && others.Count > 0)
            {
                Faction next = PickWeightedByTrust(state, others);
                participants.Add(next);
                others.Remove(next);
            }

            return true;
        }

        /// <summary>
        /// 首领显示规则：leader 可用时用 LeaderTitle + leader.LabelShort；
        /// 否则用「{派系名}的代表」。不允许个别派系无 leader 阻断任务生成。
        /// </summary>
        public static string BuildProposerLeaderLabel(Faction? proposer)
        {
            if (proposer?.leader != null
                && !proposer.leader.Destroyed
                && !proposer.leader.Dead)
            {
                // LeaderTitle 与 LabelShort 均为游戏数据，组合成显示名不属于硬编码句子。
                return proposer.LeaderTitle + " " + proposer.leader.LabelShort;
            }

            return "MAP_MechanoidMechanitor.Symbiosis.JointOp.Proposer.Representative"
                .Translate(proposer?.Name ?? "???");
        }

        /// <summary>
        /// 构造邀请说明文本（作为 Quest.description）。完整句子模板在语言文件中，
        /// C# 只负责把动态占位符（发起派系、首领描述、目标派系、目标地点、参与派系）填入。
        /// 首领描述由 BuildProposerLeaderLabel 提供（称谓+名，或“{派系}的代表”兜底）。
        /// </summary>
        public static TaggedString BuildOfferDescription(
            Faction? proposer,
            Faction? targetFaction,
            WorldObject? targetWorldObject,
            List<Faction> participants)
        {
            string leaderLabel = BuildProposerLeaderLabel(proposer);
            string participantsList = string.Join(
                ", ",
                participants.Select(f => f.Name));
            return "MAP_MechanoidMechanitor.Symbiosis.JointOp.Offer.Text".Translate(
                proposer?.Name ?? "???",
                leaderLabel,
                targetFaction?.Name ?? "???",
                targetWorldObject?.Label ?? "???",
                participantsList);
        }

        public static string BuildQuestName(Faction? targetFaction, WorldObject? targetWorldObject)
        {
            // Quest.Name 翻译键不含占位符，保留方法签名以减少调用方改动，但不传入无意义参数。
            return "MAP_MechanoidMechanitor.Symbiosis.JointOp.Quest.Name".Translate();
        }

        public static int ComputeRewardValue(
            float threatEstimate,
            SymbiosisCovenantJointOperationDef def)
        {
            float raw = threatEstimate * def.rewardValueFactor;
            return (int)Mathf.Clamp(raw, def.minRewardValue, def.maxRewardValue);
        }

        /// <summary>
        /// 计算“联合行动援军规模”所使用的真实目标威胁点（H）。
        /// 不再使用 StorytellerUtility.DefaultThreatPointsNow（那是按玩家殖民地规模估算的）。
        /// - Site / Outpost / WorkSite：优先读取站点自己保存的真实威胁点 Site.ActualThreatPoints；
        ///   不可用时以目标派系当前实际敌对 Pawn 的战斗力总和作为可解释 fallback。
        /// - Settlement：原版没有可靠的预存点数，直接以目标派系当前实际敌对 Pawn 的战斗力总和计算。
        /// 计算结果保存为 targetThreatPointsAtDeployment，之后不得因地图敌人死亡/读档而改变。
        /// </summary>
        public static int TryGetTargetThreatPointsAtDeployment(
            WorldObject? target,
            Map? map)
        {
            // MAPFactionOutpost 是 Site 子类，必须在本分支之前先行判定。
            // 联合援军基准 = 前哨创建时保存的守军预算快照（GarrisonThreatPoints），
            // 而非开始任务时的当前财富，也非地图敌人实际 combatPower 合计。
            if (target is MAPFactionOutpost factionOutpost)
            {
                return factionOutpost.GarrisonThreatPoints;
            }

            if (target is Site site)
            {
                float actual = site.ActualThreatPoints;
                if (actual > 0f)
                {
                    return Mathf.RoundToInt(actual);
                }

                return EstimateFromMapHostiles(map, site.Faction);
            }

            if (target is Settlement settlement)
            {
                return EstimateFromMapHostiles(map, settlement.Faction);
            }

            return EstimateFromMapHostiles(map, target?.Faction);
        }

        /// <summary>
        /// 供诊断日志使用的、本次部署目标威胁点来源标识。
        /// 不影响业务：仅描述 TryGetTargetThreatPointsAtDeployment 实际采用的数据来源。
        /// </summary>
        public static string GetTargetThreatSourceName(WorldObject? target)
        {
            if (target is MAPFactionOutpost)
            {
                return "MAPFactionOutpostSavedGarrisonBudget";
            }

            if (target is Site)
            {
                return "SiteActualThreatPoints";
            }

            if (target is Settlement)
            {
                return "Settlement";
            }

            return "MapHostilePawnCombatPowerFallback";
        }

        private static int EstimateFromMapHostiles(Map? map, Faction? faction)
        {
            if (map == null || faction == null)
            {
                return 1000;
            }

            float sum = 0f;
            foreach (Pawn pawn in map.mapPawns.SpawnedPawnsInFaction(faction))
            {
                if (!pawn.Dead && pawn.Spawned)
                {
                    sum += pawn.kindDef.combatPower;
                }
            }

            return Mathf.Max(1, Mathf.RoundToInt(sum));
        }

        public static bool ViolentQuestsAllowed =>
            Find.Storyteller != null && Find.Storyteller.difficulty.allowViolentQuests;

        /// <summary>
        /// 当前是否处于可安全生成任务的游戏上下文：非主菜单、非世界生成中、非存档加载中。
        /// </summary>
        public static bool IsInPlayableContext()
        {
            return Current.Game != null
                && Find.World != null
                && Find.TickManager != null
                && Scribe.mode == LoadSaveMode.Inactive;
        }

        public static bool IsJointOperationOngoing()
        {
            return FindActiveOperationPart() != null;
        }

        /// <summary>
        /// 判断给定世界对象是否为当前仍活动的「联合军事行动」目标。
        /// 标识从当前活动 QuestPart 动态查询，任务结束后自动消失，不会在 WorldObject 上持久保存标志。
        /// </summary>
        public static bool IsAcceptedJointOperationTarget(WorldObject? worldObject)
        {
            if (worldObject == null)
            {
                return false;
            }

            QuestPart_SymbiosisCovenantJointOperation? part = FindActiveOperationPart();
            if (part == null)
            {
                return false;
            }

            // FindActiveOperationPart 已保证 IsActive；再确认引用相等且已接取（已进入/已部署）。
            if (!part.IsOperationAccepted)
            {
                return false;
            }

            return ReferenceEquals(part.targetWorldObject, worldObject);
        }

        public static QuestPart_SymbiosisCovenantJointOperation? FindActiveOperationPart()
        {
            if (Find.QuestManager == null)
            {
                return null;
            }

            foreach (Quest quest in Find.QuestManager.QuestsListForReading)
            {
                if (quest == null
                    || quest.State == QuestState.EndedInvalid
                    || quest.State == QuestState.EndedSuccess
                    || quest.State == QuestState.EndedFailed
                    || quest.State == QuestState.EndedOfferExpired
                    || quest.State == QuestState.EndedUnknownOutcome)
                {
                    continue;
                }

                QuestPart_SymbiosisCovenantJointOperation? part =
                    quest.PartsListForReading
                        .OfType<QuestPart_SymbiosisCovenantJointOperation>()
                        .FirstOrDefault();
                if (part != null && part.IsActive)
                {
                    return part;
                }
            }

            return null;
        }

        private static bool IsTargetAlreadyInUse(WorldObject obj)
        {
            QuestPart_SymbiosisCovenantJointOperation? active = FindActiveOperationPart();
            return active != null && active.targetWorldObject == obj;
        }

        private static bool AnyCovenantMemberHostileTo(Faction? targetFaction)
        {
            GameComponent_SymbiosisCovenantState? state =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (state == null || targetFaction == null)
            {
                return false;
            }

            IReadOnlyList<SymbiosisCovenantFactionRecord> records = state.GetRecordsSorted();
            for (int i = 0; i < records.Count; i++)
            {
                Faction? faction = records[i].Faction;
                if (records[i].CovenantMember
                    && faction != null
                    && faction.HostileTo(targetFaction))
                {
                    return true;
                }
            }

            return false;
        }

        private static Faction PickWeightedByTrust(
            GameComponent_SymbiosisCovenantState state,
            List<Faction> candidates)
        {
            Faction? picked = candidates.TryRandomElementByWeight(
                faction =>
                {
                    int trust = state.GetRecord(faction)?.Trust ?? 0;
                    // 基础权重 + 随机扰动：避免永远同一派系当选。
                    return (Math.Max(0, trust) + 1f) * Rand.Range(0.8f, 1.2f);
                },
                out Faction selected)
                ? selected
                : candidates.RandomElement();
            return picked;
        }

        public static bool CanGenerateCombatGroup(Faction faction)
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
    }
}
