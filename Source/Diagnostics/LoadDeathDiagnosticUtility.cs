using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 加载期死亡诊断统一工具。所有快照均为被动读取：
    /// 禁止调用 PawnCapacity GetLevel/CapableOf、Hediff.CurStage/CurStageIndex，
    /// 避免诊断日志提前初始化动态缓存并改变旧存档加载时序。
    /// </summary>
    internal static class LoadDeathDiagnosticUtility
    {
        internal const string LogPrefix = "[MAP-机械族机械师] 加载期死亡诊断：";

        private static readonly object Sync = new object();
        private static readonly HashSet<string> WatchedPawnIds = new HashSet<string>();
        private static readonly HashSet<string> FirstDeathLoggedPawnIds = new HashSet<string>();
        private static int sequence;
        private static bool loadSessionActive;

        private static readonly FieldInfo? HealthPawnField =
            AccessTools.Field(typeof(Pawn_HealthTracker), "pawn");
        private static readonly FieldInfo? CapacityCacheField =
            AccessTools.Field(typeof(PawnCapacitiesHandler), "cachedCapacityLevels");

        private static readonly FieldInfo? AllocationRecordsField =
            AccessTools.Field(typeof(GameComponent_DataProcessingAllocationRegistry), "records");
        private static readonly FieldInfo? DynamicAllocationRecordsField =
            AccessTools.Field(typeof(GameComponent_DataProcessingAllocationRegistry), "dynamicAllocationRecords");
        private static readonly FieldInfo? DynamicTargetRecordsField =
            AccessTools.Field(typeof(GameComponent_DataProcessingAllocationRegistry), "dynamicTargetRecords");
        private static readonly FieldInfo? SpecializationRecordsField =
            AccessTools.Field(typeof(GameComponent_DataProcessingAllocationRegistry), "specializationRecords");
        private static readonly FieldInfo? PendingPostLoadReconciliationField =
            AccessTools.Field(typeof(GameComponent_DataProcessingAllocationRegistry), "pendingPostLoadDynamicReconciliation");
        private static readonly FieldInfo? PostLoadReconciliationTickField =
            AccessTools.Field(typeof(GameComponent_DataProcessingAllocationRegistry), "postLoadReconciliationEarliestTick");

        private static readonly FieldInfo? HediffSeverityField =
            AccessTools.Field(typeof(Hediff), "severityInt");

        private static readonly FieldInfo? DataCachedStepsField =
            AccessTools.Field(typeof(Hediff_DataProcessingAllocationBase), "cachedSteps");
        private static readonly FieldInfo? DataCachedVariantField =
            AccessTools.Field(typeof(Hediff_DataProcessingAllocationBase), "cachedVariantKey");
        private static readonly FieldInfo? DataCachedStageField =
            AccessTools.Field(typeof(Hediff_DataProcessingAllocationBase), "cachedStage");

        private static readonly FieldInfo? DynamicCacheInitializedField =
            AccessTools.Field(typeof(Hediff_DynamicConsciousnessBonusBase), "cacheInitialized");
        private static readonly FieldInfo? DynamicCachedOffsetField =
            AccessTools.Field(typeof(Hediff_DynamicConsciousnessBonusBase), "cachedOffset");
        private static readonly FieldInfo? DynamicCachedVariantField =
            AccessTools.Field(typeof(Hediff_DynamicConsciousnessBonusBase), "cachedVariantKey");
        private static readonly FieldInfo? DynamicCachedStageField =
            AccessTools.Field(typeof(Hediff_DynamicConsciousnessBonusBase), "cachedStage");

        private static readonly FieldInfo? ArrayTargetField =
            AccessTools.Field(typeof(CompParallelThoughtArray), "target");
        private static readonly FieldInfo? ArrayConfiguredBoostField =
            AccessTools.Field(typeof(CompParallelThoughtArray), "configuredBoostPercent");
        private static readonly FieldInfo? ArrayPowerTraderField =
            AccessTools.Field(typeof(CompParallelThoughtArray), "powerTrader");
        private static readonly FieldInfo? ArrayLastEffectiveField =
            AccessTools.Field(typeof(CompParallelThoughtArray), "lastEffectiveBoostPercent");
        private static readonly FieldInfo? ArrayFallbackTickField =
            AccessTools.Field(typeof(CompParallelThoughtArray), "fallbackTickCounter");

        internal static bool Enabled
        {
            get
            {
                try
                {
                    return MAPMechanitorMod.Settings?.enableLoadDeathDiagnosticLogging == true;
                }
                catch
                {
                    return false;
                }
            }
        }

        internal static bool LoadSessionActive
        {
            get
            {
                lock (Sync)
                {
                    return loadSessionActive;
                }
            }
        }

        internal static bool Active => Enabled && LoadSessionActive;

        internal static void BeginLoadSession(string reason)
        {
            try
            {
                bool enabled = Enabled;
                lock (Sync)
                {
                    WatchedPawnIds.Clear();
                    FirstDeathLoggedPawnIds.Clear();
                    sequence = 0;
                    loadSessionActive = enabled;
                }

                if (enabled)
                {
                    WriteCore("SESSION.BEGIN", null, "REASON=" + SafeText(reason), null);
                }
            }
            catch
            {
                // 诊断不得影响加载。
            }
        }

        internal static void EndLoadSession(string reason, Exception? exception)
        {
            try
            {
                if (Enabled && LoadSessionActive)
                {
                    WriteCore(
                        "SESSION.END",
                        null,
                        "REASON=" + SafeText(reason)
                        + " EXCEPTION=" + (exception?.GetType().FullName ?? "null"),
                        null);
                }
            }
            catch
            {
                // 忽略。
            }
            finally
            {
                lock (Sync)
                {
                    loadSessionActive = false;
                    WatchedPawnIds.Clear();
                    FirstDeathLoggedPawnIds.Clear();
                    sequence = 0;
                }
            }
        }

        internal static Pawn? GetPawnFromHealthTracker(Pawn_HealthTracker? tracker)
        {
            if (tracker == null)
            {
                return null;
            }

            try
            {
                return HealthPawnField?.GetValue(tracker) as Pawn;
            }
            catch
            {
                return null;
            }
        }

        internal static bool ShouldTracePawn(Pawn? pawn)
        {
            if (!Active || pawn == null)
            {
                return false;
            }

            string id = SafeThingId(pawn);
            lock (Sync)
            {
                if (WatchedPawnIds.Contains(id))
                {
                    return true;
                }
            }

            bool trace = false;
            try
            {
                string name = SafePawnName(pawn);
                trace = name.Contains("正义") || name.Contains("刃");
            }
            catch
            {
                // 忽略。
            }

            if (!trace)
            {
                try
                {
                    trace = pawn.TryGetComp<CompNativeMechanoidMechanitor>() != null;
                }
                catch
                {
                    // 忽略。
                }
            }

            if (!trace)
            {
                try
                {
                    List<Hediff>? hediffs = pawn.health?.hediffSet?.hediffs;
                    if (hediffs != null)
                    {
                        for (int i = 0; i < hediffs.Count; i++)
                        {
                            Hediff? hediff = hediffs[i];
                            if (hediff is Hediff_DataProcessingAllocationBase
                                || hediff is Hediff_DynamicConsciousnessBonusBase)
                            {
                                trace = true;
                                break;
                            }

                            string? defName = hediff?.def?.defName;
                            if (defName == "MAP_NativeMechanoidMechanitor"
                                || defName == "MAP_AcquiredMechanoidMechanitor"
                                || defName == "MAP_MechanicalConsciousness")
                            {
                                trace = true;
                                break;
                            }
                        }
                    }
                }
                catch
                {
                    // 忽略。
                }
            }

            if (!trace)
            {
                try
                {
                    IReadOnlyList<MechanoidMechanitorRegistrySnapshotEntry> entries =
                        GameComponent_MechanoidMechanitorRegistry.GetPersistentRecordSnapshot();
                    for (int i = 0; i < entries.Count; i++)
                    {
                        if (ReferenceEquals(entries[i].Pawn, pawn)
                            || SafeThingId(entries[i].Pawn) == id)
                        {
                            trace = true;
                            break;
                        }
                    }
                }
                catch
                {
                    // 忽略。
                }
            }

            if (!trace)
            {
                trace = IsPawnReferencedByRawDataProcessingRecords(pawn);
            }

            if (trace)
            {
                lock (Sync)
                {
                    WatchedPawnIds.Add(id);
                }
            }

            return trace;
        }

        internal static void Write(
            string eventName,
            Pawn? pawn,
            string details,
            bool includeStack = false)
        {
            if (!Active)
            {
                return;
            }

            try
            {
                string? stack = includeStack
                    ? new StackTrace(2, true).ToString()
                    : null;
                WriteCore(eventName, pawn, details, stack);
            }
            catch
            {
                // 日志不得影响加载。
            }
        }

        internal static void WriteWithStack(
            string eventName,
            Pawn? pawn,
            string details,
            string? stack)
        {
            if (!Active)
            {
                return;
            }

            try
            {
                WriteCore(eventName, pawn, details, stack);
            }
            catch
            {
                // 忽略。
            }
        }

        private static void WriteCore(
            string eventName,
            Pawn? pawn,
            string details,
            string? stack)
        {
            int localSequence;
            lock (Sync)
            {
                localSequence = ++sequence;
            }

            StringBuilder builder = new StringBuilder();
            builder.Append(LogPrefix)
                .Append(" #").Append(localSequence.ToString("D6"))
                .Append(" EVENT=").Append(SafeText(eventName))
                .Append(" PAWN=").Append(SafeThingId(pawn))
                .Append(" NAME=").Append(SafePawnName(pawn))
                .Append(" DEF=").Append(SafeDefName(pawn))
                .Append(" SCRIBE=").Append(Safe(() => Scribe.mode.ToString()))
                .Append(" PROGRAM=").Append(Safe(() => Current.ProgramState.ToString()))
                .Append(" TICK=").Append(Safe(() => Find.TickManager?.TicksGame.ToString() ?? "N/A"))
                .Append(" DETAILS=").Append(details ?? string.Empty);

            string line = builder.ToString();
            try
            {
                Log.Message(line);
            }
            catch
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine(line);
                }
                catch
                {
                    // 放弃。
                }
            }

            if (!string.IsNullOrEmpty(stack))
            {
                try
                {
                    Log.Message(LogPrefix + " 调用堆栈 " + stack);
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        internal static LoadDeathPawnState CaptureState(
            Pawn? pawn,
            string methodName,
            string? triggeringHediff,
            bool includeStack)
        {
            LoadDeathPawnState state = new LoadDeathPawnState
            {
                methodName = methodName,
                triggeringHediffDefName = triggeringHediff,
                pawnThingId = SafeThingId(pawn),
                deadBefore = SafeDead(pawn),
                passivePawnBefore = BuildPassivePawnSnapshot(pawn),
                hediffsBefore = BuildPassiveHediffList(pawn),
                dataProcessingBefore = BuildDataProcessingSnapshot(pawn),
                mechanitorBefore = BuildMechanitorRecordSnapshot(pawn),
                stackTrace = includeStack ? SafeStackTrace(2) : null
            };

            string status;
            if (TryGetCachedConsciousness(pawn, out float cached, out status))
            {
                state.cachedConsciousnessBefore = cached;
            }

            state.consciousnessCacheStatusBefore = status;
            return state;
        }

        internal static void ReportFirstDeadTransition(
            LoadDeathPawnState? state,
            Pawn? pawn,
            string methodName,
            string? triggeringHediff)
        {
            if (!Active || state == null || state.deadBefore || !SafeDead(pawn))
            {
                return;
            }

            string id = SafeThingId(pawn);
            lock (Sync)
            {
                if (!FirstDeathLoggedPawnIds.Add(id))
                {
                    return;
                }
            }

            float? afterValue = null;
            string afterStatus;
            if (TryGetCachedConsciousness(pawn, out float cached, out afterStatus))
            {
                afterValue = cached;
            }

            string details =
                "*** 首次进入死亡状态 ***"
                + " METHOD=" + SafeText(methodName)
                + " TRIGGER_HEDIFF=" + SafeText(triggeringHediff)
                + " DEAD_BEFORE=" + state.deadBefore
                + " DEAD_AFTER=" + SafeDead(pawn)
                + " CONS_CACHE_BEFORE=" + FormatNullable(state.cachedConsciousnessBefore)
                + " CONS_CACHE_STATUS_BEFORE=" + SafeText(state.consciousnessCacheStatusBefore)
                + " CONS_CACHE_AFTER=" + FormatNullable(afterValue)
                + " CONS_CACHE_STATUS_AFTER=" + SafeText(afterStatus)
                + " BEFORE_PAWN={" + state.passivePawnBefore + "}"
                + " AFTER_PAWN={" + BuildPassivePawnSnapshot(pawn) + "}"
                + " BEFORE_HEDIFFS={" + state.hediffsBefore + "}"
                + " AFTER_HEDIFFS={" + BuildPassiveHediffList(pawn) + "}"
                + " BEFORE_DATAPROC={" + state.dataProcessingBefore + "}"
                + " AFTER_DATAPROC={" + BuildDataProcessingSnapshot(pawn) + "}"
                + " BEFORE_MECHANITOR={" + state.mechanitorBefore + "}"
                + " AFTER_MECHANITOR={" + BuildMechanitorRecordSnapshot(pawn) + "}";

            WriteWithStack(
                "*** 首次进入死亡状态 ***",
                pawn,
                details,
                state.stackTrace ?? SafeStackTrace(2));
        }

        internal static string BuildPassivePawnSnapshot(Pawn? pawn)
        {
            if (pawn == null)
            {
                return "PAWN=null";
            }

            StringBuilder builder = new StringBuilder();
            Append(builder, "THINGID", SafeThingId(pawn));
            Append(builder, "NAME", SafePawnName(pawn));
            Append(builder, "DEF", SafeDefName(pawn));
            Append(builder, "KIND", Safe(() => pawn.kindDef?.defName ?? "null"));
            Append(builder, "DEAD", Safe(() => pawn.Dead.ToString()));
            Append(builder, "DESTROYED", Safe(() => pawn.Destroyed.ToString()));
            Append(builder, "DISCARDED", Safe(() => pawn.Discarded.ToString()));
            Append(builder, "SPAWNED", Safe(() => pawn.Spawned.ToString()));
            Append(builder, "HEALTH_STATE", Safe(() => pawn.health?.State.ToString() ?? "null"));
            Append(builder, "IS_BEING_KILLED", Safe(() => (pawn.health?.isBeingKilled == true).ToString()));
            Append(builder, "MAP", Safe(() => pawn.Map?.uniqueID.ToString() ?? "null"));
            Append(builder, "POSITION", Safe(() => pawn.Position.IsValid ? pawn.Position.ToString() : "Invalid"));

            float? value = null;
            string status;
            if (TryGetCachedConsciousness(pawn, out float cached, out status))
            {
                value = cached;
            }

            Append(builder, "CONS_CACHE_STATUS", status);
            Append(builder, "CONS_CACHE_VALUE", FormatNullable(value));
            Append(
                builder,
                "CONS_CACHE_CAPABLE",
                value.HasValue
                    ? (value.Value > PawnCapacityDefOf.Consciousness.minForCapable).ToString()
                    : "unavailable");
            return builder.ToString();
        }

        internal static string BuildPassiveHediffList(Pawn? pawn)
        {
            try
            {
                List<Hediff>? hediffs = pawn?.health?.hediffSet?.hediffs;
                if (hediffs == null)
                {
                    return "null";
                }

                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < hediffs.Count; i++)
                {
                    Hediff? hediff = hediffs[i];
                    if (hediff == null)
                    {
                        continue;
                    }

                    if (builder.Length > 0)
                    {
                        builder.Append("; ");
                    }

                    builder.Append('{')
                        .Append("TYPE=").Append(hediff.GetType().FullName ?? hediff.GetType().Name)
                        .Append(" DEF=").Append(hediff.def?.defName ?? "null")
                        .Append(" SEVERITY_RAW=").Append(SafeField(HediffSeverityField, hediff))
                        .Append(" PART=").Append(hediff.Part?.def?.defName ?? "null");

                    if (hediff is Hediff_DataProcessingAllocationBase)
                    {
                        builder.Append(" DATA_CACHE=")
                            .Append(BuildDataProcessingHediffCacheSnapshot(hediff));
                    }

                    if (hediff is Hediff_DynamicConsciousnessBonusBase)
                    {
                        builder.Append(" DYNAMIC_CACHE=")
                            .Append(BuildDynamicConsciousnessCacheSnapshot(hediff));
                    }

                    builder.Append('}');
                }

                return builder.ToString();
            }
            catch (Exception ex)
            {
                return "<error:" + ex.GetType().Name + ">";
            }
        }

        internal static string BuildDataProcessingHediffCacheSnapshot(Hediff? hediff)
        {
            if (!(hediff is Hediff_DataProcessingAllocationBase))
            {
                return "not-data-processing";
            }

            return "cachedSteps=" + SafeField(DataCachedStepsField, hediff)
                + ",cachedVariantKey=" + SafeField(DataCachedVariantField, hediff)
                + ",cachedStagePresent=" + SafeFieldPresent(DataCachedStageField, hediff);
        }

        internal static string BuildDynamicConsciousnessCacheSnapshot(Hediff? hediff)
        {
            if (!(hediff is Hediff_DynamicConsciousnessBonusBase))
            {
                return "not-dynamic-consciousness";
            }

            return "cacheInitialized=" + SafeField(DynamicCacheInitializedField, hediff)
                + ",cachedOffset=" + SafeField(DynamicCachedOffsetField, hediff)
                + ",cachedVariantKey=" + SafeField(DynamicCachedVariantField, hediff)
                + ",cachedStagePresent=" + SafeFieldPresent(DynamicCachedStageField, hediff);
        }

        internal static string BuildMechanitorRecordSnapshot(Pawn? pawn)
        {
            if (pawn == null)
            {
                return "PAWN=null";
            }

            try
            {
                IReadOnlyList<MechanoidMechanitorRegistrySnapshotEntry> entries =
                    GameComponent_MechanoidMechanitorRegistry.GetPersistentRecordSnapshot();
                for (int i = 0; i < entries.Count; i++)
                {
                    MechanoidMechanitorRegistrySnapshotEntry entry = entries[i];
                    if (ReferenceEquals(entry.Pawn, pawn)
                        || SafeThingId(entry.Pawn) == SafeThingId(pawn))
                    {
                        return "HAS_PERSISTENT_RECORD=true"
                            + "|ORIGIN=" + entry.Origin
                            + "|IS_CONSCIOUSNESS_HOST=" + entry.IsMechanicalConsciousnessHost
                            + "|RECORD_PAWN=" + SafeThingId(entry.Pawn);
                    }
                }

                return "HAS_PERSISTENT_RECORD=false";
            }
            catch (Exception ex)
            {
                return "<error:" + ex.GetType().Name + ">";
            }
        }

        internal static string BuildDataProcessingSnapshot(Pawn? pawn)
        {
            return BuildRawRegistrySnapshot(
                GameComponent_DataProcessingAllocationRegistry.CurrentRegistry,
                pawn);
        }

        internal static string BuildRawRegistrySnapshot(
            GameComponent_DataProcessingAllocationRegistry? registry,
            Pawn? focus)
        {
            if (registry == null)
            {
                return "REGISTRY=null";
            }

            try
            {
                StringBuilder builder = new StringBuilder("REGISTRY=present");
                AppendRawCollection(builder, "ALLOC", AllocationRecordsField?.GetValue(registry), focus);
                AppendRawCollection(builder, "DYNAMIC_OVERSEER", DynamicAllocationRecordsField?.GetValue(registry), focus);
                AppendRawCollection(builder, "DYNAMIC_TARGET", DynamicTargetRecordsField?.GetValue(registry), focus);
                AppendRawCollection(builder, "SPECIALIZATION", SpecializationRecordsField?.GetValue(registry), focus);
                Append(builder, "PENDING_POST_LOAD_RECONCILIATION", SafeField(PendingPostLoadReconciliationField, registry));
                Append(builder, "POST_LOAD_RECONCILIATION_TICK", SafeField(PostLoadReconciliationTickField, registry));
                return builder.ToString();
            }
            catch (Exception ex)
            {
                return "<error:" + ex.GetType().Name + ">";
            }
        }

        private static void AppendRawCollection(
            StringBuilder builder,
            string label,
            object? collectionObject,
            Pawn? focus)
        {
            builder.Append('|').Append(label).Append("=[");
            if (!(collectionObject is IEnumerable enumerable))
            {
                builder.Append("unavailable]");
                return;
            }

            int total = 0;
            int emitted = 0;
            foreach (object? item in enumerable)
            {
                total++;
                if (item == null)
                {
                    continue;
                }

                Pawn? overseer = ReadPawnMember(item, "overseer");
                Pawn? target = ReadPawnMember(item, "target");
                bool related = focus == null
                    || ReferenceEquals(overseer, focus)
                    || ReferenceEquals(target, focus)
                    || SafeThingId(overseer) == SafeThingId(focus)
                    || SafeThingId(target) == SafeThingId(focus);
                if (!related)
                {
                    continue;
                }

                if (emitted > 0)
                {
                    builder.Append(';');
                }

                emitted++;
                builder.Append('{')
                    .Append("type=").Append(item.GetType().Name)
                    .Append(",overseer=").Append(SafeThingId(overseer))
                    .Append(",target=").Append(SafeThingId(target));
                AppendMemberIfPresent(builder, item, "steps");
                AppendMemberIfPresent(builder, item, "enabled");
                AppendMemberIfPresent(builder, item, "normalSteps");
                AppendMemberIfPresent(builder, item, "commonMaxSteps");
                AppendMemberIfPresent(builder, item, "defaultSpecialization");
                AppendMemberIfPresent(builder, item, "specialization");
                AppendMemberIfPresent(builder, item, "minimumReserveSteps");
                builder.Append('}');

                if (emitted >= 200)
                {
                    builder.Append(";...truncated");
                    break;
                }
            }

            builder.Append("]COUNT=").Append(total)
                .Append(",EMITTED=").Append(emitted);
        }

        internal static string BuildParallelThoughtArraySnapshot(CompParallelThoughtArray? comp)
        {
            if (comp == null)
            {
                return "COMP=null";
            }

            try
            {
                Pawn? target = ArrayTargetField?.GetValue(comp) as Pawn;
                CompPowerTrader? power = ArrayPowerTraderField?.GetValue(comp) as CompPowerTrader;
                StringBuilder builder = new StringBuilder();
                Append(builder, "BUILDING", SafeThingId(comp.parent));
                Append(builder, "SPAWNED", Safe(() => (comp.parent?.Spawned == true).ToString()));
                Append(builder, "MAP", Safe(() => comp.parent?.Map?.uniqueID.ToString() ?? "null"));
                Append(builder, "TARGET", SafeThingId(target));
                Append(builder, "CONFIGURED_BOOST", SafeField(ArrayConfiguredBoostField, comp));
                Append(builder, "LAST_EFFECTIVE_BOOST", SafeField(ArrayLastEffectiveField, comp));
                Append(builder, "FALLBACK_TICK", SafeField(ArrayFallbackTickField, comp));
                Append(builder, "POWER_TRADER_PRESENT", (power != null).ToString());
                Append(builder, "POWER_ON", Safe(() => (power?.PowerOn == true).ToString()));
                Append(builder, "POWER_OUTPUT", Safe(() => power?.PowerOutput.ToString("F1") ?? "null"));
                Append(builder, "FACTION", Safe(() => comp.parent?.Faction?.def?.defName ?? "null"));
                return builder.ToString();
            }
            catch (Exception ex)
            {
                return "<error:" + ex.GetType().Name + ">";
            }
        }

        internal static bool TryGetCachedConsciousness(
            Pawn? pawn,
            out float value,
            out string status)
        {
            value = 0f;
            status = "unavailable";
            try
            {
                PawnCapacitiesHandler? capacities = pawn?.health?.capacities;
                if (capacities == null)
                {
                    status = "capacities-null";
                    return false;
                }

                object? cacheMap = CapacityCacheField?.GetValue(capacities);
                if (cacheMap == null)
                {
                    status = "cache-map-null";
                    return false;
                }

                PropertyInfo? indexer = cacheMap.GetType().GetProperty(
                    "Item",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (indexer == null)
                {
                    status = "indexer-unavailable";
                    return false;
                }

                object? element = indexer.GetValue(
                    cacheMap,
                    new object[] { PawnCapacityDefOf.Consciousness });
                if (element == null)
                {
                    status = "cache-element-null";
                    return false;
                }

                FieldInfo? statusField = AccessTools.Field(element.GetType(), "status");
                FieldInfo? valueField = AccessTools.Field(element.GetType(), "value");
                status = statusField?.GetValue(element)?.ToString() ?? "status-null";
                if (!string.Equals(status, "Cached", StringComparison.Ordinal))
                {
                    return false;
                }

                if (valueField?.GetValue(element) is float cached)
                {
                    value = cached;
                    return true;
                }

                status = "cached-value-unavailable";
                return false;
            }
            catch (Exception ex)
            {
                status = "error:" + ex.GetType().Name;
                return false;
            }
        }

        internal static string SafeStackTrace(int skipFrames)
        {
            try
            {
                return new StackTrace(skipFrames, true).ToString();
            }
            catch
            {
                return "<stack-unavailable>";
            }
        }

        internal static string SafeThingId(Thing? thing)
        {
            try
            {
                return thing?.ThingID ?? "null";
            }
            catch
            {
                return "<error>";
            }
        }

        internal static string SafePawnName(Pawn? pawn)
        {
            try
            {
                if (pawn == null)
                {
                    return "null";
                }

                return pawn.Name?.ToStringFull ?? pawn.LabelShort ?? "null";
            }
            catch
            {
                return "<error>";
            }
        }

        internal static string SafeDefName(Pawn? pawn)
        {
            return Safe(() => pawn?.def?.defName ?? "null");
        }

        internal static bool SafeDead(Pawn? pawn)
        {
            try
            {
                return pawn?.Dead == true;
            }
            catch
            {
                return false;
            }
        }

        internal static string FormatNullable(float? value)
        {
            return value.HasValue ? value.Value.ToString("F4") : "unavailable";
        }

        private static bool IsPawnReferencedByRawDataProcessingRecords(Pawn pawn)
        {
            try
            {
                GameComponent_DataProcessingAllocationRegistry? registry =
                    GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;
                if (registry == null)
                {
                    return false;
                }

                return CollectionReferencesPawn(AllocationRecordsField?.GetValue(registry), pawn)
                    || CollectionReferencesPawn(DynamicAllocationRecordsField?.GetValue(registry), pawn)
                    || CollectionReferencesPawn(DynamicTargetRecordsField?.GetValue(registry), pawn)
                    || CollectionReferencesPawn(SpecializationRecordsField?.GetValue(registry), pawn);
            }
            catch
            {
                return false;
            }
        }

        private static bool CollectionReferencesPawn(object? collectionObject, Pawn pawn)
        {
            if (!(collectionObject is IEnumerable enumerable))
            {
                return false;
            }

            foreach (object? item in enumerable)
            {
                if (item == null)
                {
                    continue;
                }

                Pawn? overseer = ReadPawnMember(item, "overseer");
                Pawn? target = ReadPawnMember(item, "target");
                if (ReferenceEquals(overseer, pawn)
                    || ReferenceEquals(target, pawn)
                    || SafeThingId(overseer) == SafeThingId(pawn)
                    || SafeThingId(target) == SafeThingId(pawn))
                {
                    return true;
                }
            }

            return false;
        }

        private static Pawn? ReadPawnMember(object item, string memberName)
        {
            try
            {
                FieldInfo? field = AccessTools.Field(item.GetType(), memberName);
                if (field?.GetValue(item) is Pawn fieldPawn)
                {
                    return fieldPawn;
                }

                PropertyInfo? property = AccessTools.Property(item.GetType(), memberName);
                return property?.GetValue(item, null) as Pawn;
            }
            catch
            {
                return null;
            }
        }

        private static void AppendMemberIfPresent(StringBuilder builder, object item, string memberName)
        {
            try
            {
                FieldInfo? field = AccessTools.Field(item.GetType(), memberName);
                PropertyInfo? property = AccessTools.Property(item.GetType(), memberName);
                object? value = field != null
                    ? field.GetValue(item)
                    : property?.GetValue(item, null);
                if (field != null || property != null)
                {
                    builder.Append(',').Append(memberName).Append('=')
                        .Append(value?.ToString() ?? "null");
                }
            }
            catch
            {
                builder.Append(',').Append(memberName).Append("=<error>");
            }
        }

        private static string SafeField(FieldInfo? field, object? instance)
        {
            try
            {
                return field?.GetValue(instance)?.ToString() ?? "null";
            }
            catch (Exception ex)
            {
                return "<error:" + ex.GetType().Name + ">";
            }
        }

        private static string SafeFieldPresent(FieldInfo? field, object? instance)
        {
            try
            {
                return (field?.GetValue(instance) != null).ToString();
            }
            catch (Exception ex)
            {
                return "<error:" + ex.GetType().Name + ">";
            }
        }

        private static void Append(StringBuilder builder, string key, string value)
        {
            if (builder.Length > 0)
            {
                builder.Append('|');
            }

            builder.Append(key).Append('=').Append(value);
        }

        private static string Safe(Func<string> getter)
        {
            try
            {
                return getter();
            }
            catch (Exception ex)
            {
                return "<error:" + ex.GetType().Name + ">";
            }
        }

        private static string SafeText(string? value)
        {
            return value ?? "null";
        }
    }

    internal sealed class LoadDeathPawnState
    {
        public string? methodName;
        public string? triggeringHediffDefName;
        public string? pawnThingId;
        public bool deadBefore;
        public float? cachedConsciousnessBefore;
        public string? consciousnessCacheStatusBefore;
        public string? passivePawnBefore;
        public string? hediffsBefore;
        public string? dataProcessingBefore;
        public string? mechanitorBefore;
        public string? stackTrace;
    }
}
