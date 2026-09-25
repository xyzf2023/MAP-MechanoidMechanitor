using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(
        typeof(HistoryAutoRecorderWorker_ColonistMood),
        nameof(HistoryAutoRecorderWorker_ColonistMood.PullRecord))]
    public static class MechanoidMechanitorScenario_ColonistMoodHistory_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref float __result)
        {
            if (!GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
            {
                return true;
            }

            var freeColonists = PawnsFinder.AllMaps_FreeColonists;
            int moodCount = 0;
            double moodTotal = 0d;
            bool hasInvalidNeeds = false;

            foreach (Pawn pawn in freeColonists)
            {
                if (pawn?.needs == null)
                {
                    hasInvalidNeeds = true;
                    continue;
                }

                Need_Mood mood = pawn.needs.mood;
                if (mood == null)
                {
                    continue;
                }

                moodTotal += mood.CurLevel * 100f;
                moodCount++;
            }

            // 有有效样本且原版可以安全访问需求时，保留原版统计及其他补丁的处理。
            if (moodCount > 0 && !hasInvalidNeeds)
            {
                return true;
            }

            // 原版仅在过滤前检查空集合；纯机械殖民地会在过滤后对空序列求平均。
            // 无样本沿用原版的 0；存在空角色或空需求时，仅统计有效心情样本。
            __result = moodCount > 0 ? (float)(moodTotal / moodCount) : 0f;
            return false;
        }
    }
}
