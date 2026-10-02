using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>只扩展服装架的使用者门槛，保留原版菜单、换装按钮和任务流程。</summary>
    [HarmonyPatch]
    internal static class HumanApparelOutfitStandPatches
    {
        private const string LogPrefix = "[MAP-机械族机械师] 服装架换装兼容：";
        private const int ErrorKeyResolve = 879345421;
        private const int ErrorKeyStructure = 879345422;
        private const int ErrorKeyTranspiler = 879345423;

        private static readonly Dictionary<MethodBase, MethodInfo> TargetCalls =
            new Dictionary<MethodBase, MethodInfo>();
        private static bool targetsResolved;

        private static bool Prepare()
        {
            if (targetsResolved)
                return TargetCalls.Count == 2;

            targetsResolved = true;
            try
            {
                MethodInfo? controlled = AccessTools.PropertyGetter(
                    typeof(Pawn), nameof(Pawn.IsColonistPlayerControlled));
                MethodInfo? forColonist = AccessTools.Method(
                    typeof(TargetingParameters), nameof(TargetingParameters.ForColonist), Type.EmptyTypes);
                MethodInfo? menu = AccessTools.Method(
                    typeof(Building_OutfitStand), nameof(Building_OutfitStand.GetFloatMenuOptions),
                    new[] { typeof(Pawn) });
                MethodInfo? moveNext = menu == null ? null : AccessTools.EnumeratorMoveNext(menu);
                if (controlled == null || forColonist == null || moveNext == null)
                    return Reject("无法解析原版服装架菜单或选择器。", ErrorKeyResolve);

                if (CountCalls(new List<CodeInstruction>(
                        PatchProcessor.GetOriginalInstructions(moveNext)), controlled) != 1)
                    return Reject("原版服装架菜单的殖民者判断预期 1 处，实际结构不符。", ErrorKeyStructure);

                TargetCalls.Add(moveNext, controlled);
                foreach (MethodInfo callback in GetGizmoCallbacks(typeof(Building_OutfitStand)))
                {
                    int count = CountCalls(new List<CodeInstruction>(
                        PatchProcessor.GetOriginalInstructions(callback)), forColonist);
                    if (count == 0)
                        continue;
                    if (count != 1)
                        return Reject("原版服装架按钮的选择器调用结构不符。", ErrorKeyStructure);
                    TargetCalls.Add(callback, forColonist);
                }

                if (TargetCalls.Count != 2)
                    return Reject("原版服装架应有 1 个换装菜单和 1 个执行者选择器，实际结构不符。", ErrorKeyStructure);

                return true;
            }
            catch (Exception ex)
            {
                return Reject($"定位原版服装架目标失败：{ex}", ErrorKeyResolve);
            }
        }

        private static bool Reject(string reason, int errorKey)
        {
            TargetCalls.Clear();
            Log.ErrorOnce($"{LogPrefix}{reason}补丁未应用。", errorKey);
            return false;
        }

        private static IEnumerable<MethodBase> TargetMethods() => TargetCalls.Keys;

        private static IEnumerable<MethodInfo> GetGizmoCallbacks(Type type)
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                         | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                // 按所属入口及实际调用定位，不依赖编译器生成的闭包编号。
                if (method.Name.StartsWith("<GetGizmos>", StringComparison.Ordinal)
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
                if (code.Calls(method)) count++;
            return count;
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            if (!TargetCalls.TryGetValue(__originalMethod, out MethodInfo? original))
                return codes;

            MethodInfo? replacement = AccessTools.Method(typeof(HumanApparelOutfitStandPatches),
                original.DeclaringType == typeof(Pawn)
                    ? nameof(IsColonistPlayerControlledOrApparelUser)
                    : nameof(ForOutfitStandUser));
            if (replacement == null || CountCalls(codes, original) != 1)
            {
                Log.ErrorOnce($"{LogPrefix}{__originalMethod.Name} 的使用者门槛结构不符，保留原方法。",
                    ErrorKeyTranspiler);
                return codes;
            }

            foreach (CodeInstruction code in codes)
            {
                if (!code.Calls(original)) continue;
                // 栈参数与返回值不变，原位保留分支标签及异常块。
                code.opcode = OpCodes.Call;
                code.operand = replacement;
            }
            return codes;
        }

        private static bool IsColonistPlayerControlledOrApparelUser(Pawn pawn) =>
            pawn.IsColonistPlayerControlled || HumanApparelUtility.CanUseOutfitStand(pawn);

        private static TargetingParameters ForOutfitStandUser()
        {
            TargetingParameters parameters = TargetingParameters.ForColonist();
            Predicate<TargetInfo>? originalValidator = parameters.validator;
            parameters.onlyTargetColonists = false;
            parameters.validator = target =>
                (originalValidator == null || originalValidator(target))
                && target.Thing is Pawn pawn
                && ((pawn.IsColonist && pawn.HostFaction == null)
                    || HumanApparelUtility.CanUseOutfitStand(pawn));
            return parameters;
        }
    }
}
