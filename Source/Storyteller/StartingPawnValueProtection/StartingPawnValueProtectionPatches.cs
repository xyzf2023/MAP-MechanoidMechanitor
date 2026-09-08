using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 直接修改带保护 Hediff 的 Pawn 最终 MarketValue。
    /// 因此所有遵循 Pawn.MarketValue / GetStatValue(MarketValue) 的原版与第三方逻辑都会自然获得保护效果。
    /// </summary>
    [HarmonyPatch(typeof(StatWorker_MarketValue), nameof(StatWorker_MarketValue.GetValueUnfinalized))]
    internal static class StartingPawnValueProtection_MarketValue_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(StatRequest req, ref float __result)
        {
            if (req.Thing is not Pawn pawn)
            {
                return;
            }

            float factor = StartingPawnValueProtectionUtility.GetEffectiveFactor(pawn);
            if (factor >= 1f)
            {
                return;
            }

            __result *= factor;
        }
    }

    /// <summary>
    /// 机械族机械师开局主角的保护添加入口。
    /// 只负责添加 Hediff；价值系统本身不依赖此入口。
    /// </summary>
    [HarmonyPatch(typeof(ScenPart_MechanoidMechanitor), nameof(ScenPart_MechanoidMechanitor.PlayerStartingThings))]
    internal static class StartingPawnValueProtection_MechanitorStart_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(ref IEnumerable<Thing> __result)
        {
            __result = ApplyProtection(__result);
        }

        private static IEnumerable<Thing> ApplyProtection(IEnumerable<Thing> source)
        {
            foreach (Thing thing in source)
            {
                if (thing is Pawn pawn
                    && MechanoidMechanitorScenarioUtility.IsScenarioActive
                    && StartingPawnValueProtectionSettings.ShouldProtectStartingMechanitor)
                {
                    StartingPawnValueProtectionUtility.TryAddProtection(pawn);
                }

                yield return thing;
            }
        }
    }

    /// <summary>
    /// 对机械族机械师剧本中的原版 ScenPart_StartingMech 生成物应用可选保护。
    /// 仅包装本次 Scenario 的开局枚举，不扫描正式地图，因此不会影响后续加入或制造的机械族。
    /// </summary>
    [HarmonyPatch(typeof(ScenPart_StartingMech), nameof(ScenPart_StartingMech.PlayerStartingThings))]
    internal static class StartingPawnValueProtection_OtherStartingMechs_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(ref IEnumerable<Thing> __result)
        {
            __result = ApplyProtection(__result);
        }

        private static IEnumerable<Thing> ApplyProtection(IEnumerable<Thing> source)
        {
            foreach (Thing thing in source)
            {
                if (thing is Pawn pawn
                    && pawn.RaceProps.IsMechanoid
                    && MechanoidMechanitorScenarioUtility.IsScenarioActive
                    && StartingPawnValueProtectionSettings.ShouldProtectOtherStartingMechs)
                {
                    StartingPawnValueProtectionUtility.TryAddProtection(pawn);
                }

                yield return thing;
            }
        }
    }

    /// <summary>
    /// Storyteller 对玩家 Pawn 的 combatPower 贡献使用与 MarketValue 相同的保护倍率。
    /// 原版 DefaultThreatPointsNow 当前有三处 Pawn.kindDef.combatPower：
    /// 可训练动物、殖民地机械族、亚人。统一替换可确保该 Hediff 对任意 Pawn 类型保持同一语义。
    /// </summary>
    [HarmonyPatch(typeof(StorytellerUtility), nameof(StorytellerUtility.DefaultThreatPointsNow))]
    internal static class StartingPawnValueProtection_CombatPower_Patch
    {
        private const int ExpectedCombatPowerReads = 3;

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = instructions.ToList();

            FieldInfo? kindDefField = AccessTools.Field(typeof(Pawn), nameof(Pawn.kindDef));
            FieldInfo? combatPowerField = AccessTools.Field(
                typeof(PawnKindDef),
                nameof(PawnKindDef.combatPower));
            MethodInfo? applyFactorMethod = AccessTools.Method(
                typeof(StartingPawnValueProtectionUtility),
                nameof(StartingPawnValueProtectionUtility.ApplyCombatPowerFactor));

            if (kindDefField == null || combatPowerField == null || applyFactorMethod == null)
            {
                Log.Warning(
                    "[MAP-机械族机械师] 开局价值保护：无法解析 Storyteller combatPower 补丁依赖成员，已安全跳过该补丁。");
                return codes;
            }

            List<int> matches = new List<int>();
            for (int i = 2; i < codes.Count; i++)
            {
                if (!codes[i].LoadsField(combatPowerField)
                    || !codes[i - 1].LoadsField(kindDefField)
                    || !codes[i - 2].IsLdloc())
                {
                    continue;
                }

                matches.Add(i);
            }

            if (matches.Count != ExpectedCombatPowerReads)
            {
                Log.Warning(
                    "[MAP-机械族机械师] 开局价值保护：StorytellerUtility.DefaultThreatPointsNow 的 combatPower 结构与预期不一致（匹配 "
                    + matches.Count
                    + " 处，预期 "
                    + ExpectedCombatPowerReads
                    + " 处），已安全跳过该补丁。");
                return codes;
            }

            for (int matchIndex = matches.Count - 1; matchIndex >= 0; matchIndex--)
            {
                int combatPowerIndex = matches[matchIndex];
                CodeInstruction pawnLoad = new CodeInstruction(
                    codes[combatPowerIndex - 2].opcode,
                    codes[combatPowerIndex - 2].operand);

                codes.Insert(combatPowerIndex + 1, pawnLoad);
                codes.Insert(
                    combatPowerIndex + 2,
                    new CodeInstruction(OpCodes.Call, applyFactorMethod));
            }

            return codes;
        }
    }
}
