using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    internal static class ColonistLikeCompUsableUtility
    {
        public static bool IsAuthorizedMechanicalCompUsableUser(
            Pawn? pawn,
            CompUsable? usable)
        {
            if (pawn == null
                || pawn.Destroyed
                || pawn.Dead
                || pawn.Downed
                || pawn.Deathresting
                || pawn.IsSelfShutdown()
                || pawn.jobs == null
                || !pawn.RaceProps.IsMechanoid)
            {
                return false;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                return false;
            }

            if (CompColonistLikeFloatMenuUser.PawnCanUseColonistLikeFloatMenu(pawn))
            {
                return true;
            }

            return usable != null
                && MechanoidMechanitorImplantUtility.CanUseMechanitorImplant(
                    pawn,
                    usable.parent);
        }
    }

    // CompUsable normally rejects every non-flesh pawn before running its other checks.
    // This transpiler keeps the vanilla Pawn.RaceProps getter and replaces only the
    // RaceProperties.IsFlesh value production with a helper that ORs in authorized player
    // mechanoids. Full colonist-like users retain their existing access, while implant-only
    // users are admitted only when the current item is a supported mechanitor implant.
    // Power, path, reservation, required hediffs and every CompUseEffect check remain vanilla.
    [HarmonyPatch]
    public static class Patch_CompUsable_CanBeUsedBy_ColonistLikeMechanoid
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] ColonistLikeCompUsablePatches：";

        private const int ErrorKeyTargetMethodNotFound = 879345401;
        private const int ErrorKeyResolveFailed = 879345402;
        private const int ErrorKeyMatchCount = 879345403;
        private const int ErrorKeyExpandFailed = 879345404;

        // Instance method: arg0 = this, arg1 = Pawn p (MCP: CompUsable.CanBeUsedBy).
        private const int PawnParameterIndex = 1;

        private static MethodInfo? cachedCanBeUsedByMethod;

        private static MethodBase? TargetMethod()
        {
            MethodInfo? method = GetCanBeUsedByMethod();
            if (method == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}未找到 CompUsable.CanBeUsedBy(Pawn, bool, bool)，补丁未应用。",
                    ErrorKeyTargetMethodNotFound);
            }

            return method;
        }

        private static bool Prepare()
        {
            return GetCanBeUsedByMethod() != null;
        }

        private static MethodInfo? GetCanBeUsedByMethod()
        {
            if (cachedCanBeUsedByMethod != null)
            {
                return cachedCanBeUsedByMethod;
            }

            cachedCanBeUsedByMethod = AccessTools.Method(
                typeof(CompUsable),
                nameof(CompUsable.CanBeUsedBy),
                new[] { typeof(Pawn), typeof(bool), typeof(bool) });

            return cachedCanBeUsedByMethod;
        }

        private static bool IsFleshOrAuthorizedCompUsableUser(
            RaceProperties raceProps,
            Pawn pawn,
            CompUsable usable)
        {
            return raceProps.IsFlesh
                || ColonistLikeCompUsableUtility.IsAuthorizedMechanicalCompUsableUser(
                    pawn,
                    usable);
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            MethodInfo? racePropsGetter = AccessTools.PropertyGetter(
                typeof(Pawn),
                nameof(Pawn.RaceProps));
            MethodInfo? isFleshGetter = AccessTools.PropertyGetter(
                typeof(RaceProperties),
                nameof(RaceProperties.IsFlesh));
            MethodInfo? helperMethod = AccessTools.Method(
                typeof(Patch_CompUsable_CanBeUsedBy_ColonistLikeMechanoid),
                nameof(IsFleshOrAuthorizedCompUsableUser));

            if (racePropsGetter == null || isFleshGetter == null || helperMethod == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}无法解析种族门控相关方法，补丁未应用。",
                    ErrorKeyResolveFailed);
                return codes;
            }

            if (!TryFindUniqueIsFleshGateAnchor(
                    codes,
                    racePropsGetter,
                    isFleshGetter,
                    out int isFleshIndex,
                    out int matchCount))
            {
                Log.ErrorOnce(
                    $"{LogPrefix}CompUsable.CanBeUsedBy 中 RaceProperties.IsFlesh 语义锚点预期仅 1 处，实际找到 {matchCount} 处，补丁未应用。",
                    ErrorKeyMatchCount);
                return codes;
            }

            if (!TryExpandIsFleshGetter(codes, isFleshIndex, helperMethod))
            {
                Log.ErrorOnce(
                    $"{LogPrefix}无法安全扩展 IsFlesh 值生产点（目标 getter 上存在 exception block），补丁未应用。",
                    ErrorKeyExpandFailed);
                return codes;
            }

            return codes;
        }

        private static bool TryFindUniqueIsFleshGateAnchor(
            List<CodeInstruction> codes,
            MethodInfo racePropsGetter,
            MethodInfo isFleshGetter,
            out int isFleshIndex,
            out int matchCount)
        {
            isFleshIndex = -1;
            matchCount = 0;

            for (int i = 1; i < codes.Count; i++)
            {
                if (!MatchesIsFleshGateAnchor(codes, i, racePropsGetter, isFleshGetter))
                {
                    continue;
                }

                matchCount++;
                isFleshIndex = i;
            }

            return matchCount == 1;
        }

        private static bool MatchesIsFleshGateAnchor(
            List<CodeInstruction> codes,
            int isFleshIndex,
            MethodInfo racePropsGetter,
            MethodInfo isFleshGetter)
        {
            int racePropsIndex = isFleshIndex - 1;

            if (!codes[isFleshIndex].Calls(isFleshGetter))
            {
                return false;
            }

            if (!codes[racePropsIndex].Calls(racePropsGetter))
            {
                return false;
            }

            return HasPawnLoadBeforeRaceProps(codes, racePropsIndex);
        }

        private static bool HasPawnLoadBeforeRaceProps(
            List<CodeInstruction> codes,
            int racePropsIndex)
        {
            int searchStart = System.Math.Max(0, racePropsIndex - 3);
            for (int i = racePropsIndex - 1; i >= searchStart; i--)
            {
                if (LoadsPawnParameter(codes[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryExpandIsFleshGetter(
            List<CodeInstruction> codes,
            int isFleshIndex,
            MethodInfo helperMethod)
        {
            if (!CanSafelyInsertBefore(codes, isFleshIndex))
            {
                return false;
            }

            CodeInstruction getterInstruction = codes[isFleshIndex];
            CodeInstruction loadPawn = CreateLoadPawnParameterInstruction();
            CodeInstruction loadUsable = new CodeInstruction(OpCodes.Ldarg_0);

            // Stack before expansion: RaceProperties.
            // Inserted loads push Pawn and CompUsable -> RaceProperties, Pawn, CompUsable.
            TransferEntryLabels(getterInstruction, loadPawn);

            codes.Insert(isFleshIndex, loadPawn);
            codes.Insert(isFleshIndex + 1, loadUsable);

            CodeInstruction helperCall = codes[isFleshIndex + 2];
            helperCall.opcode = OpCodes.Call;
            helperCall.operand = helperMethod;

            return true;
        }

        private static bool LoadsPawnParameter(CodeInstruction instruction)
        {
            return PawnParameterIndex switch
            {
                1 => instruction.opcode == OpCodes.Ldarg_1,
                2 => instruction.opcode == OpCodes.Ldarg_2,
                3 => instruction.opcode == OpCodes.Ldarg_3,
                _ => instruction.opcode == OpCodes.Ldarg
                    && instruction.operand is int index
                    && index == PawnParameterIndex
            };
        }

        private static CodeInstruction CreateLoadPawnParameterInstruction()
        {
            return PawnParameterIndex switch
            {
                0 => new CodeInstruction(OpCodes.Ldarg_0),
                1 => new CodeInstruction(OpCodes.Ldarg_1),
                2 => new CodeInstruction(OpCodes.Ldarg_2),
                3 => new CodeInstruction(OpCodes.Ldarg_3),
                _ => new CodeInstruction(OpCodes.Ldarg, PawnParameterIndex)
            };
        }

        private static void TransferEntryLabels(
            CodeInstruction source,
            CodeInstruction target)
        {
            if (source.labels.Count == 0)
            {
                return;
            }

            target.labels.AddRange(source.labels);
            source.labels.Clear();
        }

        private static bool CanSafelyInsertBefore(
            List<CodeInstruction> codes,
            int insertIndex)
        {
            if (insertIndex < 0 || insertIndex >= codes.Count)
            {
                return false;
            }

            return codes[insertIndex].blocks.Count == 0;
        }
    }

    // CompUsable's gizmo first asks the player to choose a user. Vanilla accepts only pawns
    // reported by Pawn.IsPlayerControlled, which excludes independent mechanoid mechanitors.
    // Replace only that local gate and leave CanBeUsedBy, messages, targeting and jobs vanilla.
    [HarmonyPatch]
    public static class Patch_CompUsable_ValidateTarget_ColonistLikeMechanoid
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] ColonistLikeCompUsablePatches：";

        private const int ErrorKeyTargetMethodNotFound = 879345405;
        private const int ErrorKeyResolveFailed = 879345406;
        private const int ErrorKeyMatchCount = 879345407;
        private const int ErrorKeyExpandFailed = 879345408;

        private static MethodInfo? cachedValidateTargetMethod;
        private static MethodInfo? cachedCanBeUsedByMethod;

        private static MethodBase? TargetMethod()
        {
            MethodInfo? method = GetValidateTargetMethod();
            if (method == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}未找到 CompUsable.ValidateTarget(LocalTargetInfo, bool)，补丁未应用。",
                    ErrorKeyTargetMethodNotFound);
            }

            return method;
        }

        private static bool Prepare()
        {
            return GetValidateTargetMethod() != null;
        }

        private static MethodInfo? GetValidateTargetMethod()
        {
            if (cachedValidateTargetMethod != null)
            {
                return cachedValidateTargetMethod;
            }

            cachedValidateTargetMethod = AccessTools.Method(
                typeof(CompUsable),
                nameof(CompUsable.ValidateTarget),
                new[] { typeof(LocalTargetInfo), typeof(bool) });

            return cachedValidateTargetMethod;
        }

        private static MethodInfo? GetCanBeUsedByMethod()
        {
            if (cachedCanBeUsedByMethod != null)
            {
                return cachedCanBeUsedByMethod;
            }

            cachedCanBeUsedByMethod = AccessTools.Method(
                typeof(CompUsable),
                nameof(CompUsable.CanBeUsedBy),
                new[] { typeof(Pawn), typeof(bool), typeof(bool) });

            return cachedCanBeUsedByMethod;
        }

        private static bool IsPlayerControlledOrAuthorizedCompUsableUser(
            Pawn pawn,
            CompUsable usable)
        {
            return pawn.IsPlayerControlled
                || ColonistLikeCompUsableUtility.IsAuthorizedMechanicalCompUsableUser(
                    pawn,
                    usable);
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            MethodInfo? isPlayerControlledGetter = AccessTools.PropertyGetter(
                typeof(Pawn),
                nameof(Pawn.IsPlayerControlled));
            MethodInfo? canBeUsedByMethod = GetCanBeUsedByMethod();
            MethodInfo? helperMethod = AccessTools.Method(
                typeof(Patch_CompUsable_ValidateTarget_ColonistLikeMechanoid),
                nameof(IsPlayerControlledOrAuthorizedCompUsableUser));

            if (isPlayerControlledGetter == null
                || canBeUsedByMethod == null
                || helperMethod == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}无法解析使用者目标门控相关方法，补丁未应用。",
                    ErrorKeyResolveFailed);
                return codes;
            }

            if (!TryFindUniquePlayerControlledGateAnchor(
                    codes,
                    isPlayerControlledGetter,
                    canBeUsedByMethod,
                    out int getterIndex,
                    out int matchCount))
            {
                Log.ErrorOnce(
                    $"{LogPrefix}CompUsable.ValidateTarget 中 Pawn.IsPlayerControlled 语义锚点预期仅 1 处，实际找到 {matchCount} 处，补丁未应用。",
                    ErrorKeyMatchCount);
                return codes;
            }

            if (!TryExpandPlayerControlledGetter(codes, getterIndex, helperMethod))
            {
                Log.ErrorOnce(
                    $"{LogPrefix}无法安全扩展 IsPlayerControlled 值生产点（目标 getter 上存在 exception block），补丁未应用。",
                    ErrorKeyExpandFailed);
                return codes;
            }

            return codes;
        }

        private static bool TryFindUniquePlayerControlledGateAnchor(
            List<CodeInstruction> codes,
            MethodInfo isPlayerControlledGetter,
            MethodInfo canBeUsedByMethod,
            out int getterIndex,
            out int matchCount)
        {
            getterIndex = -1;
            matchCount = 0;

            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(isPlayerControlledGetter)
                    || !HasCallAfter(codes, i + 1, canBeUsedByMethod))
                {
                    continue;
                }

                matchCount++;
                getterIndex = i;
            }

            return matchCount == 1;
        }

        private static bool HasCallAfter(
            List<CodeInstruction> codes,
            int startIndex,
            MethodInfo method)
        {
            for (int i = startIndex; i < codes.Count; i++)
            {
                if (codes[i].Calls(method))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryExpandPlayerControlledGetter(
            List<CodeInstruction> codes,
            int getterIndex,
            MethodInfo helperMethod)
        {
            if (!CanSafelyInsertBefore(codes, getterIndex))
            {
                return false;
            }

            CodeInstruction getterInstruction = codes[getterIndex];
            CodeInstruction loadUsable = new CodeInstruction(OpCodes.Ldarg_0);

            // Stack before expansion: Pawn.
            // Loading this adds CompUsable -> Pawn, CompUsable.
            TransferEntryLabels(getterInstruction, loadUsable);
            codes.Insert(getterIndex, loadUsable);

            CodeInstruction helperCall = codes[getterIndex + 1];
            helperCall.opcode = OpCodes.Call;
            helperCall.operand = helperMethod;

            return true;
        }

        private static void TransferEntryLabels(
            CodeInstruction source,
            CodeInstruction target)
        {
            if (source.labels.Count == 0)
            {
                return;
            }

            target.labels.AddRange(source.labels);
            source.labels.Clear();
        }

        private static bool CanSafelyInsertBefore(
            List<CodeInstruction> codes,
            int insertIndex)
        {
            if (insertIndex < 0 || insertIndex >= codes.Count)
            {
                return false;
            }

            return codes[insertIndex].blocks.Count == 0;
        }
    }
}
