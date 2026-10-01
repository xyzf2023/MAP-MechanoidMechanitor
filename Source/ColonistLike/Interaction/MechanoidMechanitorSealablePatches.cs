using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 仅扩展封闭入口按钮的执行者选择及提示，不改写全局殖民者身份、确认窗口或 Seal 任务。
    /// </summary>
    [HarmonyPatch]
    internal static class MechanoidMechanitorSealablePatches
    {
        private const string LogPrefix = "[MAP-机械族机械师] 封闭入口兼容：";
        private const int ErrorKeyResolve = 0x534C_0001;
        private const int ErrorKeyStructure = 0x534C_0002;
        private const int ErrorKeyTranspiler = 0x534C_0003;

        private static readonly Dictionary<MethodBase, MethodInfo> TargetCalls =
            new Dictionary<MethodBase, MethodInfo>();
        private static bool targetsResolved;

        private static bool Prepare()
        {
            if (targetsResolved)
                return TargetCalls.Count == 3;

            targetsResolved = true;
            try
            {
                MethodInfo? forColonist = AccessTools.Method(
                    typeof(TargetingParameters), nameof(TargetingParameters.ForColonist), Type.EmptyTypes);
                MethodInfo? controlled = AccessTools.PropertyGetter(
                    typeof(Pawn), nameof(Pawn.IsColonistPlayerControlled));
                if (forColonist == null || controlled == null)
                {
                    Log.ErrorOnce($"{LogPrefix}无法解析原版执行者选择方法，补丁未应用。", ErrorKeyResolve);
                    return false;
                }

                int selectorCount = 0;
                int feedbackCount = 0;
                foreach (MethodInfo method in GetGizmoCallbacks(typeof(CompSealable)))
                {
                    List<CodeInstruction> codes = new List<CodeInstruction>(
                        PatchProcessor.GetOriginalInstructions(method));
                    int selectors = CountCalls(codes, forColonist);
                    int feedback = CountCalls(codes, controlled);
                    if (selectors == 0 && feedback == 0)
                        continue;

                    // 原版有一个选择器回调，以及两个分别负责高亮、失败提示的回调。
                    // 在安装任何补丁前核对整组目标，避免原版结构变化时只放开一半流程。
                    if (selectors + feedback != 1)
                        return RejectStructure();

                    TargetCalls.Add(method, selectors == 1 ? forColonist : controlled);
                    selectorCount += selectors;
                    feedbackCount += feedback;
                }

                if (selectorCount != 1 || feedbackCount != 2 || TargetCalls.Count != 3)
                    return RejectStructure();

                return true;
            }
            catch (Exception ex)
            {
                TargetCalls.Clear();
                Log.ErrorOnce($"{LogPrefix}定位原版封闭按钮回调失败，补丁未应用：{ex}", ErrorKeyResolve);
                return false;
            }
        }

        private static bool RejectStructure()
        {
            TargetCalls.Clear();
            Log.ErrorOnce(
                $"{LogPrefix}原版封闭按钮应包含 1 个执行者选择器和 2 个殖民者提示判断，实际结构不符，补丁未应用。",
                ErrorKeyStructure);
            return false;
        }

        private static IEnumerable<MethodBase> TargetMethods() => TargetCalls.Keys;

        private static IEnumerable<MethodInfo> GetGizmoCallbacks(Type type)
        {
            foreach (MethodInfo method in type.GetMethods(
                         BindingFlags.Public | BindingFlags.NonPublic
                         | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                // 不依赖编译器分配的回调编号，也覆盖闭包类中的同名回调。
                if (method.Name.StartsWith("<CompGetGizmosExtra>", StringComparison.Ordinal)
                    && method.GetMethodBody() != null)
                    yield return method;
            }

            foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                foreach (MethodInfo method in GetGizmoCallbacks(nested))
                    yield return method;
            }
        }

        private static int CountCalls(List<CodeInstruction> codes, MethodInfo method)
        {
            int count = 0;
            foreach (CodeInstruction code in codes)
            {
                if (code.Calls(method))
                    count++;
            }
            return count;
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            if (!TargetCalls.TryGetValue(__originalMethod, out MethodInfo? original))
                return codes;

            string replacementName = original.DeclaringType == typeof(TargetingParameters)
                ? nameof(ForSealer)
                : nameof(IsColonistPlayerControlledOrMechanitor);
            MethodInfo? replacement = AccessTools.Method(
                typeof(MechanoidMechanitorSealablePatches), replacementName);
            if (replacement == null || CountCalls(codes, original) != 1)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}{__originalMethod.Name} 的执行者判断调用结构不符，该回调补丁未应用。",
                    ErrorKeyTranspiler);
                return codes;
            }

            foreach (CodeInstruction code in codes)
            {
                if (!code.Calls(original))
                    continue;

                // 替换方法的栈参数和返回值保持一致；原位保留分支标签及异常块信息。
                code.opcode = OpCodes.Call;
                code.operand = replacement;
            }
            return codes;
        }

        private static TargetingParameters ForSealer()
        {
            TargetingParameters parameters = TargetingParameters.ForColonist();
            Predicate<TargetInfo>? originalValidator = parameters.validator;
            parameters.onlyTargetColonists = false;
            parameters.validator = target =>
                (originalValidator == null || originalValidator(target))
                && target.Thing is Pawn pawn
                && ((pawn.IsColonist && pawn.HostFaction == null) || IsAuthorizedMechanitor(pawn));
            return parameters;
        }

        private static bool IsColonistPlayerControlledOrMechanitor(Pawn pawn) =>
            pawn.IsColonistPlayerControlled || IsAuthorizedMechanitor(pawn);

        private static bool IsAuthorizedMechanitor(Pawn? pawn)
        {
            // 正式身份复用权威注册表，兼容先天、后天机械师，不授权普通机械体。
            // 倒地及可达性仍交给原版 ValidateSealer，以保留对应失败原因提示。
            return pawn != null
                && pawn.Spawned
                && !pawn.Destroyed
                && !pawn.Dead
                && pawn.RaceProps.IsMechanoid
                && pawn.Faction == Faction.OfPlayer
                && pawn.HostFaction == null
                && pawn.MentalStateDef == null
                && !pawn.Deathresting
                && !pawn.IsSelfShutdown()
                && pawn.jobs != null
                && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn);
        }
    }
}
