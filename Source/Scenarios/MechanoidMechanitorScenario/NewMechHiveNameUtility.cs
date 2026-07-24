using System;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class NewMechHiveNameUtility
    {
        private static readonly string[] GreekPrefixKeys =
        {
            "MAP_MechanoidMechanitor.NewMechHive.Greek.Alpha",
            "MAP_MechanoidMechanitor.NewMechHive.Greek.Beta",
            "MAP_MechanoidMechanitor.NewMechHive.Greek.Gamma",
            "MAP_MechanoidMechanitor.NewMechHive.Greek.Delta",
            "MAP_MechanoidMechanitor.NewMechHive.Greek.Epsilon",
            "MAP_MechanoidMechanitor.NewMechHive.Greek.Zeta",
            "MAP_MechanoidMechanitor.NewMechHive.Greek.Eta",
            "MAP_MechanoidMechanitor.NewMechHive.Greek.Theta",
            "MAP_MechanoidMechanitor.NewMechHive.Greek.Iota",
            "MAP_MechanoidMechanitor.NewMechHive.Greek.Kappa",
            "MAP_MechanoidMechanitor.NewMechHive.Greek.Lambda",
            "MAP_MechanoidMechanitor.NewMechHive.Greek.Xi",
            "MAP_MechanoidMechanitor.NewMechHive.Greek.Omicron",
            "MAP_MechanoidMechanitor.NewMechHive.Greek.Sigma",
            "MAP_MechanoidMechanitor.NewMechHive.Greek.Upsilon",
            "MAP_MechanoidMechanitor.NewMechHive.Greek.Psi",
            "MAP_MechanoidMechanitor.NewMechHive.Greek.Omega"
        };

        public static string GenerateFactionName(Func<string, bool>? isValidName)
        {
            const int maxAttempts = 100;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                string candidate = GreekPrefixKeys.RandomElement().Translate() + "节点";

                if (isValidName == null || isValidName(candidate))
                {
                    return candidate;
                }
            }

            return GreekPrefixKeys.RandomElement().Translate() + "节点";
        }

        public static string GenerateSettlementName(Func<string, bool>? isValidName)
        {
            const int maxAttempts = 100;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                string candidate = "节点-" + Rand.RangeInclusive(1, 999);

                if (isValidName == null || isValidName(candidate))
                {
                    return candidate;
                }
            }

            return "节点-" + Rand.RangeInclusive(1, 999);
        }
    }
}
