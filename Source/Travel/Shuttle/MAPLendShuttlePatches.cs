using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>只扩展借出任务的指定穿梭机；不改变 Pawn 的全局殖民者身份。</summary>
    internal static class MAPLendShuttleUtility
    {
        // 四处身份判断必须一起成功，否则不能让机械师登船后遗漏任务归还名单。
        internal static bool AllowedPatched;
        internal static bool AllowedNowPatched;
        internal static bool CountPatched;
        internal static bool LendPatched;

        internal static bool Ready => AllowedPatched && AllowedNowPatched && CountPatched && LendPatched;

        internal static bool IsEligibleMechanitor(Pawn? pawn)
        {
            return pawn != null
                && pawn.RaceProps?.IsMechanoid == true
                && pawn.Faction == Faction.OfPlayer
                && pawn.HostFaction == null
                && !pawn.Dead
                && !pawn.Destroyed
                && !pawn.Discarded
                && GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(pawn, out _);
        }

        internal static bool IsLendShuttle(CompShuttle? shuttle)
        {
            if (!Ready || shuttle?.parent == null || Find.QuestManager == null)
            {
                return false;
            }

            foreach (Quest quest in Find.QuestManager.QuestsListForReading)
            {
                if (quest == null || quest.State != QuestState.Ongoing)
                {
                    continue;
                }

                foreach (QuestPart part in quest.PartsListForReading)
                {
                    if (part is QuestPart_LendColonistsToFaction lend
                        && lend.shuttle == shuttle.parent
                        && lend.lendColonistsToFaction != null)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        internal static bool IsSupportedLendPassenger(Pawn? pawn, CompShuttle? shuttle)
        {
            return IsEligibleMechanitor(pawn) && IsLendShuttle(shuttle);
        }

        private static bool IsColonistOrLendPassenger(Pawn pawn, CompShuttle shuttle)
        {
            return pawn.IsColonist || IsSupportedLendPassenger(pawn, shuttle);
        }

        private static bool IsFreeColonistOrLendPassenger(Pawn pawn, CompShuttle shuttle)
        {
            return pawn.IsFreeColonist || IsSupportedLendPassenger(pawn, shuttle);
        }

        private static bool IsFreeColonistOrLentMechanitor(Pawn pawn, QuestPart_LendColonistsToFaction part)
        {
            return pawn.IsFreeColonist
                || (Ready && IsEligibleMechanitor(pawn)
                    && part?.quest?.State == QuestState.Ongoing
                    && part.lendColonistsToFaction != null);
        }

        internal static IEnumerable<CodeInstruction> ReplaceIdentityCheck(
            IEnumerable<CodeInstruction> instructions, MethodBase original, bool freeColonist, bool lendPart)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            MethodInfo? getter = AccessTools.PropertyGetter(typeof(Pawn),
                freeColonist ? nameof(Pawn.IsFreeColonist) : nameof(Pawn.IsColonist));
            MethodInfo? replacement = AccessTools.Method(typeof(MAPLendShuttleUtility),
                lendPart ? nameof(IsFreeColonistOrLentMechanitor)
                    : freeColonist ? nameof(IsFreeColonistOrLendPassenger)
                    : nameof(IsColonistOrLendPassenger));
            int index = -1;
            int matches = 0;
            if (getter != null && replacement != null)
            {
                for (int i = 0; i < codes.Count; i++)
                {
                    if (codes[i].Calls(getter))
                    {
                        index = i;
                        matches++;
                    }
                }
            }

            bool success = matches == 1 && codes[index].blocks.Count == 0;
            SetPatched(original, success);
            if (!success)
            {
                Log.Error($"[MAP-机械族机械师] 借出任务穿梭机兼容：{original.DeclaringType?.Name}.{original.Name} 身份判断预期 1 处，实际 {matches} 处或存在异常处理边界；相关兼容功能已禁用。");
                return codes;
            }

            CodeInstruction loadInstance = new CodeInstruction(OpCodes.Ldarg_0);
            loadInstance.labels.AddRange(codes[index].labels);
            codes[index].labels.Clear();
            codes.Insert(index, loadInstance);
            codes[index + 1].opcode = OpCodes.Call;
            codes[index + 1].operand = replacement;
            return codes;
        }

        private static void SetPatched(MethodBase original, bool value)
        {
            if (original.DeclaringType == typeof(QuestPart_LendColonistsToFaction)) LendPatched = value;
            else if (original.Name == nameof(CompShuttle.IsAllowed)) AllowedPatched = value;
            else if (original.Name == nameof(CompShuttle.IsAllowedNow)) AllowedNowPatched = value;
            else if (original.Name == "get_ContainedColonistCount") CountPatched = value;
        }
    }

    [HarmonyPatch(typeof(CompShuttle), nameof(CompShuttle.IsAllowed))]
    internal static class MAPLendShuttleAllowedPatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
            => MAPLendShuttleUtility.ReplaceIdentityCheck(instructions, __originalMethod, false, false);
    }

    [HarmonyPatch(typeof(CompShuttle), nameof(CompShuttle.IsAllowedNow))]
    internal static class MAPLendShuttleAllowedNowPatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
            => MAPLendShuttleUtility.ReplaceIdentityCheck(instructions, __originalMethod, false, false);
    }

    [HarmonyPatch(typeof(CompShuttle), "get_ContainedColonistCount")]
    internal static class MAPLendShuttleCountPatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
            => MAPLendShuttleUtility.ReplaceIdentityCheck(instructions, __originalMethod, true, false);
    }

    [HarmonyPatch(typeof(QuestPart_LendColonistsToFaction), "Enable")]
    internal static class MAPLendShuttleLendPatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
            => MAPLendShuttleUtility.ReplaceIdentityCheck(instructions, __originalMethod, true, true);
    }
}
