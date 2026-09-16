using System;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [Flags]
    public enum MechanicalFlightAuthorizationSource
    {
        None = 0,
        Legacy = 1 << 0,
        Direct = 1 << 1,
        Innate = 1 << 2,
        TemporaryFusion = 1 << 3,
        Debug = 1 << 4
    }

    public sealed class MechanicalFlightAuthorizationRecord : IExposable
    {
        private Pawn? pawn;
        private MechanicalFlightProfileDef? profile;
        private MechanicalFlightAuthorizationSource authorizationSources;
        private MechanicalFlightPhase phase;
        private MechanicalFlightPurpose purpose;
        private int ticksUntilNextEnergyDrain;
        private IntVec3 emergencyLandingTarget = IntVec3.Invalid;
        private bool lowEnergyWarningSent;
        private bool pendingShutdownAfterLanding;
        private bool groundLandingBlockedNoticeSent;
        private bool groundJobBlockedNoticeSent;
        // 非空表示本次飞行有待落地重判的撤离意图；绑定地图防止跨图误用。
        internal Map? PendingExitMap;

        // 运行时字段（不序列化）：迫降目标下一次完整验证 Tick 与验证时所在 Map。
        // 读档后默认 0/null，迫使恢复迫降流程时立即重新验证。
        internal int NextEmergencyTargetValidationTick;
        internal Map? EmergencyTargetMap;

        public Pawn? Pawn => pawn;
        public MechanicalFlightProfileDef? Profile => profile;
        public MechanicalFlightAuthorizationSource AuthorizationSources => authorizationSources;
        public MechanicalFlightPhase Phase
        {
            get => phase;
            internal set => phase = value;
        }
        public MechanicalFlightPurpose Purpose
        {
            get => purpose;
            internal set => purpose = value;
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

        public MechanicalFlightAuthorizationRecord(
            Pawn pawn,
            MechanicalFlightProfileDef profile,
            MechanicalFlightAuthorizationSource authorizationSource)
        {
            this.pawn = pawn;
            this.profile = profile;
            authorizationSources = authorizationSource;
        }

        internal bool HasAuthorizationSource(
            MechanicalFlightAuthorizationSource source)
        {
            return source != MechanicalFlightAuthorizationSource.None
                && (authorizationSources & source) == source;
        }

        internal bool AddAuthorizationSource(
            MechanicalFlightAuthorizationSource source)
        {
            if (source == MechanicalFlightAuthorizationSource.None
                || HasAuthorizationSource(source))
            {
                return false;
            }

            authorizationSources |= source;
            return true;
        }

        internal bool RemoveAuthorizationSource(
            MechanicalFlightAuthorizationSource source)
        {
            if (!HasAuthorizationSource(source))
            {
                return false;
            }

            authorizationSources &= ~source;
            return true;
        }

        internal void SetProfile(MechanicalFlightProfileDef newProfile)
        {
            profile = newProfile;
        }

        internal void ResetRuntimeState()
        {
            phase = MechanicalFlightPhase.Grounded;
            purpose = MechanicalFlightPurpose.Normal;
            ticksUntilNextEnergyDrain = 0;
            emergencyLandingTarget = IntVec3.Invalid;
            lowEnergyWarningSent = false;
            pendingShutdownAfterLanding = false;
            groundLandingBlockedNoticeSent = false;
            groundJobBlockedNoticeSent = false;
            PendingExitMap = null;
            NextEmergencyTargetValidationTick = 0;
            EmergencyTargetMap = null;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Defs.Look(ref profile, "profile");
            Scribe_Values.Look(
                ref authorizationSources,
                "authorizationSources",
                MechanicalFlightAuthorizationSource.Legacy);
            Scribe_Values.Look(ref phase, "phase", MechanicalFlightPhase.Grounded);
            Scribe_Values.Look(ref purpose, "purpose", MechanicalFlightPurpose.Normal);
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
            Scribe_References.Look(ref PendingExitMap, "pendingExitMap");
        }
    }
}
