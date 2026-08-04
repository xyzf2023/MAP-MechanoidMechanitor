using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [StaticConstructorOnStartup]
    internal static class JusticeBossDiagnosticStartupSafety
    {
        private const string HarmonyId =
            "MAP_MechanoidMechanitor.JusticeBossDiagnosticStartupSafety";

        static JusticeBossDiagnosticStartupSafety()
        {
            Harmony harmony = new Harmony(HarmonyId);
            try
            {
                MethodInfo? transpiler = AccessTools.Method(
                    typeof(JusticeBossDiagnosticStartupSafety),
                    nameof(ReplaceUnsafeTickManagerGetter));
                if (transpiler == null)
                {
                    throw new MissingMethodException(
                        typeof(JusticeBossDiagnosticStartupSafety).FullName,
                        nameof(ReplaceUnsafeTickManagerGetter));
                }

                PatchRequired(
                    harmony,
                    "JusticeBossDiagnosticUtility.Write",
                    AccessTools.Method(
                        typeof(JusticeBossDiagnosticUtility),
                        "Write",
                        new[]
                        {
                            typeof(string),
                            typeof(string),
                            typeof(double),
                            typeof(bool),
                        }),
                    transpiler);

                PatchRequired(
                    harmony,
                    "JusticeBossCriticalTrace.WriteLineUnsafe",
                    AccessTools.Method(
                        typeof(JusticeBossCriticalTrace),
                        "WriteLineUnsafe",
                        new[]
                        {
                            typeof(string),
                            typeof(string),
                            typeof(bool),
                        }),
                    transpiler);
            }
            catch (Exception exception)
            {
                harmony.UnpatchAll(HarmonyId);
                Log.Error(
                    "[MAP JusticeBoss Diagnostic] 启动期安全补丁安装失败："
                        + exception);
            }
        }

        private static void PatchRequired(
            Harmony harmony,
            string description,
            MethodBase? original,
            MethodInfo transpiler)
        {
            if (original == null)
            {
                throw new MissingMethodException(description);
            }

            harmony.Patch(
                original,
                transpiler: new HarmonyMethod(transpiler));
        }

        private static IEnumerable<CodeInstruction> ReplaceUnsafeTickManagerGetter(
            IEnumerable<CodeInstruction> instructions,
            MethodBase __originalMethod)
        {
            List<CodeInstruction> codes =
                new List<CodeInstruction>(instructions);
            MethodInfo? unsafeGetter = AccessTools.PropertyGetter(
                typeof(Find),
                nameof(Find.TickManager));
            MethodInfo? safeGetter = AccessTools.Method(
                typeof(JusticeBossDiagnosticStartupSafety),
                nameof(GetTickManagerSafe));

            if (unsafeGetter == null || safeGetter == null)
            {
                throw new MissingMethodException(
                    "无法解析 TickManager getter。目标方法："
                        + __originalMethod.FullDescription());
            }

            int matchIndex = -1;
            int matchCount = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(unsafeGetter))
                {
                    continue;
                }

                matchIndex = i;
                matchCount++;
            }

            if (matchCount != 1)
            {
                throw new InvalidOperationException(
                    __originalMethod.FullDescription()
                        + " 中预期恰好存在 1 个 Find.TickManager getter，实际找到 "
                        + matchCount
                        + " 个。");
            }

            codes[matchIndex].operand = safeGetter;
            return codes;
        }

        private static TickManager? GetTickManagerSafe()
        {
            return Current.Game?.tickManager;
        }
    }
}
