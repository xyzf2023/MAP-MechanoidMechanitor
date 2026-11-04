using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.MiliraRace
{
    internal static class MiliraButcheryCompatibilityPatches
    {
        internal static bool Enabled;

        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            MethodInfo? chance = AccessTools.DeclaredMethod(typeof(Rand), nameof(Rand.Chance), new[] { typeof(float) });
            MethodInfo? humanlike = AccessTools.PropertyGetter(typeof(RaceProperties), nameof(RaceProperties.Humanlike));
            MethodInfo? raceProps = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.RaceProps));
            MethodInfo? evaluate = AccessTools.DeclaredMethod(typeof(SimpleCurve), nameof(SimpleCurve.Evaluate),
                new[] { typeof(float) });
            MethodInfo? getSkill = AccessTools.DeclaredMethod(typeof(Pawn_SkillTracker),
                nameof(Pawn_SkillTracker.GetSkill), new[] { typeof(SkillDef) });
            MethodInfo? skillLevel = AccessTools.PropertyGetter(typeof(SkillRecord), nameof(SkillRecord.Level));
            FieldInfo? skills = AccessTools.Field(typeof(Pawn), nameof(Pawn.skills));
            FieldInfo? crafting = AccessTools.Field(typeof(SkillDefOf), nameof(SkillDefOf.Crafting));
            MethodInfo? helper = AccessTools.DeclaredMethod(typeof(MiliraButcheryCompatibilityPatches),
                nameof(ResolveChance), new[] { typeof(float), typeof(Pawn), typeof(SimpleCurve) });
            if (chance == null || humanlike == null || raceProps == null || evaluate == null
                || getSkill == null || skillLevel == null || skills == null || crafting == null
                || helper == null || !helper.IsStatic || helper.ReturnType != typeof(float))
                throw new InvalidOperationException("无法解析米莉安拆解概率判断的完整签名。");

            int[] chances = Enumerable.Range(0, codes.Count).Where(i => codes[i].Calls(chance)).ToArray();
            int[] evaluations = Enumerable.Range(0, codes.Count).Where(i => codes[i].Calls(evaluate)).ToArray();
            if (chances.Length != 3 || evaluations.Length != 1)
                throw new InvalidOperationException("米莉安拆解产物的概率调用结构发生变化。");

            int index = chances[1];
            // 固定概率必须仍紧随非人类判断，且不是技能概率或阳晶石概率。
            if (index < 6 || codes[index - 1].opcode != OpCodes.Ldc_R4
                || !(codes[index - 1].operand is float value) || value != 0.1f
                || (codes[index - 2].opcode != OpCodes.Brtrue && codes[index - 2].opcode != OpCodes.Brtrue_S)
                || !codes[index - 3].Calls(humanlike) || !codes[index - 4].Calls(raceProps)
                || codes[index - 5].opcode != OpCodes.Ldfld
                || !(codes[index - 5].operand is FieldInfo butcher) || butcher.FieldType != typeof(Pawn)
                || codes[index - 6].opcode != OpCodes.Ldarg_0)
                throw new InvalidOperationException("米莉安拆解非人类固定概率分支发生变化。");

            int evaluation = evaluations[0];
            // 直接验证上游人类分支确实用同一操作者的制作技能和该曲线调用 Evaluate。
            if (evaluation < 9 || codes[evaluation - 9].opcode != OpCodes.Ldarg_0
                || codes[evaluation - 8].opcode != OpCodes.Ldfld
                || !(codes[evaluation - 8].operand is FieldInfo curve) || curve.FieldType != typeof(SimpleCurve)
                || curve.DeclaringType != butcher.DeclaringType
                || codes[evaluation - 7].opcode != OpCodes.Ldarg_0
                || !codes[evaluation - 6].LoadsField(butcher)
                || !codes[evaluation - 5].LoadsField(skills)
                || !codes[evaluation - 4].LoadsField(crafting)
                || !codes[evaluation - 3].Calls(getSkill) || !codes[evaluation - 2].Calls(skillLevel)
                || codes[evaluation - 1].opcode != OpCodes.Conv_R4
                || evaluation + 1 != chances[0] || chances[0] >= index
                || codes[index].labels.Count != 0 || codes[index].blocks.Count != 0)
                throw new InvalidOperationException("米莉安拆解技能曲线或固定概率调用结构发生变化。");

            // 概率入栈后追加操作者及上游实际构造的曲线；保留唯一一次 Rand.Chance 调用。
            codes.InsertRange(index, new[]
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, butcher),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, curve),
                new CodeInstruction(OpCodes.Call, helper)
            });
            return codes;
        }

        internal static float ResolveChance(float original, Pawn? butcher, SimpleCurve? curve)
        {
            if (!Enabled || Scribe.mode != LoadSaveMode.Inactive
                || MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferPositiveRestore
                || curve == null || !MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(butcher)
                || !MechanoidMechanitorSkillUtility.TryGetPreferredSkillLevel(butcher, SkillDefOf.Crafting, out int level))
                return original;
            return curve.Evaluate(level);
        }
    }
}
