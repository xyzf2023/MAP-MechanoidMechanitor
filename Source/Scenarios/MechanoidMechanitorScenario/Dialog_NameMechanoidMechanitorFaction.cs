using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class Dialog_NameMechanoidMechanitorFaction
        : Dialog_NamePlayerFaction
    {
        private static readonly string[] GreekPrefixes =
        {
            "阿尔法",
            "贝塔",
            "伽马",
            "德尔塔",
            "艾普西隆",
            "泽塔",
            "伊塔",
            "西塔",
            "约塔",
            "卡帕",
            "拉姆达",
            "克西",
            "奥密克戎",
            "西格玛",
            "宇普西隆",
            "普西",
            "欧米伽"
        };

        public Dialog_NameMechanoidMechanitorFaction()
        {
            nameGenerator = GenerateRandomFactionName;
            curName = nameGenerator();
        }

        private string GenerateRandomFactionName()
        {
            const int maxAttempts = 100;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                string prefix = GreekPrefixes.RandomElement();
                int number = Rand.RangeInclusive(1, 999);
                string candidate = prefix + "节点-" + number;

                if (IsValidName(candidate))
                {
                    return candidate;
                }
            }

            return "阿尔法节点-" + Rand.RangeInclusive(1, 999);
        }
    }
}
