using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidMechanitorStoryConfigurationContext
    {
        private readonly List<Faction> ordinaryFactions;

        public MechanoidMechanitorStoryConfiguration Configuration { get; }

        public IReadOnlyList<Faction> OrdinaryFactions => ordinaryFactions;

        public Faction? MechHive { get; }

        public bool HasOrdinaryFactions => ordinaryFactions.Count > 0;

        public bool HasMechHive => MechHive != null;

        private MechanoidMechanitorStoryConfigurationContext(
            MechanoidMechanitorStoryConfiguration configuration,
            List<Faction> ordinaryFactions,
            Faction? mechHive)
        {
            Configuration = configuration;
            this.ordinaryFactions = ordinaryFactions;
            MechHive = mechHive;
        }

        public static MechanoidMechanitorStoryConfigurationContext Create(
            MechanoidMechanitorStoryConfiguration configuration)
        {
            FactionManager? factionManager = Find.FactionManager;
            if (factionManager == null)
            {
                return new MechanoidMechanitorStoryConfigurationContext(
                    configuration,
                    new List<Faction>(),
                    null);
            }

            List<Faction> currentFactions = factionManager.AllFactionsListForReading;
            Faction? mechHive = TryGetMechHive(factionManager, currentFactions);
            List<Faction> ordinaryFactions = new List<Faction>();

            for (int i = 0; i < currentFactions.Count; i++)
            {
                Faction faction = currentFactions[i];
                if (IsOrdinaryFaction(faction, currentFactions, mechHive))
                {
                    ordinaryFactions.Add(faction);
                }
            }

            ordinaryFactions.Sort(CompareOrdinaryFactions);
            return new MechanoidMechanitorStoryConfigurationContext(
                configuration,
                ordinaryFactions,
                mechHive);
        }

        public static List<Faction> GetOrdinaryFactionsSorted()
        {
            FactionManager? factionManager = Find.FactionManager;
            if (factionManager == null)
            {
                return new List<Faction>();
            }

            List<Faction> currentFactions = factionManager.AllFactionsListForReading;
            Faction? mechHive = TryGetMechHive(factionManager, currentFactions);
            List<Faction> ordinaryFactions = new List<Faction>();
            for (int i = 0; i < currentFactions.Count; i++)
            {
                Faction faction = currentFactions[i];
                if (IsOrdinaryFaction(faction, currentFactions, mechHive))
                {
                    ordinaryFactions.Add(faction);
                }
            }

            ordinaryFactions.Sort(CompareOrdinaryFactions);
            return ordinaryFactions;
        }

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

        private static Faction? TryGetMechHive(
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

        private static bool IsOrdinaryFaction(
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
