using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>仅在资格查询时替换白名单读取，不修改共享 IncidentDef。</summary>
    [HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.CanFireNow))]
    internal static class WheelOfFateHerdMigrationBiomes
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList();
            var field = AccessTools.Field(typeof(IncidentDef), nameof(IncidentDef.allowedBiomes));
            var getter = AccessTools.Method(typeof(WheelOfFateHerdMigrationBiomes), nameof(AllowedBiomes));
            if (field == null || getter == null || codes.Count(code => code.LoadsField(field)) != 2)
            {
                Log.Warning("[MAP-机械族机械师] 旅行热：动物迁徙白名单读取结构不符，已跳过补丁。");
                return codes;
            }
            foreach (CodeInstruction code in codes)
            {
                if (!code.LoadsField(field)) continue;
                // 原位替换，保留分支标签和异常块边界。
                code.opcode = OpCodes.Call;
                code.operand = getter;
            }
            return codes;
        }

        private static List<BiomeDef>? AllowedBiomes(IncidentDef incident)
        {
            return incident.defName == "HerdMigration"
                && GameComponent_WheelOfFateThemes.Current?.ActiveTheme?.ignoreHerdMigrationAllowedBiomes == true
                ? null : incident.allowedBiomes;
        }
    }

    /// <summary>覆盖实际行进和编队/路线预览的共同入口，只缩短玩家队伍的移动耗时。</summary>
    [HarmonyPatch(typeof(CaravanTicksPerMoveUtility), nameof(CaravanTicksPerMoveUtility.GetTicksPerMove),
        new[] { typeof(List<Pawn>), typeof(float), typeof(float), typeof(bool), typeof(StringBuilder) })]
    internal static class WheelOfFateCaravanSpeed
    {
        private static void Postfix(List<Pawn> pawns, bool isShuttle, StringBuilder explanation, ref int __result)
        {
            float factor = GameComponent_WheelOfFateThemes.Current?.ActiveTheme?.caravanSpeedFactor ?? 1f;
            if (isShuttle || __result <= 0 || factor == 1f
                || !StorytellerCompProperties_WheelOfFateRandomMain.IsFinitePositive(factor)
                || pawns == null || !pawns.Any(pawn => pawn?.Faction == Faction.OfPlayer)) return;
            __result = Mathf.Max(1, Mathf.RoundToInt(__result / factor));
            if (explanation != null)
            {
                explanation.AppendLine();
                explanation.Append("  " + "MAP_WheelOfFate.TravelSpeed".Translate(
                    factor.ToStringPercent(), (GenDate.TicksPerDay / (float)__result).ToString("0.#")));
            }
        }
    }

    /// <summary>只缩放发射写入的新冷却；既有冷却及其原版存档不需要额外状态。</summary>
    [HarmonyPatch(typeof(Building_GravEngine), nameof(Building_GravEngine.ConsumeFuel))]
    internal static class WheelOfFateGravEngineCooldown
    {
        private static bool Prepare() => ModsConfig.OdysseyActive;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList();
            var cooldown = AccessTools.Method(typeof(GravshipUtility), nameof(GravshipUtility.LaunchCooldownFromQuality));
            var apply = AccessTools.Method(typeof(WheelOfFateGravEngineCooldown), nameof(ApplyFactor));
            if (cooldown == null || apply == null || codes.Count(code => code.Calls(cooldown)) != 1)
            {
                Log.Warning("[MAP-机械族机械师] 旅行热：逆重引擎发射冷却结构不符，已跳过补丁。");
                return codes;
            }
            int index = codes.FindIndex(code => code.Calls(cooldown));
            codes.Insert(index + 1, new CodeInstruction(OpCodes.Call, apply));
            return codes;
        }

        private static float ApplyFactor(float cooldown)
        {
            float factor = GameComponent_WheelOfFateThemes.Current?.ActiveTheme?.gravEngineCooldownFactor ?? 1f;
            return ModsConfig.OdysseyActive
                && StorytellerCompProperties_WheelOfFateRandomMain.IsFinitePositive(factor)
                ? cooldown * factor : cooldown;
        }
    }
}
