using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 本次合体实例的唯一权威数据。唯一保存源 Pawn、目标人类、合体服装实例、
    /// 能源、结构稳定值、属性快照、健康状态规则结果与异常恢复信息；服装 Comp、
    /// Gizmo、飞行与渲染系统只能读取这里。
    /// </summary>
    public sealed class MechFusionSession : IExposable
    {
        private string? sessionId;
        private Pawn? sourcePawn;
        private Pawn? wearerPawn;
        private Thing? fusionApparel;
        private ThingDef? sourceThingDef;
        private Faction? originalSourceFaction;
        private Pawn? originalOverseer;
        private int originalControlGroupIndex = -1;
        private bool originalSourceStateCaptured;
        private MechFusionSessionState state;
        private MechFusionExitReason exitReason;
        private float currentEnergy;
        private float maxEnergy;
        private float currentStability;
        private float maxStability;
        private bool repairBeaconCaptured;
        private bool repairBeaconAuthorized;
        private bool voidEngineCaptured;
        private bool voidEngineAuthorized;
        private bool productivityCoreCaptured;
        private int productivityCoreLevel;
        private float productivityCoreWorkSpeedOffset;
        internal float PendingStabilityRepair;
        internal int StabilityRepairTicks;
        private int startTick;
        private bool teardownDeferred;
        private Map? pendingMap;
        private IntVec3 pendingPosition = IntVec3.Invalid;
        private Rot4 pendingRotation = Rot4.South;
        private List<MechFusionStatEntry> statOffsets =
            new List<MechFusionStatEntry>();
        private List<MechFusionStatEntry> statFactors =
            new List<MechFusionStatEntry>();
        private List<MechFusionHealthEffectEntry> healthEffectEntries =
            new List<MechFusionHealthEffectEntry>();
        private MechFusionMechanitorSnapshot? mechanitorSnapshot;
        private float armorSharp;
        private float armorBlunt;
        private float armorHeat;
        private float moveSpeedBase;
        private bool temporaryFlightAuthorized;
        private bool flightAuthorizationGrantedByFusion;
        private int energyTickAccumulator;
        private bool teardownCompleted;
        private bool flightRevoked;
        private bool whitelistRevoked;
        private bool synchronizationRemoved;
        private bool energyWrittenBack;
        private bool stabilitySettled;
        private bool sourceRestored;
        private bool transformationRestored;
        private bool apparelRemoved;
        private ThingWithComps? originalWearerWeapon;
        private ThingWithComps? sourceWeapon;
        private bool weaponsRestored;
        private int pendingRecoveryAttempts;
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

        public Faction? OriginalSourceFaction => originalSourceFaction;

        public Pawn? OriginalOverseer => originalOverseer;

        public int OriginalControlGroupIndex => originalControlGroupIndex;

        public MechFusionSessionState State => state;

        public MechFusionExitReason ExitReason => exitReason;

        public float CurrentEnergy => currentEnergy;

        public float MaxEnergy => maxEnergy;

        public float CurrentStability => currentStability;

        public float MaxStability => maxStability;

        internal bool RepairBeaconCaptured => repairBeaconCaptured;
        internal bool RepairBeaconAuthorized => repairBeaconAuthorized;

        internal bool VoidEngineCaptured => voidEngineCaptured;
        internal bool VoidEngineAuthorized => voidEngineAuthorized;

        internal bool ProductivityCoreCaptured => productivityCoreCaptured;
        internal int ProductivityCoreLevel => productivityCoreLevel;
        internal float ProductivityCoreWorkSpeedOffset => productivityCoreWorkSpeedOffset;

        internal void CaptureProductivityCore(int level, float workSpeedOffset)
        {
            if (productivityCoreCaptured) return;
            productivityCoreLevel = level;
            productivityCoreWorkSpeedOffset = workSpeedOffset;
            productivityCoreCaptured = true;
        }

        internal void CaptureVoidEngine(bool authorized)
        {
            if (voidEngineCaptured) return;
            voidEngineAuthorized = authorized;
            voidEngineCaptured = true;
        }

        internal void CaptureRepairBeacon(bool authorized)
        {
            if (repairBeaconCaptured) return;
            repairBeaconAuthorized = authorized;
            repairBeaconCaptured = true;
        }

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

        public IReadOnlyList<MechFusionStatEntry> StatOffsets
        {
            get
            {
                statOffsets ??= new List<MechFusionStatEntry>();
                return statOffsets;
            }
        }

        public IReadOnlyList<MechFusionStatEntry> StatFactors
        {
            get
            {
                statFactors ??= new List<MechFusionStatEntry>();
                return statFactors;
            }
        }

        public IReadOnlyList<MechFusionHealthEffectEntry> HealthEffectEntries
        {
            get
            {
                healthEffectEntries ??=
                    new List<MechFusionHealthEffectEntry>();
                return healthEffectEntries;
            }
        }

        public MechFusionMechanitorSnapshot? MechanitorSnapshot =>
            mechanitorSnapshot;

        public bool IsActive =>
            state == MechFusionSessionState.Starting
            || state == MechFusionSessionState.Active;

        public bool IsEnding => state == MechFusionSessionState.Ending;

        public bool IsPendingRecovery =>
            state == MechFusionSessionState.PendingRecovery;

        public bool TeardownCompleted => teardownCompleted;

        public bool FlightRevoked => flightRevoked;

        // 两个旧字段共同作为新健康状态管理层的迁移完成标记。
        public bool HealthEffectsRevoked =>
            whitelistRevoked && synchronizationRemoved;

        public bool EnergyWrittenBack => energyWrittenBack;

        public bool StabilitySettled => stabilitySettled;

        public bool SourceRestored => sourceRestored;

        public bool TransformationRestored => transformationRestored;

        public bool ApparelRemoved => apparelRemoved;

        internal ThingWithComps? OriginalWearerWeapon => originalWearerWeapon;

        internal ThingWithComps? SourceWeapon => sourceWeapon;

        internal bool WeaponsRestored => weaponsRestored;

        public int PendingRecoveryAttempts => pendingRecoveryAttempts;

        internal void MarkTeardownCompleted()
        {
            teardownCompleted = true;
        }

        internal void MarkFlightRevoked()
        {
            flightRevoked = true;
        }

        internal void MarkHealthEffectsRevoked()
        {
            whitelistRevoked = true;
            synchronizationRemoved = true;
        }

        internal void MarkEnergyWrittenBack()
        {
            energyWrittenBack = true;
        }

        internal void MarkStabilitySettled()
        {
            stabilitySettled = true;
        }

        internal void MarkSourceRestored()
        {
            sourceRestored = true;
        }

        internal void MarkTransformationRestored()
        {
            transformationRestored = true;
        }

        internal void MarkApparelRemoved()
        {
            apparelRemoved = true;
        }

        internal void IncrementPendingRecoveryAttempts()
        {
            if (pendingRecoveryAttempts < int.MaxValue)
            {
                pendingRecoveryAttempts++;
            }
        }

        internal void ResetPendingRecoveryAttempts()
        {
            pendingRecoveryAttempts = 0;
        }

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
            CaptureOriginalSourceState(source);
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

        private void CaptureOriginalSourceState(Pawn source)
        {
            originalSourceFaction = source.Faction;
            originalOverseer = source.GetOverseer()
                ?? MAPOverseerRelationDirectionUtility.FindActualOverseer(source);
            originalControlGroupIndex =
                MechControlGroupPositionUtility.Capture(
                    originalOverseer,
                    source);
            originalSourceStateCaptured = true;
        }

        /// <summary>
        /// 旧存档中的活动会话可能没有新增的身份快照。由于旧版开始合体时只允许
        /// 玩家安全阵营来源，若源 Pawn 已异常敌对，则以玩家阵营作为安全迁移值。
        /// 新创建会话始终使用合体开始前的精确快照。
        /// </summary>
        internal void EnsureOriginalSourceStateForRecovery()
        {
            if (originalSourceStateCaptured)
            {
                return;
            }

            originalSourceFaction = sourcePawn?.Faction;
            originalOverseer = sourcePawn?.GetOverseer()
                ?? MAPOverseerRelationDirectionUtility.FindActualOverseer(
                    sourcePawn);
            originalControlGroupIndex =
                MechControlGroupPositionUtility.Capture(
                    originalOverseer,
                    sourcePawn);
            if (originalOverseer == null
                && sourcePawn != null
                && wearerPawn != null
                && !AutonomousMechUtility.IsAutonomousMech(sourcePawn)
                && !MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(
                    sourcePawn))
            {
                // 旧版合体统一要求 wearer 就是监管者；若失控流程已删除关系，
                // 仍可由会话两端引用恢复原方向。
                originalOverseer = wearerPawn;
                originalControlGroupIndex =
                    MechControlGroupPositionUtility.Capture(
                        originalOverseer,
                        sourcePawn);
            }

            Faction? player = Faction.OfPlayerSilentFail;
            if (player != null
                && wearerPawn?.Faction?.IsPlayerSafe() == true
                && (originalSourceFaction == null
                    || originalSourceFaction.HostileTo(player)))
            {
                originalSourceFaction = player;
            }

            originalSourceStateCaptured = true;
        }

        internal void SetState(MechFusionSessionState newState)
        {
            state = newState;
        }

        internal void SetExitReason(MechFusionExitReason reason)
        {
            if (state != MechFusionSessionState.Ending
                && state != MechFusionSessionState.PendingRecovery)
            {
                exitReason = reason;
                return;
            }

            // 数值越小优先级越高；只在更高优先级到达时更新最终退出原因。
            if (reason.GetPriority() < exitReason.GetPriority())
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

            currentEnergy = Math.Max(0f, currentEnergy
                - amount * MechFusionVoidEngineUtility.ConsumptionFactor(this));
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
            if (factorLookup!.TryGetValue(stat, out float storedValue))
            {
                value = storedValue;
                return true;
            }

            value = 1f;
            return false;
        }

        internal bool HasHealthEffectRule(string? ruleId)
        {
            if (string.IsNullOrEmpty(ruleId))
            {
                return false;
            }

            healthEffectEntries ??=
                new List<MechFusionHealthEffectEntry>();
            for (int i = 0; i < healthEffectEntries.Count; i++)
            {
                if (healthEffectEntries[i]?.ruleId == ruleId)
                {
                    return true;
                }
            }

            return false;
        }

        internal void CaptureWeapons(
            ThingWithComps? wearerWeapon,
            ThingWithComps? mechWeapon)
        {
            originalWearerWeapon = wearerWeapon;
            sourceWeapon = mechWeapon;
        }

        internal void MarkWeaponsRestored()
        {
            weaponsRestored = true;
        }

        internal void AddHealthEffectEntry(MechFusionHealthEffectEntry entry)
        {
            if (entry == null || HasHealthEffectRule(entry.ruleId))
            {
                return;
            }

            healthEffectEntries ??=
                new List<MechFusionHealthEffectEntry>();
            healthEffectEntries.Add(entry);
        }

        internal void SetMechanitorSnapshot(
            MechFusionMechanitorSnapshot snapshot)
        {
            mechanitorSnapshot ??= snapshot;
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
            Scribe_References.Look(
                ref originalSourceFaction,
                "originalSourceFaction");
            Scribe_References.Look(ref originalOverseer, "originalOverseer");
            Scribe_Values.Look(
                ref originalControlGroupIndex,
                "originalControlGroupIndex",
                -1);
            Scribe_Values.Look(
                ref originalSourceStateCaptured,
                "originalSourceStateCaptured");
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
            Scribe_Values.Look(ref repairBeaconCaptured, "repairBeaconCaptured");
            Scribe_Values.Look(ref repairBeaconAuthorized, "repairBeaconAuthorized");
            // 旧存档缺少资格时不按读档后的监管关系补发；下一次合体重新捕获。
            Scribe_Values.Look(ref voidEngineCaptured, "voidEngineCaptured");
            Scribe_Values.Look(ref voidEngineAuthorized, "voidEngineAuthorized");
            // 缺少快照的旧会话保持零加成，不在读档时重新捕获。
            Scribe_Values.Look(ref productivityCoreCaptured, "productivityCoreCaptured");
            Scribe_Values.Look(ref productivityCoreLevel, "productivityCoreLevel");
            Scribe_Values.Look(ref productivityCoreWorkSpeedOffset, "productivityCoreWorkSpeedOffset");
            Scribe_Values.Look(ref PendingStabilityRepair, "pendingStabilityRepair");
            Scribe_Values.Look(ref StabilityRepairTicks, "stabilityRepairTicks");
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
                ref healthEffectEntries,
                // 保留旧键名，活动中的旧合体会话可以继续读取。
                "whitelistEntries",
                LookMode.Deep);
            Scribe_Deep.Look(
                ref mechanitorSnapshot,
                "mechanitorSynchronizationSnapshot");
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
            Scribe_Values.Look(
                ref energyTickAccumulator,
                "energyTickAccumulator");
            Scribe_Values.Look(ref flightRevoked, "flightRevoked");
            Scribe_Values.Look(ref whitelistRevoked, "whitelistRevoked");
            Scribe_Values.Look(
                ref synchronizationRemoved,
                "synchronizationRemoved");
            Scribe_Values.Look(ref energyWrittenBack, "energyWrittenBack");
            Scribe_Values.Look(ref stabilitySettled, "stabilitySettled");
            Scribe_Values.Look(ref sourceRestored, "sourceRestored");
            Scribe_Values.Look(
                ref transformationRestored,
                "transformationRestored");
            Scribe_Values.Look(ref apparelRemoved, "apparelRemoved");
            Scribe_References.Look(
                ref originalWearerWeapon,
                "originalWearerWeapon");
            Scribe_References.Look(ref sourceWeapon, "sourceWeapon");
            Scribe_Values.Look(ref weaponsRestored, "weaponsRestored");
            Scribe_Values.Look(
                ref pendingRecoveryAttempts,
                "pendingRecoveryAttempts");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureInitialized();
                statOffsets ??= new List<MechFusionStatEntry>();
                statFactors ??= new List<MechFusionStatEntry>();
                healthEffectEntries ??=
                    new List<MechFusionHealthEffectEntry>();
                offsetLookup = null;
                factorLookup = null;
            }
        }
    }
}
