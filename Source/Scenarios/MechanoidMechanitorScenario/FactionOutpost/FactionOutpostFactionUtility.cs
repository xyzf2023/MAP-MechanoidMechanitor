using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 普通派系前哨自己的候选资格入口。这里刻意不复用 OrdinaryFactionUtility：
    /// 后者服务于可调整善意的剧情关系设置，会排除 permanentEnemy；前哨必须允许海盗等
    /// 永久敌对的人类派系进入敌对候选池。
    /// </summary>
    public static class FactionOutpostFactionUtility
    {
        public static bool HasAnyEligibleFaction()
        {
            List<Faction> factions = new List<Faction>();
            CollectEligibleFactions(factions);
            return factions.Count > 0;
        }

        public static void CollectEligibleFactions(List<Faction> dest)
        {
            dest.Clear();
            FactionManager? manager = Find.FactionManager;
            if (manager == null)
            {
                return;
            }

            Faction? mechHive = manager.OfMechanoids;
            List<Faction> factions = manager.AllFactionsListForReading;
            for (int i = 0; i < factions.Count; i++)
            {
                Faction? faction = factions[i];
                if (IsEligibleFaction(faction, mechHive))
                {
                    dest.Add(faction!);
                }
            }
        }

        public static bool IsEligibleFaction(Faction? faction)
        {
            FactionManager? manager = Find.FactionManager;
            return manager != null && IsEligibleFaction(faction, manager.OfMechanoids);
        }

        private static bool IsEligibleFaction(Faction? faction, Faction? mechHive)
        {
            if (faction == null
                || faction.IsPlayer
                || faction.Hidden
                || faction.temporary
                || faction.defeated
                || faction.deactivated
                || (mechHive != null && faction == mechHive))
            {
                return false;
            }

            FactionDef? def = faction.def;
            if (def == null || !def.humanlikeFaction || def.raidsForbidden)
            {
                return false;
            }

            List<PawnGroupMaker>? makers = def.pawnGroupMakers;
            if (makers == null || makers.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < makers.Count; i++)
            {
                PawnGroupMaker? maker = makers[i];
                if (maker != null && maker.kindDef == PawnGroupKindDefOf.Combat)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
