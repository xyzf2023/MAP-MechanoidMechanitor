using System;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class NewMechHiveNameUtility
    {
        private static readonly string[] GreekPrefixKeys =
        {
            "MAP_MechanoidMechanitor.MechHiveNode.NewHive.Greek.Alpha",
            "MAP_MechanoidMechanitor.MechHiveNode.NewHive.Greek.Beta",
            "MAP_MechanoidMechanitor.MechHiveNode.NewHive.Greek.Gamma",
            "MAP_MechanoidMechanitor.MechHiveNode.NewHive.Greek.Delta",
            "MAP_MechanoidMechanitor.MechHiveNode.NewHive.Greek.Epsilon",
            "MAP_MechanoidMechanitor.MechHiveNode.NewHive.Greek.Zeta",
            "MAP_MechanoidMechanitor.MechHiveNode.NewHive.Greek.Eta",
            "MAP_MechanoidMechanitor.MechHiveNode.NewHive.Greek.Theta",
            "MAP_MechanoidMechanitor.MechHiveNode.NewHive.Greek.Iota",
            "MAP_MechanoidMechanitor.MechHiveNode.NewHive.Greek.Kappa",
            "MAP_MechanoidMechanitor.MechHiveNode.NewHive.Greek.Lambda",
            "MAP_MechanoidMechanitor.MechHiveNode.NewHive.Greek.Xi",
            "MAP_MechanoidMechanitor.MechHiveNode.NewHive.Greek.Omicron",
            "MAP_MechanoidMechanitor.MechHiveNode.NewHive.Greek.Sigma",
            "MAP_MechanoidMechanitor.MechHiveNode.NewHive.Greek.Upsilon",
            "MAP_MechanoidMechanitor.MechHiveNode.NewHive.Greek.Psi",
            "MAP_MechanoidMechanitor.MechHiveNode.NewHive.Greek.Omega"
        };

        public static string GenerateFactionName(
            Func<string, bool>? isValidName,
            string? currentName = null)
        {
            const int maxAttempts = 200;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                string candidate = GreekPrefixKeys.RandomElement().Translate() + "节点";
                if (candidate == currentName)
                {
                    // 排除当前显示的名称，避免点击“随机”后结果不变。
                    continue;
                }

                if (isValidName == null || isValidName(candidate))
                {
                    return candidate;
                }
            }

            // 兜底：在所有随机尝试都撞上当前名称时，按顺序找一个不同的有效名称。
            foreach (string key in GreekPrefixKeys)
            {
                string candidate = key.Translate() + "节点";
                if (candidate == currentName)
                {
                    continue;
                }

                if (isValidName == null || isValidName(candidate))
                {
                    return candidate;
                }
            }

            return GreekPrefixKeys.RandomElement().Translate() + "节点";
        }

        public static string GenerateSettlementName(
            Func<string, bool>? isValidName,
            string? currentName = null)
        {
            const int maxAttempts = 200;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                string candidate = "节点-" + Rand.RangeInclusive(1, 999);
                if (candidate == currentName)
                {
                    // 排除当前显示的名称，避免点击“随机”后结果不变。
                    continue;
                }

                if (isValidName == null || isValidName(candidate))
                {
                    return candidate;
                }
            }

            // 兜底：遍历所有编号找一个不同于当前名称的有效结果。
            for (int n = 1; n <= 999; n++)
            {
                string candidate = "节点-" + n;
                if (candidate == currentName)
                {
                    continue;
                }

                if (isValidName == null || isValidName(candidate))
                {
                    return candidate;
                }
            }

            return "节点-" + Rand.RangeInclusive(1, 999);
        }
    }
}
