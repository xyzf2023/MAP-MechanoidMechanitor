#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;
using Verse.AI;

// Shared source revision 2. Keep the two repositories' copies byte-identical.
// MAP's existing net472 build uses NETFRAMEWORK; GD5 Expansion builds net10.0.
#if NETFRAMEWORK
namespace MAP_MechanoidMechanitor
#else
namespace MK3expand
#endif
{
    internal static class MovementCostImmunityCore
    {
        internal const int ProtocolVersion = 1;
        internal const int SemanticsVersion = 1;
        private static readonly Dictionary<string, Func<Pawn, bool>> Sources =
            new Dictionary<string, Func<Pawn, bool>>(StringComparer.Ordinal);
        private static Func<Pawn, bool>[] externalSources = Array.Empty<Func<Pawn, bool>>();
        private static Func<Pawn, bool>? localSource;
        private static string owner = string.Empty;
        private static string logPrefix = string.Empty;
        private static bool applied;
        private static bool installed;

        internal static MethodInfo? Target => AccessTools.DeclaredMethod(
            typeof(Pawn_PathFollower), "CostToMoveIntoCell", new[] { typeof(Pawn), typeof(IntVec3) });
        internal static bool IsActive => installed && applied;

        internal static void Install(Harmony harmony, Func<Pawn, bool> source, string prefix)
        {
            installed = false;
            localSource = source;
            owner = harmony.Id;
            logPrefix = prefix;
            MethodInfo? target = Target;
            if (target == null || target.ReturnType != typeof(float) || !target.IsStatic)
            {
                Fail("原版移动代码结构变化：找不到 CostToMoveIntoCell(Pawn, IntVec3) 方法。");
                return;
            }
            try
            {
                if (!OriginalShapeMatches())
                {
                    Fail("原版移动代码结构变化：未打补丁的原始 IL 不符合成本过滤入口，保留现有方法。");
                    return;
                }
                installed = harmony.Patch(target, transpiler: new HarmonyMethod(
                    AccessTools.DeclaredMethod(typeof(MovementCostImmunityCore), nameof(Transpile)))
                { priority = Priority.Last }) != null;
            }
            catch (Exception ex)
            {
                Fail("无法安装移动成本补丁：" + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // Registration succeeds only after checking both the owner and emitted helper calls.
        internal static bool TryRegister(int protocol, int semantics, string sourceId, Func<Pawn, bool> source)
        {
            if (protocol != ProtocolVersion || semantics != SemanticsVersion
                || string.IsNullOrEmpty(sourceId) || source == null || !Verify())
                return false;
            Sources[sourceId] = source;
            externalSources = new Func<Pawn, bool>[Sources.Count];
            Sources.Values.CopyTo(externalSources, 0);
            return true;
        }

        internal static bool Verify()
        {
            // Install already diagnoses a missing/changed original or installation exception.
            if (!installed)
                return false;
            try
            {
                MethodInfo? target = Target;
                Patches? patches = target == null ? null : Harmony.GetPatchInfo(target);
                MethodInfo transpiler = AccessTools.DeclaredMethod(typeof(MovementCostImmunityCore), nameof(Transpile));
                bool registered = false;
                if (patches != null)
                    foreach (Patch patch in patches.Transpilers)
                        registered |= patch.owner == owner && patch.PatchMethod == transpiler;
                if (installed && target != null && registered)
                {
                    List<CodeInstruction> codes = PatchProcessor.GetCurrentInstructions(target);
                    if (applied && FindUniqueCall(codes, Helper(nameof(GridCost))) >= 0
                        && FindUniqueCall(codes, Helper(nameof(BuildingCost))) >= 0
                        && FindUniqueCall(codes, Helper(nameof(TerrainFactor))) >= 0)
                        return true;
                }
                Fail(OriginalShapeMatches()
                    ? "第三方移动补丁冲突：原始 IL 正常，但补丁登记或最终成本调用链不符合预期；保留现有移动。"
                    : "原版移动代码结构变化：原始成本入口不再匹配；保留现有移动。");
            }
            catch (Exception ex)
            {
                Fail("无法确认移动成本补丁：" + ex.GetType().Name + ": " + ex.Message);
            }
            return false;
        }

        internal static bool HasImmunity(Pawn? pawn)
        {
            if (pawn == null)
                return false;
            if (localSource?.Invoke(pawn) == true)
                return true;
            foreach (Func<Pawn, bool> source in externalSources)
                if (source(pawn))
                    return true;
            return false;
        }

        private static MethodInfo? Helper(string name) =>
            AccessTools.DeclaredMethod(typeof(MovementCostImmunityCore), name);

        private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        {
            applied = false;
            var codes = new List<CodeInstruction>(instructions);
            if (!TryFindCostCalls(codes, out int gridIndex, out int buildingIndex, out int factorIndex))
            {
                Fail(OriginalShapeMatches()
                    ? "第三方移动补丁冲突：原始 IL 正常，但有效调用链已改写，无法安全接入本地成本逻辑；保留现有移动。"
                    : "原版移动代码结构变化：原始成本入口不匹配；保留现有移动。");
                return codes;
            }

            // Validate every anchor before changing any instruction. Leave urgency/following intact.
            var result = new List<CodeInstruction>(codes.Count + 2);
            for (int i = 0; i < codes.Count; i++)
            {
                CodeInstruction code = new CodeInstruction(codes[i]);
                if (i == gridIndex || i == factorIndex)
                {
                    var loadPawn = new CodeInstruction(OpCodes.Ldarg_0);
                    loadPawn.labels.AddRange(code.labels);
                    code.labels.Clear();
                    result.Add(loadPawn);
                    code.opcode = OpCodes.Call;
                    code.operand = Helper(i == gridIndex ? nameof(GridCost) : nameof(TerrainFactor));
                }
                else if (i == buildingIndex)
                {
                    code.opcode = OpCodes.Call;
                    code.operand = Helper(nameof(BuildingCost));
                }
                result.Add(code);
            }
            applied = true;
            return result;
        }

        // Raw IL is deliberately separate from GetCurrentInstructions' transpiler output.
        internal static bool OriginalShapeMatches()
        {
            MethodInfo? target = Target;
            return target != null && target.IsStatic && target.ReturnType == typeof(float)
                && TryFindCostCalls(PatchProcessor.GetOriginalInstructions(target), out _, out _, out _);
        }

        private static bool TryFindCostCalls(List<CodeInstruction> codes,
            out int gridIndex, out int buildingIndex, out int factorIndex)
        {
            MethodInfo? grid = AccessTools.DeclaredMethod(typeof(PathGrid), nameof(PathGrid.CalculatedCostAt),
                new[] { typeof(IntVec3), typeof(bool), typeof(IntVec3), typeof(int?) });
            MethodInfo? building = AccessTools.Method(typeof(Building), nameof(Building.PathWalkCostFor),
                new[] { typeof(Pawn) });
            MethodInfo? factor = AccessTools.Method(typeof(Dictionary<string, float>), "TryGetValue",
                new[] { typeof(string), typeof(float).MakeByRefType() });
            gridIndex = FindUniqueCall(codes, grid);
            buildingIndex = FindUniqueCall(codes, building);
            factorIndex = FindUniqueCall(codes, factor);
            if (gridIndex < 0 || buildingIndex < 0 || factorIndex < 0
                || codes[gridIndex].blocks.Count != 0 || codes[buildingIndex].blocks.Count != 0
                || codes[factorIndex].blocks.Count != 0)
            {
                return false;
            }
            return true;
        }

        internal static int FindUniqueCall(List<CodeInstruction> codes, MethodInfo? method)
        {
            int found = -1;
            if (method == null)
                return found;
            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(method))
                    continue;
                if (found >= 0)
                    return -1;
                found = i;
            }
            return found;
        }

        private static int GridCost(PathGrid grid, IntVec3 cell, bool perceivedStatic,
            IntVec3 prevCell, int? baseCostOverride, Pawn pawn)
        {
            int original = grid.CalculatedCostAt(cell, perceivedStatic, prevCell, baseCostOverride);
            return FilterGridCost(original, cell, perceivedStatic, pawn, HasImmunity(pawn));
        }

        // Optional-peer bridges call OUR calculators and skip the peer method for our pawns.
        internal static int CalculateGridCost(PathGrid grid, IntVec3 cell, bool perceivedStatic,
            IntVec3 prevCell, int? baseCostOverride, Pawn pawn, bool immune)
        {
            int original = grid.CalculatedCostAt(cell, perceivedStatic, prevCell, baseCostOverride);
            return FilterGridCost(original, cell, perceivedStatic, pawn, immune);
        }

        private static int FilterGridCost(int original, IntVec3 cell, bool perceivedStatic, Pawn pawn, bool immune)
        {
            Map? map = pawn?.Map;
            return !immune || map == null || !cell.InBounds(map)
                || perceivedStatic || original >= 10000 ? original : 0;
        }

        private static ushort BuildingCost(Building building, Pawn pawn) =>
            CalculateBuildingCost(building, pawn, HasImmunity(pawn));

        internal static ushort CalculateBuildingCost(Building building, Pawn pawn, bool immune) =>
            immune ? (ushort)0 : building.PathWalkCostFor(pawn);

        private static bool TerrainFactor(Dictionary<string, float> factors, string tag,
            out float factor, Pawn pawn)
        {
            bool found = factors.TryGetValue(tag, out factor);
            if (found && factor < 1f && HasImmunity(pawn))
                factor = NormalizeTerrainFactor(factor, true);
            return found;
        }

        internal static bool CalculateTerrainFactor(Dictionary<string, float> factors, string tag,
            out float factor, bool immune)
        {
            bool found = factors.TryGetValue(tag, out factor);
            if (found)
                factor = NormalizeTerrainFactor(factor, immune);
            return found;
        }

        private static float NormalizeTerrainFactor(float factor, bool immune) =>
            immune && factor < 1f ? 1f : factor;

        private static void Fail(string detail)
        {
            applied = false;
            Log.ErrorOnce(logPrefix + "移动减速豁免失效：" + detail,
                unchecked(owner.GetHashCode() ^ 0x4D430001));
        }
    }
}
