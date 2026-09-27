using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 肃清评级任务目标识别与排重。
    /// 只允许选择：原版/文化DLC工作站、本MOD或原版意义上的普通派系前哨、普通派系据点。
    /// 严格排除：中立/友好/盟友、玩家、机械族/机械巢派系、机械巢节点、隐藏/临时/已战败派系、
    /// 已销毁或未登记对象、已生成地图、被其他任务占用、本MOD历史/当前已用目标、无稳定ID、
    /// 以及仅仅是 Site 但既非工作站也非前哨的特殊任务地点。
    /// </summary>
    public enum PurgeDirectiveTargetType : byte
    {
        Invalid = 0,
        WorkSite,
        Outpost,
        Settlement
    }

    public static class PurgeDirectiveQuestTargetUtility
    {
        /// <summary>取得世界目标稳定ID（用于去重与结算）。失败返回 null。</summary>
        public static string? TryGetStableId(WorldObject? obj)
        {
            if (obj == null) return null;
            string? id = obj.GetUniqueLoadID();
            return string.IsNullOrEmpty(id) ? null : id;
        }

        /// <summary>解析肃清评级任务对应的 QuestScriptDef。</summary>
        public static QuestScriptDef? ResolveQuestScriptDef()
        {
            PurgeDirectiveQuestConfigDef? cfg = PurgeDirectiveQuestConfigDefOf.MAP_PurgeDirectiveQuestConfig;
            return cfg?.QuestScript;
        }

        public static bool HasStableId(WorldObject? obj)
        {
            return TryGetStableId(obj) != null;
        }

        /// <summary>
        /// 真实目标类型分类。仅工作站/前哨/据点返回有效类型，其余（含各类特殊 Site、节点）返回 Invalid。
        /// 不读取盟约成员/信任度/团结度等无关状态。
        /// </summary>
        public static PurgeDirectiveTargetType ClassifyTargetType(WorldObject? obj)
        {
            if (obj == null || obj is MAPMechHiveNode) return PurgeDirectiveTargetType.Invalid;

            if (obj is Settlement)
            {
                return PurgeDirectiveTargetType.Settlement;
            }

            if (obj is Site site)
            {
                SitePartDef? main = site.MainSitePartDef;
                if (main == null) return PurgeDirectiveTargetType.Invalid;

                // 机械巢节点及其建设中等特殊地点直接排除。
                if (IsMechHiveNodeSitePart(main))
                {
                    return PurgeDirectiveTargetType.Invalid;
                }

                if (main.Worker is SitePartWorker_WorkSite
                    || (main.tags != null && main.tags.Contains("WorkSite")))
                {
                    return PurgeDirectiveTargetType.WorkSite;
                }

                if (obj is MAPFactionOutpost
                    || main.Worker is SitePartWorker_Outpost
                    || main.Worker is SitePartWorker_FactionOutpost
                    || (main.tags != null && main.tags.Contains("Outpost")))
                {
                    return PurgeDirectiveTargetType.Outpost;
                }

                return PurgeDirectiveTargetType.Invalid;
            }

            return PurgeDirectiveTargetType.Invalid;
        }

        private static bool IsMechHiveNodeSitePart(SitePartDef def)
        {
            if (def == null) return false;
            if (def.Worker is SitePartWorker_MechHiveNode
                || def.Worker is SitePartWorker_MechCluster)
            {
                return true;
            }

            if (def.tags != null
                && (def.tags.Contains("MechHive") || def.tags.Contains("MechCluster")))
            {
                return true;
            }

            return false;
        }

        private static bool IsExcludedFaction(Faction? faction)
        {
            if (faction == null) return true;
            if (faction.IsPlayer) return true;
            if (faction.def == null) return true;
            if (faction.Hidden || faction.def.hidden) return true;
            if (faction.temporary || faction.deactivated || faction.defeated) return true;
            if (Find.FactionManager?.AllFactionsListForReading.Contains(faction) != true) return true;
            if (faction.def == FactionDefOf.Mechanoid) return true;

            // 兼容第三方机械巢派系 Def：以当前实际机械巢实例为准，不只比较原版 FactionDef。
            Faction? mechHive = MechanoidMechanitorOrdinaryFactionUtility.TryGetMechHive();
            if (mechHive != null && faction == mechHive) return true;
            return false;
        }

        /// <summary>运行中的任务也复核实际归属；不包含地图生成、历史去重或任务占用限制。</summary>
        internal static bool IsHostileTargetFaction(Faction? faction)
        {
            Faction? player = Faction.OfPlayerSilentFail;
            return player != null
                && !IsExcludedFaction(faction)
                && faction!.RelationWith(player, allowNull: true)?.kind == FactionRelationKind.Hostile;
        }

        /// <summary>目标是否仍可作为合法肃清目标（含派系敌对、登记、未生成地图、未被占用/已用等）。</summary>
        public static bool IsValidTarget(WorldObject? obj)
        {
            if (obj == null) return false;
            if (!HasStableId(obj)) return false;
            if (obj.Destroyed) return false;
            if (!obj.Spawned) return false; // 未登记在当前世界对象列表中
            if (obj is MapParent mapParent && mapParent.HasMap) return false;

            PurgeDirectiveTargetType type = ClassifyTargetType(obj);
            if (type == PurgeDirectiveTargetType.Invalid) return false;
            PurgeDirectiveRatingConfigDef cfg = PurgeDirectiveRatingUtility.Config;
            if (type == PurgeDirectiveTargetType.Settlement
                && Find.TickManager.TicksGame < cfg.questSettlementMinDaysPassed * 60000) return false;
            if (!TryGetOperationTimeoutTicks(obj, out _)) return false;

            Faction? faction = obj.Faction;
            if (!IsHostileTargetFaction(faction)) return false;

            string stableId = TryGetStableId(obj)!;
            MechanoidMechanitorPurgeDirectiveRuntimeState? rs = PurgeDirectiveRatingUtility.Runtime;
            if (rs != null && rs.HasUsedQuestTarget(stableId)) return false;
            if (IsTargetOccupiedByOtherQuest(obj)) return false;

            return true;
        }

        /// <summary>该世界对象是否正被其他（非肃清评级）任务占用。</summary>
        public static bool IsTargetOccupiedByOtherQuest(WorldObject? obj)
        {
            if (obj == null) return false;

            // 保守保留原版/第三方标签，只忽略旧版本遗留的、已结束的本 MOD 联合行动标签。
            if (obj.questTags != null)
            {
                foreach (string tag in obj.questTags)
                {
                    if (!IsInactiveJointOperationTag(tag)) return true;
                }
            }

            string? stableId = TryGetStableId(obj);
            if (stableId == null) return false;

            List<Quest> quests = Find.QuestManager.QuestsListForReading;
            for (int i = 0; i < quests.Count; i++)
            {
                Quest q = quests[i];
                if (q == null) continue;
                if (q.State != QuestState.Ongoing && q.State != QuestState.NotYetAccepted) continue;
                foreach (QuestPart part in q.PartsListForReading)
                {
                    if (part is PurgeDirectiveQuestPart p && p.targetStableId == stableId) return true;
                }
            }

            return false;
        }

        private static bool IsInactiveJointOperationTag(string? tag)
        {
            const string suffix = ".MAP_SymbiosisCovenantJointOp_Target";
            if (tag == null || !tag.StartsWith("Quest", StringComparison.Ordinal)
                || !tag.EndsWith(suffix, StringComparison.Ordinal)
                || tag.Length <= "Quest".Length + suffix.Length
                || !int.TryParse(
                    tag.Substring("Quest".Length, tag.Length - "Quest".Length - suffix.Length),
                    out int questId))
            {
                return false;
            }

            Quest? owner = Find.QuestManager.QuestsListForReading.Find(q => q.id == questId);
            return owner == null || owner.Historical;
        }

        /// <summary>
        /// 固定按前哨、工作站、派系据点选择目标；评级不再改变优先级。
        /// 同优先级内随机选择（不永远返回列表第一个）。
        /// </summary>
        public static WorldObject? SelectTarget(int ratingLevel)
        {
            // 保留参数以兼容既有调用方；所有评级使用同一目标优先级。
            List<WorldObject> worksites = new List<WorldObject>();
            List<WorldObject> outposts = new List<WorldObject>();
            List<WorldObject> settlements = new List<WorldObject>();

            List<WorldObject> all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                WorldObject obj = all[i];
                if (!IsValidTarget(obj)) continue;
                switch (ClassifyTargetType(obj))
                {
                    case PurgeDirectiveTargetType.WorkSite:
                        worksites.Add(obj);
                        break;
                    case PurgeDirectiveTargetType.Outpost:
                        outposts.Add(obj);
                        break;
                    case PurgeDirectiveTargetType.Settlement:
                        settlements.Add(obj);
                        break;
                }
            }

            List<List<WorldObject>> ordered = new List<List<WorldObject>> { outposts, worksites, settlements };
            for (int i = 0; i < ordered.Count; i++)
            {
                List<WorldObject> bucket = ordered[i];
                if (bucket.Count == 0) continue;
                return bucket.RandomElement();
            }

            return null;
        }

        /// <summary>只在发布时读取建设阶段；星级随后由 Quest 自身存档，不随前哨完工改变。</summary>
        internal static int GetChallengeRatingAtGeneration(WorldObject target)
        {
            if (target is MAPFactionOutpost outpost && outpost.IsBuilding) return 1;
            return ClassifyTargetType(target) == PurgeDirectiveTargetType.Settlement ? 3 : 2;
        }

        /// <summary>读取发布时的精确期限；工作站无有效寿命或不足最低剩余时间时不发布。</summary>
        internal static bool TryGetOperationTimeoutTicks(WorldObject target, out int ticks)
        {
            ticks = 0;
            PurgeDirectiveRatingConfigDef cfg = PurgeDirectiveRatingUtility.Config;
            switch (ClassifyTargetType(target))
            {
                case PurgeDirectiveTargetType.WorkSite:
                    TimeoutComp? timeout = target.GetComponent<TimeoutComp>();
                    if (timeout == null || !timeout.Active) return false;
                    ticks = timeout.TicksLeft;
                    return ticks >= cfg.questWorkSiteMinRemainingDays * 60000;
                case PurgeDirectiveTargetType.Outpost:
                    ticks = cfg.questOutpostTimeoutDays * 60000;
                    break;
                case PurgeDirectiveTargetType.Settlement:
                    ticks = cfg.questSettlementTimeoutDays * 60000;
                    break;
            }
            return ticks > 0;
        }

        /// <summary>整日显示日数，否则总小时数向下取整；不改变实际截止 tick。</summary>
        internal static string FormatOperationDuration(int ticks)
        {
            return ticks % 60000 == 0
                ? "MAP_PurgeDirectiveRating.Quest.DurationDays".Translate(ticks / 60000).ToString()
                : "MAP_PurgeDirectiveRating.Quest.DurationHours".Translate(ticks / 2500).ToString();
        }
    }
}
