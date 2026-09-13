using System;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 本次合体实例的唯一权威数据。唯一保存源 Pawn、目标人类、合体服装实例、
    /// 能源、结构稳定值、属性快照、白名单载荷与异常恢复信息；服装 Comp、
    /// Gizmo、飞行与渲染系统只能读取这里。
    /// </summary>
    public sealed class MechFusionSession : IExposable
    {
        private string? sessionId;
        private Pawn? sourcePawn;
        private Pawn? wearerPawn;
        private Thing? fusionApparel;
        private ThingDef? sourceThingDef;
        private MechFusionSessionState state;
        private MechFusionExitReason exitReason;
        private float currentEnergy;
        private float maxEnergy;
        private float currentStability;
        private float maxStability;
        private int startTick;
        private bool teardownDeferred;
        private Map? pendingMap;
        private IntVec3 pendingPosition = IntVec3.Invalid;
        private Rot4 pendingRotation = Rot4.South;

        public string SessionId
        {
            get
            {
                EnsureInitialized();
                return sessionId!;
            }
        }

        public Pawn? SourcePawn => sourcePawn;

        public Pawn? WearerPawn => wearerPawn;

        public Thing? FusionApparel => fusionApparel;

        public ThingDef? SourceThingDef => sourceThingDef;

        public MechFusionSessionState State => state;

        public MechFusionExitReason ExitReason => exitReason;

        public float CurrentEnergy => currentEnergy;

        public float MaxEnergy => maxEnergy;

        public float CurrentStability => currentStability;

        public float MaxStability => maxStability;

        public int StartTick => startTick;

        public bool TeardownDeferred
        {
            get => teardownDeferred;
            internal set => teardownDeferred = value;
        }

        public Map? PendingMap => pendingMap;

        public IntVec3 PendingPosition => pendingPosition;

        public Rot4 PendingRotation => pendingRotation;

        public bool IsActive =>
            state == MechFusionSessionState.Starting
            || state == MechFusionSessionState.Active;

        public bool IsEnding => state == MechFusionSessionState.Ending;

        public bool IsPendingRecovery =>
            state == MechFusionSessionState.PendingRecovery;

        public MechFusionSession()
        {
        }

        internal MechFusionSession(
            Pawn source,
            Pawn wearer,
            Thing? apparel,
            ThingDef sourceDef,
            int startTick)
        {
            sourcePawn = source;
            wearerPawn = wearer;
            fusionApparel = apparel;
            sourceThingDef = sourceDef;
            this.startTick = startTick;
            state = MechFusionSessionState.Starting;
            EnsureInitialized();
            UpdateRecoveryLocation(wearer.Map, wearer.Position, wearer.Rotation);
        }

        internal void EnsureInitialized()
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                sessionId = Guid.NewGuid().ToString("N");
            }
        }

        internal void BindApparel(Thing? apparel)
        {
            fusionApparel = apparel;
        }

        internal void SetState(MechFusionSessionState newState)
        {
            state = newState;
        }

        internal void SetExitReason(MechFusionExitReason reason)
        {
            if (state != MechFusionSessionState.Ending)
            {
                exitReason = reason;
            }
        }

        internal void SetEnergy(float current, float max)
        {
            currentEnergy = Math.Max(0f, current);
            maxEnergy = Math.Max(0f, max);
            if (maxEnergy > 0f && currentEnergy > maxEnergy)
            {
                currentEnergy = maxEnergy;
            }
        }

        internal void ConsumeEnergy(float amount)
        {
            if (amount <= 0f)
            {
                return;
            }

            currentEnergy = Math.Max(0f, currentEnergy - amount);
        }

        internal void SetStability(float current, float max)
        {
            currentStability = Math.Max(0f, current);
            maxStability = Math.Max(0f, max);
            if (maxStability > 0f && currentStability > maxStability)
            {
                currentStability = maxStability;
            }
        }

        internal void ConsumeStability(float amount)
        {
            if (amount <= 0f)
            {
                return;
            }

            currentStability = Math.Max(0f, currentStability - amount);
        }

        internal void UpdateRecoveryLocation(Map? map, IntVec3 position, Rot4 rotation)
        {
            if (map == null)
            {
                return;
            }

            pendingMap = map;
            pendingPosition = position.IsValid ? position : IntVec3.Invalid;
            pendingRotation = rotation.IsValid ? rotation : Rot4.South;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref sessionId, "sessionId");
            Scribe_References.Look(ref sourcePawn, "sourcePawn");
            Scribe_References.Look(ref wearerPawn, "wearerPawn");
            Scribe_References.Look(ref fusionApparel, "fusionApparel");
            Scribe_Defs.Look(ref sourceThingDef, "sourceThingDef");
            Scribe_Values.Look(
                ref state,
                "state",
                MechFusionSessionState.Starting);
            Scribe_Values.Look(
                ref exitReason,
                "exitReason",
                MechFusionExitReason.LoadRepair);
            Scribe_Values.Look(ref currentEnergy, "currentEnergy");
            Scribe_Values.Look(ref maxEnergy, "maxEnergy");
            Scribe_Values.Look(ref currentStability, "currentStability");
            Scribe_Values.Look(ref maxStability, "maxStability");
            Scribe_Values.Look(ref startTick, "startTick");
            Scribe_Values.Look(ref teardownDeferred, "teardownDeferred");
            Scribe_References.Look(ref pendingMap, "pendingMap");
            Scribe_Values.Look(
                ref pendingPosition,
                "pendingPosition",
                IntVec3.Invalid);
            Scribe_Values.Look(
                ref pendingRotation,
                "pendingRotation",
                Rot4.South);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureInitialized();
            }
        }
    }
}
