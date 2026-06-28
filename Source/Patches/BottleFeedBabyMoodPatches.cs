using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch]
    public static class Patch_JobDriver_BottleFeedBaby_FedBabyMemory_MoodSafe
    {
        private const string LogPrefix =
            "[MAP_MechanoidMechanitor] BottleFeedBabyMoodPatches:";

        private static MethodBase? TargetMethod()
        {
            MethodInfo? match = null;
            MethodInfo[] methods = typeof(JobDriver_BottleFeedBaby).GetMethods(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (!MethodReferencesFedBaby(method))
                {
                    continue;
                }

                if (match != null)
                {
                    Log.Error(
                        $"{LogPrefix} expected exactly 1 compiler-generated method referencing ThoughtDefOf.FedBaby in JobDriver_BottleFeedBaby, found multiple. Patch not applied.");
                    return null;
                }

                match = method;
            }

            if (match == null)
            {
                Log.Error(
                    $"{LogPrefix} could not find compiler-generated finish action referencing ThoughtDefOf.FedBaby in JobDriver_BottleFeedBaby. Patch not applied.");
            }

            return match;
        }

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static bool MethodReferencesFedBaby(MethodInfo method)
        {
            FieldInfo? fedBabyField = AccessTools.Field(
                typeof(ThoughtDefOf),
                nameof(ThoughtDefOf.FedBaby));
            if (fedBabyField == null)
            {
                return false;
            }

            MethodBody? body = method.GetMethodBody();
            if (body == null)
            {
                return false;
            }

            byte[] il = body.GetILAsByteArray();
            for (int i = 0; i < il.Length - 4; i++)
            {
                if (il[i] != OpCodes.Ldsfld.Value)
                {
                    continue;
                }

                int token = System.BitConverter.ToInt32(il, i + 1);
                try
                {
                    if (ReferenceEquals(method.Module.ResolveMember(token), fedBabyField))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Ignore invalid metadata tokens while scanning.
                }
            }

            return false;
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            FieldInfo? fedBabyField = AccessTools.Field(
                typeof(ThoughtDefOf),
                nameof(ThoughtDefOf.FedBaby));
            MethodInfo? tryGainMemoryMethod = AccessTools.Method(
                typeof(MemoryThoughtHandler),
                nameof(MemoryThoughtHandler.TryGainMemory),
                new[] { typeof(ThoughtDef), typeof(Pawn), typeof(Precept) });
            MethodInfo? helperMethod = AccessTools.Method(
                typeof(MechanicalChildcareUtility),
                nameof(MechanicalChildcareUtility.TryGainFedBabyMemoryIfMood));
            MethodInfo? getBabyMethod = AccessTools.Method(
                typeof(JobDriver_FeedBaby),
                "get_Baby");
            FieldInfo? pawnField = AccessTools.Field(typeof(JobDriver), "pawn");

            if (fedBabyField == null
                || tryGainMemoryMethod == null
                || helperMethod == null
                || getBabyMethod == null
                || pawnField == null)
            {
                Log.Error($"{LogPrefix} missing reflection target(s). Patch not applied.");
                return codes;
            }

            int matchCount = 0;
            int replaceStart = -1;
            int replaceEnd = -1;

            for (int i = 0; i < codes.Count; i++)
            {
                if (!IsCallvirtTryGainMemory(codes[i], tryGainMemoryMethod))
                {
                    continue;
                }

                int fedBabyIndex = FindFedBabyLoadIndex(codes, i, fedBabyField);
                if (fedBabyIndex < 0 || !IsCaregiverFedBabyCallPattern(codes, fedBabyIndex, i, getBabyMethod))
                {
                    continue;
                }

                matchCount++;
                replaceStart = fedBabyIndex - 6;
                replaceEnd = i;
            }

            if (matchCount != 1 || replaceStart < 0 || replaceEnd < replaceStart)
            {
                Log.Error(
                    $"{LogPrefix} expected exactly 1 FedBaby TryGainMemory call pattern in JobDriver_BottleFeedBaby finish action, found {matchCount}. Patch not applied.");
                return codes;
            }

            codes.RemoveRange(replaceStart, replaceEnd - replaceStart + 1);
            codes.Insert(replaceStart, new CodeInstruction(OpCodes.Ldarg_0));
            codes.Insert(replaceStart + 1, new CodeInstruction(OpCodes.Ldfld, pawnField));
            codes.Insert(replaceStart + 2, new CodeInstruction(OpCodes.Ldarg_0));
            codes.Insert(replaceStart + 3, new CodeInstruction(OpCodes.Call, getBabyMethod));
            codes.Insert(replaceStart + 4, new CodeInstruction(OpCodes.Call, helperMethod));

            return codes;
        }

        private static bool IsCallvirtTryGainMemory(
            CodeInstruction instruction,
            MethodInfo tryGainMemoryMethod)
        {
            return instruction.opcode == OpCodes.Callvirt
                && instruction.operand is MethodInfo called
                && called == tryGainMemoryMethod;
        }

        private static int FindFedBabyLoadIndex(
            List<CodeInstruction> codes,
            int tryGainMemoryIndex,
            FieldInfo fedBabyField)
        {
            for (int i = tryGainMemoryIndex - 1; i >= 0 && i >= tryGainMemoryIndex - 8; i--)
            {
                if (codes[i].opcode == OpCodes.Ldsfld
                    && codes[i].operand is FieldInfo field
                    && field == fedBabyField)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsCaregiverFedBabyCallPattern(
            List<CodeInstruction> codes,
            int fedBabyIndex,
            int tryGainMemoryIndex,
            MethodInfo getBabyMethod)
        {
            if (fedBabyIndex + 4 > tryGainMemoryIndex)
            {
                return false;
            }

            return codes[fedBabyIndex + 1].opcode == OpCodes.Ldarg_0
                && codes[fedBabyIndex + 2].opcode == OpCodes.Call
                && codes[fedBabyIndex + 2].operand is MethodInfo babyGetter
                && babyGetter == getBabyMethod
                && codes[fedBabyIndex + 3].opcode == OpCodes.Ldnull;
        }
    }
}
