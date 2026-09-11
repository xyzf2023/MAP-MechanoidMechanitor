using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class MechanicalFlightAuthorizationRecord : IExposable
    {
        private Pawn? pawn;
        private MechanicalFlightProfileDef? profile;
        private MechanicalFlightPhase phase;
        private int ticksUntilNextEnergyDrain;
        private IntVec3 emergencyLandingTarget = IntVec3.Invalid;
        private bool lowEnergyWarningSent;
        private bool pendingShutdownAfterLanding;
        private bool groundLandingBlockedNoticeSent;
        private bool groundJobBlockedNoticeSent;

        public Pawn? Pawn => pawn;
        public MechanicalFlightProfileDef? Profile => profile;
        public MechanicalFlightPhase Phase
        {
            get => phase;
            internal set => phase = value;
        }
        public int TicksUntilNextEnergyDrain
        {
            get => ticksUntilNextEnergyDrain;
            internal set => ticksUntilNextEnergyDrain = value;
        }
        public bool IsRuntimeActive => phase != MechanicalFlightPhase.Grounded;
        public bool UsesAerialMovement =>
            ConsumesFlightEnergy || phase == MechanicalFlightPhase.EmergencyApproach;
        public bool ConsumesFlightEnergy =>
            phase == MechanicalFlightPhase.TakingOff
            || phase == MechanicalFlightPhase.Hovering;
        public bool IsEmergencySequence =>
            phase == MechanicalFlightPhase.EmergencyApproach
            || phase == MechanicalFlightPhase.EmergencyLanding
            || phase == MechanicalFlightPhase.Crashing;
        public IntVec3 EmergencyLandingTarget
        {
            get => emergencyLandingTarget;
            internal set => emergencyLandingTarget = value;
        }
        public bool LowEnergyWarningSent
        {
            get => lowEnergyWarningSent;
            internal set => lowEnergyWarningSent = value;
        }
        public bool PendingShutdownAfterLanding
        {
            get => pendingShutdownAfterLanding;
            internal set => pendingShutdownAfterLanding = value;
        }
        public bool GroundLandingBlockedNoticeSent
        {
            get => groundLandingBlockedNoticeSent;
            internal set => groundLandingBlockedNoticeSent = value;
        }
        public bool GroundJobBlockedNoticeSent
        {
            get => groundJobBlockedNoticeSent;
            internal set => groundJobBlockedNoticeSent = value;
        }

        public MechanicalFlightAuthorizationRecord()
        {
        }

        public MechanicalFlightAuthorizationRecord(Pawn pawn, MechanicalFlightProfileDef profile)
        {
            this.pawn = pawn;
            this.profile = profile;
        }

        internal void SetProfile(MechanicalFlightProfileDef newProfile)
        {
            profile = newProfile;
        }

        internal void ResetRuntimeState()
        {
            phase = MechanicalFlightPhase.Grounded;
            ticksUntilNextEnergyDrain = 0;
            emergencyLandingTarget = IntVec3.Invalid;
            lowEnergyWarningSent = false;
            pendingShutdownAfterLanding = false;
            groundLandingBlockedNoticeSent = false;
            groundJobBlockedNoticeSent = false;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Defs.Look(ref profile, "profile");
            Scribe_Values.Look(ref phase, "phase", MechanicalFlightPhase.Grounded);
            Scribe_Values.Look(ref ticksUntilNextEnergyDrain,
                "ticksUntilNextEnergyDrain", 0);
            Scribe_Values.Look(ref emergencyLandingTarget,
                "emergencyLandingTarget", IntVec3.Invalid);
            Scribe_Values.Look(ref lowEnergyWarningSent,
                "lowEnergyWarningSent", false);
            Scribe_Values.Look(ref pendingShutdownAfterLanding,
                "pendingShutdownAfterLanding", false);
            Scribe_Values.Look(ref groundLandingBlockedNoticeSent,
                "groundLandingBlockedNoticeSent", false);
            Scribe_Values.Look(ref groundJobBlockedNoticeSent,
                "groundJobBlockedNoticeSent", false);
        }
    }
}
