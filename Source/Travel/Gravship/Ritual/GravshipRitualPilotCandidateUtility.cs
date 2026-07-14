using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 重力船发射仪式中 pilot/copilot 机械驾驶员的共享候选资格。
    /// 不覆盖船员/旁观者资格（见 GravshipRitualCrewUtility）。
    /// </summary>
    public static class GravshipRitualPilotCandidateUtility
    {
        public static bool IsEligiblePilotCandidate(Pawn? pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            if (!CompGravshipPilotUser.PawnCanUseGravshipPilotConsole(pawn))
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

            if (pawn.GuestStatus == GuestStatus.Prisoner)
            {
                return false;
            }

            if (pawn.health?.capacities?.CapableOf(PawnCapacityDefOf.Moving) != true)
            {
                return false;
            }

            if (pawn.skills == null
                || pawn.skills.GetSkill(SkillDefOf.Intellectual).TotallyDisabled)
            {
                return false;
            }

            return true;
        }
    }
}
