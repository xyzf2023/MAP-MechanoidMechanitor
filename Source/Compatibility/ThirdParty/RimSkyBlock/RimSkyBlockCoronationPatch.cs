using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.RimSkyBlock
{
    internal static class RimSkyBlockCoronationPatch
    {
        public static IEnumerable<CodeInstruction> CandidateTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceGetter(instructions,
                AccessTools.PropertyGetter(typeof(MapPawns), nameof(MapPawns.FreeColonists)),
                nameof(Candidates), false);
        }

        public static IEnumerable<CodeInstruction> EligibilityTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceGetter(instructions,
                AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.IsColonist)),
                nameof(IsColonistOrMechanicalRoyal), false);
        }

        public static IEnumerable<CodeInstruction> RitualCandidatesTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            // static CreateRitualRoleAssignments 的第 0 参数是仪式；在筛选之前补充来源，
            // 后续原版 PawnNotAssignableReason、角色权限和空岛 filter 仍全部执行。
            return ReplaceGetter(instructions,
                AccessTools.PropertyGetter(typeof(MapPawns), nameof(MapPawns.FreeColonistsAndPrisonersSpawned)),
                nameof(RitualCandidates), true);
        }

        public static IEnumerable<CodeInstruction> FilterTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            MethodInfo race = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.RaceProps));
            MethodInfo humanlike = AccessTools.PropertyGetter(typeof(RaceProperties), nameof(RaceProperties.Humanlike));
            MethodInfo helper = AccessTools.Method(typeof(RimSkyBlockCoronationPatch), nameof(IsHumanlikeOrRitualMechanitor));
            if (race == null || humanlike == null || helper == null)
                throw new InvalidOperationException("空岛加冕：无法解析种族判断方法。");

            int match = -1;
            for (int i = 0; i + 1 < codes.Count; i++)
            {
                if (!codes[i].Calls(race) || !codes[i + 1].Calls(humanlike)) continue;
                if (match >= 0 || codes[i].blocks.Count != 0 || codes[i + 1].blocks.Count != 0
                    || codes[i + 1].labels.Count != 0)
                    throw new InvalidOperationException("空岛加冕：种族判断不唯一或存在不支持的控制流。");
                match = i;
            }
            if (match < 0) throw new InvalidOperationException("空岛加冕：未找到预期的种族判断。");

            codes[match].opcode = OpCodes.Call;
            codes[match].operand = helper;
            codes.RemoveAt(match + 1);
            return codes;
        }

        private static IEnumerable<CodeInstruction> ReplaceGetter(
            IEnumerable<CodeInstruction> instructions, MethodInfo getter, string helperName, bool loadRitual)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            MethodInfo helper = AccessTools.Method(typeof(RimSkyBlockCoronationPatch), helperName);
            if (getter == null || helper == null)
                throw new InvalidOperationException("空岛加冕：无法解析 属性读取方法 或兼容方法。");

            int match = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(getter)) continue;
                if (match >= 0 || codes[i].blocks.Count != 0)
                    throw new InvalidOperationException($"空岛加冕：{getter.Name} 不唯一或位于异常块边界。");
                match = i;
            }
            if (match < 0) throw new InvalidOperationException($"空岛加冕：未找到 {getter.Name}。");

            CodeInstruction call = codes[match];
            if (loadRitual)
            {
                CodeInstruction load = new CodeInstruction(OpCodes.Ldarg_0);
                load.labels.AddRange(call.labels);
                call.labels.Clear();
                codes.Insert(match, load);
            }
            call.opcode = OpCodes.Call;
            call.operand = helper;
            return codes;
        }

        private static List<Pawn> Candidates(MapPawns mapPawns)
        {
            return RimSkyBlockCompatibilityUtility.AppendMapMechanitors(
                mapPawns.FreeColonists, mapPawns, false);
        }

        private static bool IsColonistOrMechanicalRoyal(Pawn pawn)
        {
            return pawn.IsColonist || RimSkyBlockCompatibilityUtility.CanReceiveTitle(pawn);
        }

        private static bool IsHumanlikeOrRitualMechanitor(Pawn pawn)
        {
            return pawn.RaceProps.Humanlike || RimSkyBlockCompatibilityUtility.CanParticipateInRitual(pawn);
        }

        private static List<Pawn> RitualCandidates(MapPawns mapPawns, Precept_Ritual? ritual)
        {
            List<Pawn> source = mapPawns.FreeColonistsAndPrisonersSpawned;
            // 原版方法共用，但新增候选严格限制在空岛加冕，不改变其他仪式的候选来源。
            return ritual?.def?.defName == RimSkyBlockCompatibilityUtility.CoronationDefName
                ? RimSkyBlockCompatibilityUtility.AppendMapMechanitors(source, mapPawns, true)
                : source;
        }
    }
}
