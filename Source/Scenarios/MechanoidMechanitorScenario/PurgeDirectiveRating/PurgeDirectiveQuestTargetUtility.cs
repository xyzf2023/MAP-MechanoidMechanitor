using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 肃清评级任务目标选择：只选择已经敌对玩家的「普通派系」工作站、前哨和据点
    /// （Settlement / Site），排除机械族与机械巢，且排除已被当前进行中肃清任务锁定的目标。
    /// </summary>
    public static class PurgeDirectiveQuestTargetUtility
    {
        public static bool IsValidTarget(WorldObject worldObject)
        {
            if (worldObject == null || worldObject.Destroyed)
            {
                return false;
            }

            if (worldObject.Faction == null)
            {
                return false;
            }

            Faction faction = worldObject.Faction;
            if (faction == Faction.OfPlayer || faction == Faction.OfMechanoids)
            {
                return false;
            }

            // 排除机械巢（玩家联络的机械族主脑派系）。
            Faction? mechHive = MechanoidMechanitorOrdinaryFactionUtility.TryGetMechHive();
            if (mechHive != null && faction == mechHive)
            {
                return false;
            }

            // 仅普通派系，且当前必须敌对玩家。
            if (faction.def == null || faction.def.isPlayer || !faction.HostileTo(Faction.OfPlayer))
            {
                return false;
            }

            if (worldObject is Settlement)
            {
                return true;
            }

            if (worldObject is Site)
            {
                return true;
            }

            return false;
        }

        public static WorldObject? TryGetValidTarget()
        {
            if (!PurgeDirectiveRatingUtility.IsRatingSystemActive())
            {
                return null;
            }

            HashSet<WorldObject> alreadyTargeted = GetCurrentlyTargetedWorldObjects();

            // 优先据点（Settlement），其次站点（Site / 前哨）。
            foreach (Settlement settlement in Find.WorldObjects.Settlements)
            {
                if (IsValidTarget(settlement) && !alreadyTargeted.Contains(settlement))
                {
                    return settlement;
                }
            }

            foreach (Site site in Find.WorldObjects.Sites)
            {
                if (IsValidTarget(site) && !alreadyTargeted.Contains(site))
                {
                    return site;
                }
            }

            return null;
        }

        private static HashSet<WorldObject> GetCurrentlyTargetedWorldObjects()
        {
            HashSet<WorldObject> result = new HashSet<WorldObject>();
            QuestScriptDef? questScriptDef = ResolveQuestScriptDef();
            if (questScriptDef == null)
            {
                return result;
            }

            foreach (Quest quest in Find.QuestManager.QuestsListForReading)
            {
                if (quest.root != questScriptDef || quest.State != QuestState.Ongoing)
                {
                    continue;
                }

                foreach (QuestPart part in quest.PartsListForReading)
                {
                    if (part is PurgeDirectiveQuestPart purgePart
                        && purgePart.targetWorldObject != null)
                    {
                        result.Add(purgePart.targetWorldObject);
                    }
                }
            }

            return result;
        }

        public static QuestScriptDef? ResolveQuestScriptDef()
        {
            string? name =
                PurgeDirectiveQuestConfigDefOf.MAP_PurgeDirectiveQuestConfig?.questScriptDefName;
            if (name == null)
            {
                return null;
            }

            return DefDatabase<QuestScriptDef>.GetNamedSilentFail(name);
        }
    }
}
