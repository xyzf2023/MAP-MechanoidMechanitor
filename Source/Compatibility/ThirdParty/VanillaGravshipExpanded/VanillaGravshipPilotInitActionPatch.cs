using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.VanillaGravshipExpanded
{
    internal static class VanillaGravshipPilotInitActionPatch
    {
        private static FieldInfo? jumperField;
        private static FieldInfo? hulkField;
        private static readonly MethodInfo IdeoGetter = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.Ideo));
        private static readonly MethodInfo GetPrecept = AccessTools.DeclaredMethod(typeof(Ideo),
            nameof(Ideo.GetPrecept), new[] { typeof(PreceptDef) });
        private static readonly MethodInfo ShowWindow = AccessTools.DeclaredMethod(typeof(Precept_Ritual),
            nameof(Precept_Ritual.ShowRitualBeginWindow), new[]
            {
                typeof(TargetInfo), typeof(RitualObligation), typeof(Pawn), typeof(Dictionary<string, Pawn>)
            });

        public static void Configure(FieldInfo jumper, FieldInfo hulk)
        {
            jumperField = jumper;
            hulkField = hulk;
        }

        public static bool TryFindAnchors(List<CodeInstruction> codes,
            out List<int> chains, out List<int> windows, out string failure)
        {
            chains = new List<int>();
            windows = new List<int>();
            failure = string.Empty;
            int ideos = 0, precepts = 0, jumpers = 0, hulks = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].Calls(GetPrecept)) precepts++;
                if (codes[i].Calls(ShowWindow)) windows.Add(i);
                if (!codes[i].Calls(IdeoGetter)) continue;
                ideos++;
                if (i + 3 >= codes.Count || codes[i + 1].opcode != OpCodes.Ldsfld
                    || !codes[i + 2].Calls(GetPrecept) || codes[i + 3].opcode != OpCodes.Castclass
                    || !Equals(codes[i + 3].operand, typeof(Precept_Ritual)))
                    continue;
                if (Equals(codes[i + 1].operand, jumperField)) jumpers++;
                else if (Equals(codes[i + 1].operand, hulkField)) hulks++;
                else continue;
                // 不允许跳转直接进入将被改变的栈类型；链首元数据保留。
                if (codes[i + 1].labels.Count != 0 || codes[i + 2].labels.Count != 0
                    || codes[i].blocks.Count != 0 || codes[i + 1].blocks.Count != 0
                    || codes[i + 2].blocks.Count != 0)
                    continue;
                chains.Add(i);
            }
            if (ideos != 2 || precepts != 2 || jumpers != 1 || hulks != 1
                || chains.Count != 2 || windows.Count != 2)
            {
                failure = "VGE initAction 的两组驾驶仪式解析链与开窗调用不符合预期。";
                return false;
            }
            return true;
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            if (!TryFindAnchors(codes, out List<int> chains, out List<int> windows, out string failure))
                throw new InvalidOperationException(failure);
            MethodInfo resolve = AccessTools.DeclaredMethod(typeof(GravshipLaunchRitualUtility),
                nameof(GravshipLaunchRitualUtility.ResolveForPilot));
            MethodInfo show = AccessTools.DeclaredMethod(typeof(GravshipPilotConsole_InitActionRitualResolution_Patch),
                nameof(GravshipPilotConsole_InitActionRitualResolution_Patch.ShowGravshipLaunchRitualBeginWindowSafely));
            foreach (int i in chains)
            {
                // 保留 Pawn 与 VGE 原先加载的 PreceptDef；不改变驾驶台选择分支。
                codes[i].opcode = OpCodes.Nop;
                codes[i].operand = null;
                codes[i + 2].opcode = OpCodes.Call;
                codes[i + 2].operand = resolve;
            }
            foreach (int i in windows)
            {
                codes[i].opcode = OpCodes.Call;
                codes[i].operand = show;
            }
            return codes;
        }
    }
}
