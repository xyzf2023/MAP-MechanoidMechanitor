using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompProperties_PsychicRitualParticipantUser : CompProperties
    {
        public bool allowPsychicRituals = true;
        public bool allowInvoker = true;
        public bool allowChanter = true;
        public bool allowChanterAdvanced = true;
        public bool allowDefender = true;
        public bool allowTargetRoles = false;

        public CompProperties_PsychicRitualParticipantUser()
        {
            compClass = typeof(CompPsychicRitualParticipantUser);
        }
    }

    public class CompPsychicRitualParticipantUser : ThingComp
    {
        public CompProperties_PsychicRitualParticipantUser Props =>
            (CompProperties_PsychicRitualParticipantUser)props;
    }
}
