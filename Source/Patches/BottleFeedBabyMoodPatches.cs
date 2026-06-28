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
    [HarmonyPatch]
    public static class Patch_JobDriver_BottleFeedBaby_FedBabyMemory_MoodSafe
    {
        private const string LogPrefix =
            "[MAP_MechanoidMechanitor] BottleFeedBabyMoodPatches:";

        private const int CaregiverFedBabyChainLength = 11;

        private struct FedBabyPatternMatch
        {
            public int StartIndex;
            public int EndIndex;
        }

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

            MethodBody? body;
            try
            {
                body = method.GetMethodBody();
            }
            catch (InvalidOperationException ex)
            {
                Log.Warning(
                    $"{LogPrefix} could not read method body for {method.Name}: {ex.Message}");
                return false;
            }

            if (body == null)
            {
                return false;
            }

            byte[] il;
            try
            {
                il = body.GetILAsByteArray();
            }
            catch (InvalidOperationException ex)
            {
                Log.Warning(
                    $"{LogPrefix} could not read IL bytes for {method.Name}: {ex.Message}");
                return false;
            }

            if (il == null || il.Length < 5)
            {
                return false;
            }

            for (int i = 0; i < il.Length - 4; i++)
            {
                if (il[i] != OpCodes.Ldsfld.Value)
                {
                    continue;
                }

                int token = BitConverter.ToInt32(il, i + 1);
                MemberInfo? member;
                try
                {
                    member = method.Module.ResolveMember(token);
                }
                catch (ArgumentException)
                {
                    continue;
                }
                catch (BadImageFormatException)
                {
                    continue;
                }

                if (SameMember(member, fedBabyField))
                {
                    return true;
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
            FieldInfo? pawnField = AccessTools.Field(typeof(JobDriver), "pawn");
            FieldInfo? needsField = AccessTools.Field(typeof(Pawn), nameof(Pawn.needs));
            FieldInfo? moodField = AccessTools.Field(typeof(Pawn_NeedsTracker), "mood");
            FieldInfo? thoughtsField = AccessTools.Field(typeof(Need_Mood), "thoughts");
            FieldInfo? memoriesField = AccessTools.Field(typeof(ThoughtHandler), "memories");
            MethodInfo? tryGainMemoryMethod = AccessTools.Method(
                typeof(MemoryThoughtHandler),
                nameof(MemoryThoughtHandler.TryGainMemory),
                new[] { typeof(ThoughtDef), typeof(Pawn), typeof(Precept) });
            MethodInfo? helperMethod = AccessTools.Method(
                typeof(MechanicalChildcareUtility),
                nameof(MechanicalChildcareUtility.TryGiveFedBabyThought));
            MethodInfo? getBabyMethod = AccessTools.Method(
                typeof(JobDriver_FeedBaby),
                "get_Baby");

            if (fedBabyField == null
                || pawnField == null
                || needsField == null
                || moodField == null
                || thoughtsField == null
                || memoriesField == null
                || tryGainMemoryMethod == null
                || helperMethod == null
                || getBabyMethod == null)
            {
                Log.Error($"{LogPrefix} missing reflection target(s). Patch not applied.");
                return codes;
            }

            if (!TryFindCaregiverFedBabyPattern(
                    codes,
                    fedBabyField,
                    pawnField,
                    needsField,
                    moodField,
                    thoughtsField,
                    memoriesField,
                    getBabyMethod,
                    tryGainMemoryMethod,
                    out FedBabyPatternMatch pattern))
            {
                return codes;
            }

            if (!CanSafelyReplaceRange(codes, pattern.StartIndex, pattern.EndIndex, out string reason))
            {
                Log.Error(
                    $"{LogPrefix} cannot safely replace caregiver FedBaby IL chain: {reason}. Patch not applied.");
                return codes;
            }

            ReplaceCaregiverFedBabyChain(
                codes,
                pattern.StartIndex,
                pattern.EndIndex,
                pawnField,
                getBabyMethod,
                helperMethod);

            return codes;
        }

        private static bool TryFindCaregiverFedBabyPattern(
            List<CodeInstruction> codes,
            FieldInfo fedBabyField,
            FieldInfo pawnField,
            FieldInfo needsField,
            FieldInfo moodField,
            FieldInfo thoughtsField,
            FieldInfo memoriesField,
            MethodInfo getBabyMethod,
            MethodInfo tryGainMemoryMethod,
            out FedBabyPatternMatch pattern)
        {
            pattern = default;
            int matchCount = 0;
            FedBabyPatternMatch lastMatch = default;

            for (int i = 0; i < codes.Count; i++)
            {
                if (!IsCallvirtTryGainMemory(codes[i], tryGainMemoryMethod))
                {
                    continue;
                }

                if (!TryMatchCaregiverFedBabyChain(
                        codes,
                        i,
                        fedBabyField,
                        pawnField,
                        needsField,
                        moodField,
                        thoughtsField,
                        memoriesField,
                        getBabyMethod,
                        tryGainMemoryMethod,
                        out int startIndex))
                {
                    continue;
                }

                matchCount++;
                lastMatch = new FedBabyPatternMatch
                {
                    StartIndex = startIndex,
                    EndIndex = i
                };
            }

            if (matchCount != 1)
            {
                Log.Error(
                    $"{LogPrefix} expected exactly 1 caregiver FedBaby TryGainMemory chain in JobDriver_BottleFeedBaby finish action, found {matchCount}. Patch not applied.");
                return false;
            }

            pattern = lastMatch;
            return true;
        }

        private static bool TryMatchCaregiverFedBabyChain(
            List<CodeInstruction> codes,
            int tryGainMemoryIndex,
            FieldInfo fedBabyField,
            FieldInfo pawnField,
            FieldInfo needsField,
            FieldInfo moodField,
            FieldInfo thoughtsField,
            FieldInfo memoriesField,
            MethodInfo getBabyMethod,
            MethodInfo tryGainMemoryMethod,
            out int startIndex)
        {
            startIndex = tryGainMemoryIndex - (CaregiverFedBabyChainLength - 1);
            if (startIndex < 0)
            {
                return false;
            }

            int index = startIndex;
            if (codes[index++].opcode != OpCodes.Ldarg_0)
            {
                return false;
            }

            if (!LoadsInstanceField(codes[index++], pawnField))
            {
                return false;
            }

            if (!LoadsInstanceField(codes[index++], needsField))
            {
                return false;
            }

            if (!LoadsInstanceField(codes[index++], moodField))
            {
                return false;
            }

            if (!LoadsInstanceField(codes[index++], thoughtsField))
            {
                return false;
            }

            if (!LoadsInstanceField(codes[index++], memoriesField))
            {
                return false;
            }

            if (!LoadsStaticField(codes[index++], fedBabyField))
            {
                return false;
            }

            if (codes[index++].opcode != OpCodes.Ldarg_0)
            {
                return false;
            }

            if (!IsCall(codes[index++], getBabyMethod))
            {
                return false;
            }

            if (codes[index++].opcode != OpCodes.Ldnull)
            {
                return false;
            }

            if (!IsCallvirtTryGainMemory(codes[index], tryGainMemoryMethod))
            {
                return false;
            }

            return index == tryGainMemoryIndex;
        }

        private static bool CanSafelyReplaceRange(
            List<CodeInstruction> codes,
            int start,
            int end,
            out string reason)
        {
            reason = string.Empty;

            for (int i = start + 1; i <= end; i++)
            {
                if (codes[i].labels.Count > 0)
                {
                    reason = $"instruction at index {i} has labels inside replacement range";
                    return false;
                }
            }

            for (int i = start; i <= end; i++)
            {
                if (codes[i].blocks.Count > 0)
                {
                    reason = $"instruction at index {i} has exception blocks inside replacement range";
                    return false;
                }
            }

            return true;
        }

        private static void ReplaceCaregiverFedBabyChain(
            List<CodeInstruction> codes,
            int start,
            int end,
            FieldInfo pawnField,
            MethodInfo getBabyMethod,
            MethodInfo helperMethod)
        {
            List<Label> entryLabels = new List<Label>(codes[start].labels);

            codes.RemoveRange(start, end - start + 1);

            CodeInstruction loadCaregiver = new CodeInstruction(OpCodes.Ldarg_0);
            CodeInstruction loadPawn = new CodeInstruction(OpCodes.Ldfld, pawnField);
            CodeInstruction loadDriver = new CodeInstruction(OpCodes.Ldarg_0);
            CodeInstruction loadBaby = new CodeInstruction(OpCodes.Call, getBabyMethod);
            CodeInstruction callHelper = new CodeInstruction(OpCodes.Call, helperMethod);

            foreach (Label label in entryLabels)
            {
                loadCaregiver.labels.Add(label);
            }

            codes.Insert(start, loadCaregiver);
            codes.Insert(start + 1, loadPawn);
            codes.Insert(start + 2, loadDriver);
            codes.Insert(start + 3, loadBaby);
            codes.Insert(start + 4, callHelper);
        }

        private static bool IsCallvirtTryGainMemory(
            CodeInstruction instruction,
            MethodInfo tryGainMemoryMethod)
        {
            return instruction.opcode == OpCodes.Callvirt
                && instruction.operand is MethodInfo called
                && SameMember(called, tryGainMemoryMethod);
        }

        private static bool IsCall(CodeInstruction instruction, MethodInfo method)
        {
            return instruction.opcode == OpCodes.Call
                && instruction.operand is MethodInfo called
                && SameMember(called, method);
        }

        private static bool LoadsInstanceField(CodeInstruction instruction, FieldInfo target)
        {
            return instruction.opcode == OpCodes.Ldfld
                && instruction.operand is FieldInfo field
                && SameMember(field, target);
        }

        private static bool LoadsStaticField(CodeInstruction instruction, FieldInfo target)
        {
            return instruction.opcode == OpCodes.Ldsfld
                && instruction.operand is FieldInfo field
                && SameMember(field, target);
        }

        private static bool SameMember(MemberInfo? left, MemberInfo? right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            if (left.MetadataToken == right.MetadataToken && left.Module == right.Module)
            {
                return true;
            }

            return left.Equals(right);
        }
    }
}
