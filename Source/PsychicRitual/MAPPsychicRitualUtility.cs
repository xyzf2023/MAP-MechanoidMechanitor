using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class MAPPsychicRitualUtility
    {
        public static bool IsAllowedPsychicRitualParticipant(Pawn? pawn)
        {
            if (!ModsConfig.AnomalyActive || pawn == null)
            {
                return false;
            }

            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn)
                || !MechanoidMechanitorRoleUtility.AllowsPsychicRituals(pawn))
            {
                return false;
            }

            if (!pawn.Spawned || pawn.Dead || pawn.Downed)
            {
                return false;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                return false;
            }

            return pawn.health?.capacities?.CapableOf(PawnCapacityDefOf.Moving) == true;
        }

        public static bool IsSafeRoleWhitelisted(PsychicRitualRoleDef? roleDef)
        {
            if (roleDef == null)
            {
                return false;
            }

            string roleDefName = roleDef.defName;
            return roleDefName == "Invoker"
                || roleDefName == "Chanter"
                || roleDefName == "ChanterAdvanced"
                || roleDefName == "Defender";
        }

        public static bool PawnCanDoSafeRole(Pawn? pawn, PsychicRitualRoleDef? roleDef)
        {
            if (!IsAllowedPsychicRitualParticipant(pawn)
                || roleDef == null
                || !IsSafeRoleWhitelisted(roleDef))
            {
                return false;
            }

            if (MechanoidMechanitorRoleUtility.IsAcquiredMechanoidMechanitor(pawn))
            {
                return true;
            }

            CompPsychicRitualParticipantUser? comp =
                pawn!.GetComp<CompPsychicRitualParticipantUser>();
            if (comp == null)
            {
                return false;
            }

            switch (roleDef.defName)
            {
                case "Invoker":
                    return comp.Props.allowInvoker;
                case "Chanter":
                    return comp.Props.allowChanter;
                case "ChanterAdvanced":
                    return comp.Props.allowChanterAdvanced;
                case "Defender":
                    return comp.Props.allowDefender;
                default:
                    return false;
            }
        }
    }
}
