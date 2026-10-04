using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(CompPilotConsole), "ValidateNavigator")]
    public static class GravshipPilotConsole_ValidateNavigator_Patch
    {
        public static void Postfix(
            CompPilotConsole __instance,
            LocalTargetInfo target,
            ref AcceptanceReport __result)
        {
            if (__result.Accepted)
            {
                return;
            }

            if (target.Thing is not Pawn pawn)
            {
                return;
            }

            if (!CompGravshipPilotUser.PawnCanUseGravshipPilotConsole(pawn))
            {
                return;
            }

            if (pawn.Downed
                || pawn.health?.capacities?.CapableOf(PawnCapacityDefOf.Moving) != true)
            {
                __result = "Incapable".Translate();
                return;
            }

            if (pawn.skills == null
                || pawn.skills.GetSkill(SkillDefOf.Intellectual).TotallyDisabled)
            {
                __result = "IncapableOfCapacity".Translate(SkillDefOf.Intellectual.label).CapitalizeFirst();
                return;
            }

            if (!pawn.CanReach(__instance.parent, PathEndMode.InteractionCell, Danger.Deadly))
            {
                __result = "NoPath".Translate();
                return;
            }

            __result = AcceptanceReport.WasAccepted;
        }
    }

    /// <summary>
    /// 不跳过 MakeNewToils：仅在编译器生成的驾驶台 initAction 中，
    /// 为 Ideo 为空的合法机械驾驶员补齐 GravshipLaunch 解析，并安全打开仪式窗口。
    /// </summary>
    [HarmonyPatch]
    public static class GravshipPilotConsole_InitActionRitualResolution_Patch
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] GravshipPilotConsolePatches：";

        private const int ErrorKeyTargetNotFound = 879347001;
        private const int ErrorKeyAmbiguousTarget = 879347002;
        private const int ErrorKeyResolveMembers = 879347003;
        private const int ErrorKeyPatternMismatch = 879347004;
        private const int ErrorKeyReadInstructions = 879347005;

        private static MethodBase? cachedTargetMethod;
        private static bool targetMethodResolved;

        private static bool Prepare()
        {
            try
            {
                return TargetMethod() != null;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}定位驾驶台仪式初始化动作时发生异常，补丁未应用：{ex}",
                    ErrorKeyTargetNotFound);
                return false;
            }
        }

        private static MethodBase? TargetMethod()
        {
            if (targetMethodResolved)
            {
                return cachedTargetMethod;
            }

            targetMethodResolved = true;
            cachedTargetMethod = ResolvePilotConsoleInitActionMethod();
            return cachedTargetMethod;
        }

        private static MethodBase? ResolvePilotConsoleInitActionMethod()
        {
            HashSet<MethodInfo> candidates = new HashSet<MethodInfo>();
            CollectDeclaredMethods(typeof(JobDriver_PilotConsole), candidates);

            List<MethodInfo> matches = new List<MethodInfo>();
            foreach (MethodInfo candidate in candidates)
            {
                if (IsPilotConsoleRitualInitAction(candidate))
                {
                    matches.Add(candidate);
                }
            }

            if (matches.Count == 0)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}未定位到 JobDriver_PilotConsole 打开 GravshipLaunch 仪式的编译器生成方法，补丁未应用。",
                    ErrorKeyTargetNotFound);
                return null;
            }

            if (matches.Count > 1)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}定位到多个驾驶台仪式初始化候选方法（{matches.Count}），补丁未应用。",
                    ErrorKeyAmbiguousTarget);
                return null;
            }

            return matches[0];
        }

        private static void CollectDeclaredMethods(Type type, HashSet<MethodInfo> methods)
        {
            MethodInfo[] declared;
            try
            {
                declared = type.GetMethods(
                    BindingFlags.Instance
                    | BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}反射枚举 {type.FullName} 方法失败：{ex}",
                    ErrorKeyReadInstructions);
                return;
            }

            for (int i = 0; i < declared.Length; i++)
            {
                methods.Add(declared[i]);
            }

            Type[] nestedTypes;
            try
            {
                nestedTypes = type.GetNestedTypes(
                    BindingFlags.Public | BindingFlags.NonPublic);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}反射枚举 {type.FullName} 嵌套类型失败：{ex}",
                    ErrorKeyReadInstructions);
                return;
            }

            for (int i = 0; i < nestedTypes.Length; i++)
            {
                CollectDeclaredMethods(nestedTypes[i], methods);
            }
        }

        private static bool IsPilotConsoleRitualInitAction(MethodInfo method)
        {
            MethodInfo? ideoGetter = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.Ideo));
            MethodInfo? getPrecept = AccessTools.Method(
                typeof(Ideo),
                nameof(Ideo.GetPrecept),
                new[] { typeof(PreceptDef) });
            FieldInfo? gravshipLaunchField = AccessTools.Field(
                typeof(PreceptDefOf),
                nameof(PreceptDefOf.GravshipLaunch));
            MethodInfo? showWindow = GetShowRitualBeginWindowMethod();
            FieldInfo? pawnField = AccessTools.Field(typeof(JobDriver), "pawn");

            if (ideoGetter == null
                || getPrecept == null
                || gravshipLaunchField == null
                || showWindow == null
                || pawnField == null)
            {
                return false;
            }

            List<CodeInstruction>? instructions = TryReadInstructions(method);
            if (instructions == null)
            {
                return false;
            }

            bool hasPawnLoad = false;
            bool hasIdeo = false;
            bool hasGravshipLaunch = false;
            bool hasGetPrecept = false;
            bool hasCastRitual = false;
            bool hasShowWindow = false;

            for (int i = 0; i < instructions.Count; i++)
            {
                CodeInstruction instruction = instructions[i];
                if (instruction.opcode == OpCodes.Ldfld
                    && instruction.operand is FieldInfo field
                    && field == pawnField)
                {
                    hasPawnLoad = true;
                }

                if (instruction.Calls(ideoGetter))
                {
                    hasIdeo = true;
                }

                if (instruction.opcode == OpCodes.Ldsfld
                    && instruction.operand is FieldInfo staticField
                    && staticField == gravshipLaunchField)
                {
                    hasGravshipLaunch = true;
                }

                if (instruction.Calls(getPrecept))
                {
                    hasGetPrecept = true;
                }

                if (instruction.opcode == OpCodes.Castclass
                    && instruction.operand is Type castType
                    && castType == typeof(Precept_Ritual))
                {
                    hasCastRitual = true;
                }

                if (instruction.Calls(showWindow))
                {
                    hasShowWindow = true;
                }
            }

            return hasPawnLoad
                && hasIdeo
                && hasGravshipLaunch
                && hasGetPrecept
                && hasCastRitual
                && hasShowWindow;
        }

        /// <summary>
        /// 有个人 Ideo 时只查该 Ideo；仅空 Ideo 的合法机械驾驶员才回退到玩家主 Ideo。
        /// </summary>
        public static Precept_Ritual? ResolveGravshipLaunchRitualForPilot(Pawn? pawn)
        {
            return GravshipLaunchRitualUtility.ResolveForPilot(pawn, PreceptDefOf.GravshipLaunch);
        }

        /// <summary>
        /// 与原版 ShowRitualBeginWindow 实例调用栈布局一致的安全包装。
        /// </summary>
        public static void ShowGravshipLaunchRitualBeginWindowSafely(
            Precept_Ritual? ritual,
            TargetInfo target,
            RitualObligation? obligation,
            Pawn? selectedPawn,
            Dictionary<string, Pawn>? forcedForRole)
        {
            if (ritual == null)
            {
                Messages.Message(
                    "MAP_MechanoidMechanitor.Core.Gravship.NoGravshipLaunchRitual".Translate(),
                    target,
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            ritual.ShowRitualBeginWindow(target, obligation, selectedPawn, forcedForRole);
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            MethodInfo? ideoGetter = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.Ideo));
            MethodInfo? getPrecept = AccessTools.Method(
                typeof(Ideo),
                nameof(Ideo.GetPrecept),
                new[] { typeof(PreceptDef) });
            FieldInfo? gravshipLaunchField = AccessTools.Field(
                typeof(PreceptDefOf),
                nameof(PreceptDefOf.GravshipLaunch));
            MethodInfo? showWindow = GetShowRitualBeginWindowMethod();
            MethodInfo? resolveRitual = AccessTools.Method(
                typeof(GravshipPilotConsole_InitActionRitualResolution_Patch),
                nameof(ResolveGravshipLaunchRitualForPilot));
            MethodInfo? showWindowSafely = AccessTools.Method(
                typeof(GravshipPilotConsole_InitActionRitualResolution_Patch),
                nameof(ShowGravshipLaunchRitualBeginWindowSafely));

            if (ideoGetter == null
                || getPrecept == null
                || gravshipLaunchField == null
                || showWindow == null
                || resolveRitual == null
                || showWindowSafely == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}无法解析驾驶台仪式 Transpiler 所需成员，补丁未应用。",
                    ErrorKeyResolveMembers);
                return codes;
            }

            List<int> ritualChainStarts = new List<int>();
            for (int i = 0; i + 3 < codes.Count; i++)
            {
                if (!codes[i].Calls(ideoGetter))
                {
                    continue;
                }

                if (codes[i + 1].opcode != OpCodes.Ldsfld
                    || codes[i + 1].operand is not FieldInfo field
                    || field != gravshipLaunchField)
                {
                    continue;
                }

                if (!codes[i + 2].Calls(getPrecept))
                {
                    continue;
                }

                if (codes[i + 3].opcode != OpCodes.Castclass
                    || codes[i + 3].operand is not Type castType
                    || castType != typeof(Precept_Ritual))
                {
                    continue;
                }

                ritualChainStarts.Add(i);
            }

            List<int> showWindowIndices = new List<int>();
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].Calls(showWindow))
                {
                    showWindowIndices.Add(i);
                }
            }

            if (ritualChainStarts.Count != 1 || showWindowIndices.Count != 1)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}驾驶台 initAction IL 锚点数量不符合预期：" +
                    $"仪式解析链={ritualChainStarts.Count}，ShowRitualBeginWindow={showWindowIndices.Count}，补丁未应用。",
                    ErrorKeyPatternMismatch);
                return codes;
            }

            int chainStart = ritualChainStarts[0];
            codes[chainStart].opcode = OpCodes.Call;
            codes[chainStart].operand = resolveRitual;
            NopPreserveMeta(codes[chainStart + 1]);
            NopPreserveMeta(codes[chainStart + 2]);
            NopPreserveMeta(codes[chainStart + 3]);

            int showIndex = showWindowIndices[0];
            codes[showIndex].opcode = OpCodes.Call;
            codes[showIndex].operand = showWindowSafely;

            return codes;
        }

        private static void NopPreserveMeta(CodeInstruction instruction)
        {
            instruction.opcode = OpCodes.Nop;
            instruction.operand = null;
        }

        private static MethodInfo? GetShowRitualBeginWindowMethod()
        {
            return AccessTools.Method(
                typeof(Precept_Ritual),
                nameof(Precept_Ritual.ShowRitualBeginWindow),
                new[]
                {
                    typeof(TargetInfo),
                    typeof(RitualObligation),
                    typeof(Pawn),
                    typeof(Dictionary<string, Pawn>)
                });
        }

        private static List<CodeInstruction>? TryReadInstructions(MethodBase method)
        {
            try
            {
                return new List<CodeInstruction>(
                    PatchProcessor.GetOriginalInstructions(method));
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}无法通过 PatchProcessor 读取方法 IL：" +
                    $"{method.DeclaringType?.FullName}.{method.Name}：{ex}",
                    ErrorKeyReadInstructions);
                return null;
            }
        }
    }
}
