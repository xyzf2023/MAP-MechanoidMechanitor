using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class MechanoidMechanitorOrdinaryFactionUtility
    {
        public static Faction? TryGetMechHive()
        {
            FactionManager? factionManager = Find.FactionManager;
            if (factionManager == null)
            {
                return null;
            }

            return TryGetMechHive(factionManager, factionManager.AllFactionsListForReading);
        }

        public static bool IsOrdinaryFaction(Faction? faction)
        {
            FactionManager? factionManager = Find.FactionManager;
            if (factionManager == null)
            {
                return false;
            }

            List<Faction> currentFactions = factionManager.AllFactionsListForReading;
            Faction? mechHive = TryGetMechHive(factionManager, currentFactions);
            return IsOrdinaryFaction(faction, currentFactions, mechHive);
        }

        public static List<Faction> GetOrdinaryFactionsSorted()
        {
            List<Faction> ordinaryFactions = new List<Faction>();
            CollectOrdinaryFactions(ordinaryFactions);
            ordinaryFactions.Sort(CompareOrdinaryFactions);
            return ordinaryFactions;
        }

        public static void CollectOrdinaryFactions(List<Faction> dest)
        {
            dest.Clear();
            FactionManager? factionManager = Find.FactionManager;
            if (factionManager == null)
            {
                return;
            }

            List<Faction> currentFactions = factionManager.AllFactionsListForReading;
            Faction? mechHive = TryGetMechHive(factionManager, currentFactions);
            for (int i = 0; i < currentFactions.Count; i++)
            {
                Faction faction = currentFactions[i];
                if (IsOrdinaryFaction(faction, currentFactions, mechHive))
                {
                    dest.Add(faction);
                }
            }
        }

        public static bool TryGetPlayerAndOrdinary(
            Faction a,
            Faction b,
            out Faction player,
            out Faction ordinary)
        {
            player = null!;
            ordinary = null!;
            if (a == null || b == null || a == b)
            {
                return false;
            }

            if (a.IsPlayer && !b.IsPlayer)
            {
                player = a;
                ordinary = b;
                return IsOrdinaryFaction(ordinary);
            }

            if (b.IsPlayer && !a.IsPlayer)
            {
                player = b;
                ordinary = a;
                return IsOrdinaryFaction(ordinary);
            }

            return false;
        }

        internal static Faction? TryGetMechHive(
            FactionManager factionManager,
            List<Faction> currentFactions)
        {
            Faction? mechHive = factionManager.OfMechanoids;
            if (mechHive == null)
            {
                return null;
            }

            if (!currentFactions.Contains(mechHive))
            {
                return null;
            }

            return mechHive;
        }

        internal static bool IsOrdinaryFaction(
            Faction? faction,
            List<Faction> currentFactions,
            Faction? mechHive)
        {
            if (faction == null)
            {
                return false;
            }

            if (!currentFactions.Contains(faction))
            {
                return false;
            }

            if (faction.IsPlayer || faction.Hidden || faction.temporary)
            {
                return false;
            }

            if (mechHive != null && faction == mechHive)
            {
                return false;
            }

            if (!faction.HasGoodwill)
            {
                return false;
            }

            if (faction.def == null || faction.def.permanentEnemy)
            {
                return false;
            }

            return true;
        }

        private static int CompareOrdinaryFactions(Faction left, Faction right)
        {
            int nameComparison = string.Compare(
                left.Name,
                right.Name,
                StringComparison.OrdinalIgnoreCase);
            if (nameComparison != 0)
            {
                return nameComparison;
            }

            return left.loadID.CompareTo(right.loadID);
        }
    }
}
