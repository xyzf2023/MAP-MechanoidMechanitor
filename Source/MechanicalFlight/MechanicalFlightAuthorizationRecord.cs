using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class MechanicalFlightAuthorizationRecord : IExposable
    {
        private Pawn? pawn;
        private MechanicalFlightProfileDef? profile;
        private MechanicalFlightPhase phase;
        private int ticksUntilNextEnergyDrain;

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
        public bool ConsumesFlightEnergy =>
            phase == MechanicalFlightPhase.TakingOff
            || phase == MechanicalFlightPhase.Hovering;

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
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Defs.Look(ref profile, "profile");
            Scribe_Values.Look(ref phase, "phase", MechanicalFlightPhase.Grounded);
            Scribe_Values.Look(ref ticksUntilNextEnergyDrain,
                "ticksUntilNextEnergyDrain", 0);
        }
    }
}
