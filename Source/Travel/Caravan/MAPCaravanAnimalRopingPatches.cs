using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>仅由三个动物牵引 Duty 使用，不扩大其他行为树的殖民者条件。</summary>
    public sealed class ThinkNode_ConditionalCaravanAnimalRoper : ThinkNode_ConditionalColonist
    {
        protected override bool Satisfied(Pawn pawn)
        {
            return MAPTravelUtility.CanActAsCaravanAnimalRoper(pawn);
        }
    }

    /// <summary>统一适配出发牵引职责、返程牵引者选择和返程完成判定。</summary>
    [HarmonyPatch]
    public static class MAPCaravanAnimalRoperLifecyclePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            Type[] types = { typeof(LordToil_PrepareCaravan_RopeAnimals),
                typeof(CaravanFormingUtility), typeof(LordToil_ReturnedCaravan_PenAnimals) };
            string[] names = { nameof(LordToil_PrepareCaravan_RopeAnimals.UpdateAllDuties),
                nameof(CaravanFormingUtility.LeadAnimalsToPen),
                nameof(LordToil_ReturnedCaravan_PenAnimals.LordToilTick) };
            for (int i = 0; i < types.Length; i++)
            {
                MethodInfo? method = AccessTools.Method(types[i], names[i]);
                if (method == null)
                {
                    Log.ErrorOnce($"[MAP-机械族机械师] 未找到 {types[i].Name}.{names[i]}，跳过该动物牵引补丁。", 879361010 + i);
                    continue;
                }

                yield return method;
            }
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            return MAPCaravanGatheringTranspilerUtility.ReplaceSingleIsColonistCall(
                instructions,
                $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}",
                __originalMethod.DeclaringType == typeof(CaravanFormingUtility) ? 879361021
                    : __originalMethod.DeclaringType == typeof(LordToil_ReturnedCaravan_PenAnimals) ? 879361022
                    : 879361020,
                nameof(MAPTravelUtility.CanActAsCaravanAnimalRoper));
        }
    }

    /// <summary>
    /// 通过牵引可达性调用定位出发检查的 Lambda，不依赖编译器生成的方法编号。
    /// 只替换牵引者身份，保留集合点和出口的原版可达性检查。
    /// </summary>
    [HarmonyPatch]
    public static class MAPCaravanAnimalRoperReachabilityPatch
    {
        private static MethodBase? cachedTargetMethod;
        private static bool targetMethodResolved;

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static MethodBase? TargetMethod()
        {
            if (targetMethodResolved)
            {
                return cachedTargetMethod;
            }

            targetMethodResolved = true;
            try
            {
                MethodInfo? isColonist = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.IsColonist));
                MethodInfo? canTakeAnimal = AccessTools.Method(typeof(GatherAnimalsAndSlavesForCaravanUtility),
                    nameof(GatherAnimalsAndSlavesForCaravanUtility.CanRoperTakeAnimalToDest));
                if (isColonist == null || canTakeAnimal == null)
                {
                    Log.ErrorOnce("[MAP-机械族机械师] 无法解析远行队动物牵引检查方法，保留原版行为。", 879361002);
                    return null;
                }

                List<MethodInfo> matches = new List<MethodInfo>();
                foreach (MethodInfo method in EnumerateMethods(typeof(Dialog_FormCaravan)))
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    if (method.ReturnType != typeof(bool) || parameters.Length != 1
                        || parameters[0].ParameterType != typeof(Pawn))
                    {
                        continue;
                    }

                    bool hasColonist = false;
                    int reachabilityCalls = 0;
                    foreach (CodeInstruction instruction in PatchProcessor.GetOriginalInstructions(method))
                    {
                        if (instruction.Calls(isColonist))
                        {
                            hasColonist = true;
                        }

                        if (instruction.Calls(canTakeAnimal))
                        {
                            reachabilityCalls++;
                        }
                    }

                    if (hasColonist && reachabilityCalls == 2)
                    {
                        matches.Add(method);
                    }
                }

                if (matches.Count != 1)
                {
                    Log.ErrorOnce($"[MAP-机械族机械师] 远行队动物牵引可达性 Lambda 预期 1 个，实际 {matches.Count} 个，保留原版行为。", 879361003);
                    return null;
                }

                cachedTargetMethod = matches[0];
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[MAP-机械族机械师] 定位远行队动物牵引可达性 Lambda 失败，保留原版行为：{ex}", 879361004);
            }

            return cachedTargetMethod;
        }

        private static IEnumerable<MethodInfo> EnumerateMethods(Type type)
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                yield return method;
            }

            foreach (Type nestedType in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                foreach (MethodInfo method in EnumerateMethods(nestedType))
                {
                    yield return method;
                }
            }
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return MAPCaravanGatheringTranspilerUtility.ReplaceSingleIsColonistCall(
                instructions,
                "Dialog_FormCaravan.TryFormAndSendCaravan 动物牵引可达性 Lambda",
                879361005,
                nameof(MAPTravelUtility.CanActAsCaravanAnimalRoper));
        }
    }
}
