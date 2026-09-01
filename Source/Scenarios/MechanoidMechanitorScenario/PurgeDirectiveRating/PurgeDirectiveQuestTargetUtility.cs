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
            if (obj == null) return PurgeDirectiveTargetType.Invalid;

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

                if (main.tags != null && main.tags.Contains("WorkSite"))
                {
                    return PurgeDirectiveTargetType.WorkSite;
                }

                if (main.tags != null && main.tags.Contains("Outpost"))
                {
                    return PurgeDirectiveTargetType.Outpost;
                }

                // 兜底：以 defName 中是否含 Outpost 识别普通派系前哨（不读取盟约/援军状态）。
                if (main.defName.IndexOf("Outpost", StringComparison.OrdinalIgnoreCase) >= 0)
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
            if (def.defName.IndexOf("MechHive", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (def.defName.IndexOf("MechCluster", StringComparison.OrdinalIgnoreCase) >= 0)
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
            if (faction.def == FactionDefOf.Mechanoid) return true;

            // 兼容第三方机械巢派系 Def：以当前实际机械巢实例为准，不只比较原版 FactionDef。
            Faction? mechHive = MechanoidMechanitorOrdinaryFactionUtility.TryGetMechHive();
            if (mechHive != null && faction == mechHive) return true;
            return false;
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

            Faction? faction = obj.Faction;
            if (IsExcludedFaction(faction)) return false;
            if (!faction!.HostileTo(Faction.OfPlayer)) return false; // 中立/友好/盟友排除

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

            // 原版及第三方任务通常会把占用标记写入世界对象 questTags。
            // 保守排除任何已有标记的目标，避免把同一地点分配给两条任务线。
            if (obj.questTags != null && obj.questTags.Count > 0) return true;

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

        /// <summary>
        /// 按当前评级选择目标：评级只影响优先顺序，最高类别无目标时允许回退。
        /// 同优先级内随机选择（不永远返回列表第一个）。
        /// 五级高威胁前哨：仅在目标已生成地图、存在可靠守军威胁快照时按威胁排序，否则退化为同类随机。
        /// </summary>
        public static WorldObject? SelectTarget(int ratingLevel)
        {
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

            List<List<WorldObject>> ordered = GetOrderedCategories(ratingLevel, worksites, outposts, settlements);
            for (int i = 0; i < ordered.Count; i++)
            {
                List<WorldObject> bucket = ordered[i];
                if (bucket.Count == 0) continue;
                if (ratingLevel >= 5 && bucket == outposts)
                {
                    WorldObject? best = PickHighestThreatOutpostIfReliable(bucket);
                    if (best != null) return best;
                }

                return bucket.RandomElement();
            }

            return null;
        }

        private static List<List<WorldObject>> GetOrderedCategories(
            int ratingLevel,
            List<WorldObject> worksites,
            List<WorldObject> outposts,
            List<WorldObject> settlements)
        {
            switch (ratingLevel)
            {
                case 1:
                    return new List<List<WorldObject>> { worksites, outposts, settlements };
                case 2:
                    return new List<List<WorldObject>> { outposts, worksites, settlements };
                case 3:
                    return new List<List<WorldObject>> { outposts, settlements, worksites };
                case 4:
                    return new List<List<WorldObject>> { settlements, outposts, worksites };
                default: // 5
                    return new List<List<WorldObject>> { settlements, outposts, worksites };
            }
        }

        /// <summary>
        /// 仅在目标已生成地图、存在可靠守军威胁快照时按威胁排序；否则返回 null 退化为同类随机。
        /// 不为了读取威胁值而生成目标地图。
        /// </summary>
        private static WorldObject? PickHighestThreatOutpostIfReliable(List<WorldObject> outposts)
        {
            // 合法候选明确排除已生成地图的地点；未生成地图时没有可靠、统一的实际守军
            // 威胁快照，因此不猜测第三方 Site 的强度，交由调用方在同类中随机选择。
            return null;
        }
    }
}
