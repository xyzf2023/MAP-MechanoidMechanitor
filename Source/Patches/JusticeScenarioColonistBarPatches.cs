using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 在机械族机械师专属剧本中，扩展 ColonistBar 远行队分组的殖民者头像筛选，
    /// 不修改 Pawn.IsColonist 的全局语义。
    /// </summary>
    [HarmonyPatch(typeof(ColonistBar), "CheckRecacheEntries")]
    public static class JusticeScenario_ColonistBar_CheckRecacheEntries_Patch
    {
        private const string LogPrefix =
            "[MAP_MechanoidMechanitor] JusticeScenarioColonistBarPatches:";

        private const int MaxInstructionsAfterIsColonist = 8;

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            MethodInfo? isColonistGetter = AccessTools.PropertyGetter(
                typeof(Pawn),
                nameof(Pawn.IsColonist));
            MethodInfo? isColonySubhumanGetter = AccessTools.PropertyGetter(
                typeof(Pawn),
                nameof(Pawn.IsColonySubhumanPlayerControlled));
            MethodInfo? helperMethod = AccessTools.Method(
                typeof(JusticeScenarioFreeColonistUtility),
                nameof(JusticeScenarioFreeColonistUtility.CountsAsColonistForCaravanBar));

            if (isColonistGetter == null
                || isColonySubhumanGetter == null
                || helperMethod == null)
            {
                Log.Error(
                    $"{LogPrefix} could not resolve ColonistBar caravan filter methods. Patch not applied.");
                return codes;
            }

            int matchCount = 0;

            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(isColonistGetter))
                {
                    continue;
                }

                bool followedBySubhumanCheck = false;
                int searchLimit = i + MaxInstructionsAfterIsColonist;
                if (searchLimit > codes.Count)
                {
                    searchLimit = codes.Count;
                }

                for (int j = i + 1; j < searchLimit; j++)
                {
                    if (codes[j].Calls(isColonySubhumanGetter))
                    {
                        followedBySubhumanCheck = true;
                        break;
                    }
                }

                if (!followedBySubhumanCheck)
                {
                    continue;
                }

                codes[i] = new CodeInstruction(OpCodes.Call, helperMethod);
                matchCount++;
            }

            if (matchCount != 1)
            {
                Log.Error(
                    $"{LogPrefix} expected exactly one ColonistBar caravan IsColonist check, found {matchCount}. Patch not applied.");
            }

            return codes;
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspilerAppendPortraitDisplayEntries(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            FieldInfo? reorderableGroupsField = AccessTools.Field(
                typeof(ColonistBar),
                "cachedReorderableGroups");
            MethodInfo? clearMethod = AccessTools.Method(
                typeof(List<int>),
                nameof(List<int>.Clear));
            MethodInfo? appendMethod = AccessTools.Method(
                typeof(JusticeScenarioColonistBarPortraitUtility),
                nameof(JusticeScenarioColonistBarPortraitUtility.AppendMapPortraitDisplayEntries));

            if (reorderableGroupsField == null
                || clearMethod == null
                || appendMethod == null)
            {
                Log.Error(
                    $"{LogPrefix} could not resolve ColonistBar portrait append methods. Patch not applied.");
                return codes;
            }

            bool injected = false;

            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode != OpCodes.Ldfld
                    || !ReferenceEquals(codes[i].operand, reorderableGroupsField))
                {
                    continue;
                }

                if (i + 1 >= codes.Count || !codes[i + 1].Calls(clearMethod))
                {
                    continue;
                }

                codes.InsertRange(
                    i,
                    new[]
                    {
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Call, appendMethod)
                    });
                injected = true;
                break;
            }

            if (!injected)
            {
                Log.Error(
                    $"{LogPrefix} could not inject ColonistBar portrait append call. Patch not applied.");
            }

            return codes;
        }
    }
}
