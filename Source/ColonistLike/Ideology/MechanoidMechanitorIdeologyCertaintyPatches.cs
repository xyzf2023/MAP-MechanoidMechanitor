using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械族机械师意识形态确定度锁定为 100%，并阻止外部转换/传教改写其意识形态。
    /// </summary>
    public static class MechanoidMechanitorIdeologyCertaintyPatches
    {
        [HarmonyPatch(typeof(Pawn_IdeoTracker), nameof(Pawn_IdeoTracker.Certainty), MethodType.Getter)]
        public static class Patch_Certainty_Getter
        {
            [HarmonyPostfix]
            public static void Postfix(Pawn ___pawn, ref float __result)
            {
                if (!ModsConfig.IdeologyActive
                    || !MechanoidMechanitorIdeologyAdaptationUtility.AllowsIdeologyMembership(___pawn))
                {
                    return;
                }

                __result = 1f;
            }
        }

        [HarmonyPatch]
        public static class Patch_CertaintyChangeFactor
        {
            private static MethodBase? cachedTarget;

            private static bool Prepare()
            {
                return TargetMethod() != null;
            }

            private static MethodBase? TargetMethod()
            {
                if (cachedTarget != null)
                {
                    return cachedTarget;
                }

                cachedTarget = AccessTools.PropertyGetter(
                    typeof(Pawn_IdeoTracker),
                    "CertaintyChangeFactor");
                return cachedTarget;
            }

            [HarmonyPrefix]
            public static bool Prefix(Pawn ___pawn, ref float __result)
            {
                if (!ModsConfig.IdeologyActive
                    || !MechanoidMechanitorIdeologyAdaptationUtility.AllowsIdeologyMembership(___pawn))
                {
                    return true;
                }

                __result = 1f;
                return false;
            }
        }

        [HarmonyPatch(typeof(Pawn_IdeoTracker), nameof(Pawn_IdeoTracker.IdeoConversionAttempt))]
        public static class Patch_IdeoConversionAttempt
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn ___pawn, ref bool __result)
            {
                if (!ModsConfig.IdeologyActive
                    || !MechanoidMechanitorIdeologyAdaptationUtility.AllowsIdeologyMembership(___pawn))
                {
                    return true;
                }

                __result = false;
                MechanoidMechanitorIdeologyAdaptationUtility.ForceCertaintyFull(___pawn);
                return false;
            }
        }

        [HarmonyPatch(typeof(Pawn_IdeoTracker), nameof(Pawn_IdeoTracker.OffsetCertainty))]
        public static class Patch_OffsetCertainty
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn ___pawn)
            {
                if (!ModsConfig.IdeologyActive
                    || !MechanoidMechanitorIdeologyAdaptationUtility.AllowsIdeologyMembership(___pawn))
                {
                    return true;
                }

                MechanoidMechanitorIdeologyAdaptationUtility.ForceCertaintyFull(___pawn);
                return false;
            }
        }

        [HarmonyPatch(typeof(Pawn_IdeoTracker), nameof(Pawn_IdeoTracker.Reassure))]
        public static class Patch_Reassure
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn ___pawn)
            {
                if (!ModsConfig.IdeologyActive
                    || !MechanoidMechanitorIdeologyAdaptationUtility.AllowsIdeologyMembership(___pawn))
                {
                    return true;
                }

                MechanoidMechanitorIdeologyAdaptationUtility.ForceCertaintyFull(___pawn);
                return false;
            }
        }

        [HarmonyPatch(typeof(Pawn_IdeoTracker), nameof(Pawn_IdeoTracker.SetIdeo))]
        public static class Patch_SetIdeo
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn ___pawn, Ideo ideo)
            {
                if (!ModsConfig.IdeologyActive
                    || !MechanoidMechanitorIdeologyAdaptationUtility.AllowsIdeologyMembership(___pawn))
                {
                    return true;
                }

                if (MechanoidMechanitorIdeologyAdaptationUtility.IsIdeoMutationAllowed)
                {
                    return true;
                }

                if (___pawn.ideo?.Ideo == null)
                {
                    return true;
                }

                if (ideo == ___pawn.Ideo)
                {
                    return true;
                }

                return false;
            }

            [HarmonyPostfix]
            public static void Postfix(Pawn ___pawn)
            {
                if (!ModsConfig.IdeologyActive
                    || !MechanoidMechanitorIdeologyAdaptationUtility.AllowsIdeologyMembership(___pawn))
                {
                    return;
                }

                MechanoidMechanitorIdeologyAdaptationUtility.ForceCertaintyFull(___pawn);
            }
        }

        /// <summary>
        /// 道德导师转换能力：机械族机械师本就无法作为转换目标（原版 Valid → ValidateMustBeHuman 拒绝，
        /// 并显示原版拒绝提示，不会静默消耗能力），且确定度补丁已保护其确定度/意识形态。
        /// 唯一需要处理的是发起者/接收者失败心情写入的空引用：用窄范围 Transpiler 把失败分支的两处
        /// <c>needs.mood.thoughts.memories.TryGainMemory</c> 改走空心情安全的辅助方法，
        /// 完整保留成功/失败消息、Convert_Success/Convert_Failure 互动记录、确定度变化、意识形态变化、
        /// 声音与能力冷却等全部原版副作用。
        /// </summary>
        [HarmonyPatch(typeof(CompAbilityEffect_Convert), nameof(CompAbilityEffect_Convert.Apply))]
        public static class Patch_CompAbilityEffect_Convert_Apply
        {
            public static void SafeGainConvertMemory(
                Pawn pawn,
                ThoughtDef def,
                Pawn otherPawn,
                Precept sourcePrecept)
            {
                // 仅当 Pawn 拥有心情 Tracker 时写入：机械族机械师发起者无心情则跳过 failedThoughtInitiator；
                // 普通接收者的 failedThoughtRecipient 照常写入。
                if (pawn?.needs?.mood == null || def == null)
                {
                    return;
                }

                pawn.needs.mood.thoughts.memories.TryGainMemory(def, otherPawn, sourcePrecept);
            }

            private static bool IsMemberAccess(CodeInstruction instruction, string name)
            {
                if (instruction.opcode == OpCodes.Ldfld || instruction.opcode == OpCodes.Ldflda)
                {
                    return instruction.operand is FieldInfo field && field.Name == name;
                }

                if (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
                {
                    return instruction.operand is MethodInfo method
                        && method.Name == "get_" + name;
                }

                return false;
            }

            [HarmonyTranspiler]
            public static IEnumerable<CodeInstruction> Transpiler(
                IEnumerable<CodeInstruction> instructions)
            {
                MethodInfo replacement = AccessTools.Method(
                    typeof(Patch_CompAbilityEffect_Convert_Apply),
                    nameof(SafeGainConvertMemory));

                List<CodeInstruction> code = new List<CodeInstruction>(instructions);
                HashSet<int> removeIndices = new HashSet<int>();
                HashSet<int> replaceIndices = new HashSet<int>();

                for (int i = 0; i + 3 < code.Count; i++)
                {
                    if (!IsMemberAccess(code[i], "needs")
                        || !IsMemberAccess(code[i + 1], "mood")
                        || !IsMemberAccess(code[i + 2], "thoughts")
                        || !IsMemberAccess(code[i + 3], "memories"))
                    {
                        continue;
                    }

                    removeIndices.Add(i);
                    removeIndices.Add(i + 1);
                    removeIndices.Add(i + 2);
                    removeIndices.Add(i + 3);

                    for (int j = i + 4; j < code.Count; j++)
                    {
                        if ((code[j].opcode == OpCodes.Call || code[j].opcode == OpCodes.Callvirt)
                            && code[j].operand is MethodInfo method
                            && method.Name == "TryGainMemory")
                        {
                            replaceIndices.Add(j);
                            break;
                        }
                    }
                }

                List<Label> pendingLabels = new List<Label>();
                for (int i = 0; i < code.Count; i++)
                {
                    CodeInstruction current = code[i];
                    if (removeIndices.Contains(i))
                    {
                        pendingLabels.AddRange(current.labels);
                        continue;
                    }

                    CodeInstruction emit = replaceIndices.Contains(i)
                        ? new CodeInstruction(OpCodes.Call, replacement)
                        {
                            blocks = current.blocks
                        }
                        : current;

                    if (pendingLabels.Count > 0)
                    {
                        emit.labels.AddRange(pendingLabels);
                        pendingLabels.Clear();
                    }

                    if (replaceIndices.Contains(i))
                    {
                        emit.labels.AddRange(current.labels);
                    }

                    yield return emit;
                }
            }
        }
    }
}
