using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>仅标记本主题正在执行的袭击参数，在原版完整点数修正之后乘最终系数。</summary>
    [HarmonyPatch(typeof(IncidentWorker_Raid), nameof(IncidentWorker_Raid.TryGenerateRaidInfo))]
    internal static class WheelOfFateRaidPoints
    {
        private const float FinalPointsFactor = 0.5f;
        private static readonly HashSet<IncidentParms> scaledRaids = new HashSet<IncidentParms>();
        private static bool installed;

        internal static bool TryFire(FiringIncident incident)
        {
            // 补丁结构不符时已在加载期报告；不得退化成原强度的四波袭击。
            if (!installed) return false;
            scaledRaids.Add(incident.parms);
            try
            {
                return Find.Storyteller.TryFire(incident);
            }
            finally
            {
                scaledRaids.Remove(incident.parms);
            }
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            installed = false;
            var codes = instructions.ToList();
            var adjusted = AccessTools.Method(typeof(IncidentWorker_Raid), nameof(IncidentWorker_Raid.AdjustedRaidPoints));
            var generate = AccessTools.Method(typeof(RaidStrategyWorker), nameof(RaidStrategyWorker.TryGenerateThreats));
            var apply = AccessTools.Method(typeof(WheelOfFateRaidPoints), nameof(ApplyFinalFactor));
            var generateScaled = AccessTools.Method(typeof(WheelOfFateRaidPoints), nameof(GenerateThreats));
            if (adjusted == null || generate == null || apply == null || generateScaled == null
                || codes.Count(code => code.Calls(adjusted)) != 1
                || codes.Count(code => code.Calls(generate)) != 1)
            {
                Log.Error("[MAP-机械族机械师] 四面楚歌：袭击点数生成结构不符，已停用主题连波以避免生成错误强度的袭击。");
                return codes;
            }

            CodeInstruction generation = codes.First(code => code.Calls(generate));
            generation.opcode = OpCodes.Call;
            generation.operand = generateScaled;
            int index = codes.FindIndex(code => code.Calls(adjusted));
            // 保留 AdjustedRaidPoints 及其补丁的返回值，随后乘系数，再写入 parms.points。
            codes.Insert(index + 1, new CodeInstruction(OpCodes.Ldarg_1));
            codes.Insert(index + 2, new CodeInstruction(OpCodes.Call, apply));
            installed = true;
            return codes;
        }

        private static float ApplyFinalFactor(float points, IncidentParms parms) =>
            scaledRaids.Contains(parms) ? points * FinalPointsFactor : points;

        private static void GenerateThreats(RaidStrategyWorker worker, IncidentParms parms)
        {
            if (!scaledRaids.Contains(parms))
            {
                worker.TryGenerateThreats(parms);
                return;
            }

            // 机械族围攻在正式点数赋值之前生成集群草图，必须也使用完整修正后的半额点数。
            // 暂时改点数仅供预生成读取，随后恢复，避免后面的正常结算再次缩放。
            float originalPoints = parms.points;
            try
            {
                parms.points = IncidentWorker_Raid.AdjustedRaidPoints(originalPoints, parms.raidArrivalMode,
                    parms.raidStrategy, parms.faction, parms.pawnGroupKind ?? PawnGroupKindDefOf.Combat,
                    parms.target, parms.raidAgeRestriction) * FinalPointsFactor;
                worker.TryGenerateThreats(parms);
            }
            finally
            {
                parms.points = originalPoints;
            }
        }
    }
}
