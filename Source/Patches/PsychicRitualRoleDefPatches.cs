using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(
        typeof(PsychicRitualRoleDef),
        nameof(PsychicRitualRoleDef.PawnCanDo),
        typeof(PsychicRitualRoleDef.Context),
        typeof(Pawn),
        typeof(TargetInfo),
        typeof(PsychicRitualRoleDef.Reason))]
    public static class PsychicRitualRoleDef_PawnCanDo_Patch
    {
        public static void Postfix(
            PsychicRitualRoleDef __instance,
            PsychicRitualRoleDef.Context context,
            Pawn pawn,
            TargetInfo target,
            ref PsychicRitualRoleDef.Reason reason,
            ref bool __result)
        {
            if (!ModsConfig.AnomalyActive || __result)
            {
                return;
            }

            if (!MAPPsychicRitualUtility.PawnCanDoSafeRole(pawn, __instance))
            {
                return;
            }

            // 仅放行 non-humanlike 拒绝；其余失败理由不覆盖。
            if (reason.reasonCode.As<PsychicRitualRoleDef.Condition>() != PsychicRitualRoleDef.Condition.NonHumanlike)
            {
                return;
            }

            __result = true;
            reason = PsychicRitualRoleDef.Reason.None;
        }
    }
}
