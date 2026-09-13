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
        private List<MechFusionStatEntry> statOffsets =
            new List<MechFusionStatEntry>();
        private List<MechFusionStatEntry> statFactors =
            new List<MechFusionStatEntry>();
        private List<MechFusionWhitelistEntry> whitelistEntries =
            new List<MechFusionWhitelistEntry>();
        private float armorSharp;
        private float armorBlunt;
        private float armorHeat;
        private float moveSpeedBase;
        private bool temporaryFlightAuthorized;
        private bool flightAuthorizationGrantedByFusion;
        private int energyTickAccumulator;
        private Dictionary<StatDef, float>? offsetLookup;
        private Dictionary<StatDef, float>? factorLookup;

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

        public float ArmorSharp => armorSharp;

        public float ArmorBlunt => armorBlunt;

        public float ArmorHeat => armorHeat;

        public float MoveSpeedBase => moveSpeedBase;

        public bool TemporaryFlightAuthorized => temporaryFlightAuthorized;

        public bool FlightAuthorizationGrantedByFusion =>
            flightAuthorizationGrantedByFusion;

        internal int EnergyTickAccumulator
        {
            get => energyTickAccumulator;
            set => energyTickAccumulator = Math.Max(0, value);
        }

        public IReadOnlyList<MechFusionWhitelistEntry> WhitelistEntries
        {
            get
            {
                whitelistEntries ??= new List<MechFusionWhitelistEntry>();
                return whitelistEntries;
            }
        }

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

        internal void SetSnapshot(
            List<MechFusionStatEntry> offsets,
            List<MechFusionStatEntry> factors,
            float sharp,
            float blunt,
            float heat,
            float speedBase)
        {
            statOffsets = offsets ?? new List<MechFusionStatEntry>();
            statFactors = factors ?? new List<MechFusionStatEntry>();
            armorSharp = sharp;
            armorBlunt = blunt;
            armorHeat = heat;
            moveSpeedBase = speedBase;
            offsetLookup = null;
            factorLookup = null;
        }

        internal bool TryGetStatOffset(StatDef? stat, out float value)
        {
            value = 0f;
            if (stat == null)
            {
                return false;
            }

            EnsureLookups();
            return offsetLookup!.TryGetValue(stat, out value);
        }

        internal bool TryGetStatFactor(StatDef? stat, out float value)
        {
            value = 1f;
            if (stat == null)
            {
                return false;
            }

            EnsureLookups();
            return factorLookup!.TryGetValue(stat, out value);
        }

        internal void AddWhitelistEntry(MechFusionWhitelistEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            whitelistEntries ??= new List<MechFusionWhitelistEntry>();
            whitelistEntries.Add(entry);
        }

        internal void SetTemporaryFlightState(
            bool authorized,
            bool grantedByFusion)
        {
            temporaryFlightAuthorized = authorized;
            flightAuthorizationGrantedByFusion = grantedByFusion;
        }

        internal void CollectAffectedStats(HashSet<StatDef> result)
        {
            if (result == null)
            {
                return;
            }

            statOffsets ??= new List<MechFusionStatEntry>();
            statFactors ??= new List<MechFusionStatEntry>();
            for (int i = 0; i < statOffsets.Count; i++)
            {
                if (statOffsets[i]?.stat != null)
                {
                    result.Add(statOffsets[i].stat!);
                }
            }

            for (int i = 0; i < statFactors.Count; i++)
            {
                if (statFactors[i]?.stat != null)
                {
                    result.Add(statFactors[i].stat!);
                }
            }
        }

        private void EnsureLookups()
        {
            if (offsetLookup != null && factorLookup != null)
            {
                return;
            }

            offsetLookup = new Dictionary<StatDef, float>();
            factorLookup = new Dictionary<StatDef, float>();
            statOffsets ??= new List<MechFusionStatEntry>();
            statFactors ??= new List<MechFusionStatEntry>();

            for (int i = 0; i < statOffsets.Count; i++)
            {
                StatDef? stat = statOffsets[i]?.stat;
                if (stat == null)
                {
                    continue;
                }

                float sum = offsetLookup.TryGetValue(stat, out float existing)
                    ? existing
                    : 0f;
                offsetLookup[stat] = sum + statOffsets[i].value;
            }

            for (int i = 0; i < statFactors.Count; i++)
            {
                StatDef? stat = statFactors[i]?.stat;
                if (stat == null)
                {
                    continue;
                }

                float product = factorLookup.TryGetValue(stat, out float existing)
                    ? existing
                    : 1f;
                factorLookup[stat] = product * statFactors[i].value;
            }
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
            Scribe_Collections.Look(
                ref statOffsets,
                "statOffsets",
                LookMode.Deep);
            Scribe_Collections.Look(
                ref statFactors,
                "statFactors",
                LookMode.Deep);
            Scribe_Collections.Look(
                ref whitelistEntries,
                "whitelistEntries",
                LookMode.Deep);
            Scribe_Values.Look(ref armorSharp, "armorSharp");
            Scribe_Values.Look(ref armorBlunt, "armorBlunt");
            Scribe_Values.Look(ref armorHeat, "armorHeat");
            Scribe_Values.Look(ref moveSpeedBase, "moveSpeedBase");
            Scribe_Values.Look(
                ref temporaryFlightAuthorized,
                "temporaryFlightAuthorized");
            Scribe_Values.Look(
                ref flightAuthorizationGrantedByFusion,
                "flightAuthorizationGrantedByFusion");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureInitialized();
                statOffsets ??= new List<MechFusionStatEntry>();
                statFactors ??= new List<MechFusionStatEntry>();
                whitelistEntries ??= new List<MechFusionWhitelistEntry>();
                offsetLookup = null;
                factorLookup = null;
            }
        }
    }
}
