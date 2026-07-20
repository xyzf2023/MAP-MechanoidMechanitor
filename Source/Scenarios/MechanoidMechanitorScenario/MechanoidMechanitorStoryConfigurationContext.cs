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

        public bool HasPursuingMechanoidsScenarioPart { get; }

        private MechanoidMechanitorStoryConfigurationContext(
            MechanoidMechanitorStoryConfiguration configuration,
            List<Faction> ordinaryFactions,
            Faction? mechHive,
            bool hasPursuingMechanoidsScenarioPart)
        {
            Configuration = configuration;
            this.ordinaryFactions = ordinaryFactions;
            MechHive = mechHive;
            HasPursuingMechanoidsScenarioPart = hasPursuingMechanoidsScenarioPart;
        }

        public static MechanoidMechanitorStoryConfigurationContext Create(
            MechanoidMechanitorStoryConfiguration configuration)
        {
            List<Faction> ordinaryFactions =
                MechanoidMechanitorOrdinaryFactionUtility.GetOrdinaryFactionsSorted();
            return new MechanoidMechanitorStoryConfigurationContext(
                configuration,
                ordinaryFactions,
                MechanoidMechanitorOrdinaryFactionUtility.TryGetMechHive(),
                DetectPursuingMechanoidsScenarioPart());
        }

        public static List<Faction> GetOrdinaryFactionsSorted()
        {
            return MechanoidMechanitorOrdinaryFactionUtility.GetOrdinaryFactionsSorted();
        }

        public static Faction? TryGetMechHive()
        {
            return MechanoidMechanitorOrdinaryFactionUtility.TryGetMechHive();
        }

        public static bool IsOrdinaryFaction(Faction? faction)
        {
            return MechanoidMechanitorOrdinaryFactionUtility.IsOrdinaryFaction(faction);
        }

        private static bool DetectPursuingMechanoidsScenarioPart()
        {
            if (!ModsConfig.OdysseyActive)
            {
                return false;
            }

            Scenario? scenario = Find.Scenario;
            if (scenario == null)
            {
                return false;
            }

            foreach (ScenPart part in scenario.AllParts)
            {
                if (part is ScenPart_PursuingMechanoids)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
