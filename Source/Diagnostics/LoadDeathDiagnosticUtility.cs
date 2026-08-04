using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 加载期死亡诊断统一工具。
    /// 临时、默认关闭、只记录不修复。所有日志均带前缀 [MAP-LOAD-DEATH-DIAG]。
    /// 任何格式化或快照方法都必须独立 try/catch，异常绝不外抛。
    /// </summary>
    internal static class LoadDeathDiagnosticUtility
    {
        public const string LogPrefix = "[MAP-LOAD-DEATH-DIAG]";

        private static int sequence;

        private static readonly HashSet<string> watchedPawnIds =
            new HashSet<string>();

        private static readonly HashSet<string> firstDeathLoggedPawnIds =
            new HashSet<string>();

        private static readonly object sessionLock = new object();

        // Pawn_HealthTracker 内部持有 Pawn（private readonly Pawn pawn）。
        private static readonly AccessTools.FieldRef<Pawn_HealthTracker, Pawn>? pawnFromHealthTracker =
            TryCreatePawnFromHealthTrackerAccessor();

        private static AccessTools.FieldRef<Pawn_HealthTracker, Pawn>? TryCreatePawnFromHealthTrackerAccessor()
        {
            try
            {
                return AccessTools.FieldRefAccess<Pawn_HealthTracker, Pawn>("pawn");
            }
            catch (Exception ex)
            {
                try
                {
                    WriteSelfError("无法获取 Pawn_HealthTracker.pawn 访问器：" + ex);
                }
                catch
                {
                    // 忽略：工具自身错误不得影响游戏。
                }

                return null;
            }
        }

        /// <summary>
        /// 只读开关。仅在设置开启时允许诊断。
        /// </summary>
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

        /// <summary>
        /// 清空关注集合与序号，开始一次新的诊断会话。不修改任何游戏数据。
        /// </summary>
        internal static void ResetSession(string reason)
        {
            try
            {
                lock (sessionLock)
                {
                    watchedPawnIds.Clear();
                    firstDeathLoggedPawnIds.Clear();
                    sequence = 0;
                }

                WriteCore("SESSION", "LoadDeathDiagnostic", string.Empty,
                    "LoadDeathDiagnostic",
                    "开始新的诊断会话，原因=" + (reason ?? "null") +
                    "。关注集合已清空。", null);
            }
            catch
            {
                // 忽略。
            }
        }

        // ===== 关注对象判定 =====

        /// <summary>
        /// 空值安全与加载中安全的关注对象判定。命中后把 ThingID 加入关注集合。
        /// </summary>
        internal static bool ShouldTracePawn(Pawn? pawn)
        {
            if (!Enabled)
            {
                return false;
            }

            if (pawn == null)
            {
                return false;
            }

            try
            {
                string? thingId = SafeThingId(pawn);
                if (thingId != null && watchedPawnIds.Contains(thingId))
                {
                    return true;
                }

                bool watch = DetermineTrace(pawn);
                if (watch && thingId != null)
                {
                    lock (sessionLock)
                    {
                        watchedPawnIds.Add(thingId);
                    }
                }

                return watch;
            }
            catch
            {
                return false;
            }
        }

        private static bool DetermineTrace(Pawn pawn)
        {
            try
            {
                if (SafePawnName(pawn) == "正义" || SafePawnName(pawn) == "刃")
                {
                    return true;
                }
            }
            catch
            {
                // 忽略。
            }

            try
            {
                if (pawn.TryGetComp<CompNativeMechanoidMechanitor>() != null)
                {
                    return true;
                }
            }
            catch
            {
                // 忽略。
            }

            try
            {
                if (HasModIdentityHediff(pawn))
                {
                    return true;
                }
            }
            catch
            {
                // 忽略。
            }

            try
            {
                if (HasDataProcessingAllocationHediff(pawn))
                {
                    return true;
                }
            }
            catch
            {
                // 忽略。
            }

            try
            {
                if (HasDynamicConsciousnessHediff(pawn))
                {
                    return true;
                }
            }
            catch
            {
                // 忽略。
            }

            try
            {
                if (GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(
                        pawn, out _))
                {
                    return true;
                }
            }
            catch
            {
                // 忽略。
            }

            try
            {
                if (IsOverseerOrTargetInRegistry(pawn))
                {
                    return true;
                }
            }
            catch
            {
                // 忽略。
            }

            return false;
        }

        private static bool HasModIdentityHediff(Pawn pawn)
        {
            HediffSet? set = pawn.health?.hediffSet;
            if (set?.hediffs == null)
            {
                return false;
            }

            foreach (Hediff hediff in set.hediffs)
            {
                if (hediff == null)
                {
                    continue;
                }

                string? defName = hediff.def?.defName;
                if (defName == "MAP_NativeMechanoidMechanitor"
                    || defName == "MAP_AcquiredMechanoidMechanitor"
                    || defName == "MAP_MechanicalConsciousness")
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasDataProcessingAllocationHediff(Pawn pawn)
        {
            HediffSet? set = pawn.health?.hediffSet;
            if (set?.hediffs == null)
            {
                return false;
            }

            foreach (Hediff hediff in set.hediffs)
            {
                if (hediff == null)
                {
                    continue;
                }

                if (hediff is Hediff_DataProcessingAllocationBase)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasDynamicConsciousnessHediff(Pawn pawn)
        {
            HediffSet? set = pawn.health?.hediffSet;
            if (set?.hediffs == null)
            {
                return false;
            }

            foreach (Hediff hediff in set.hediffs)
            {
                if (hediff == null)
                {
                    continue;
                }

                if (hediff is Hediff_DynamicConsciousnessBonusBase)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsOverseerOrTargetInRegistry(Pawn pawn)
        {
            GameComponent_DataProcessingAllocationRegistry? registry =
                GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;
            if (registry == null)
            {
                return false;
            }

            // 机械体才检查监管者/目标关系（任务范围）。
            if (pawn.RaceProps == null || !pawn.RaceProps.IsMechanoid)
            {
                return false;
            }

            // 优先使用公开查询 API。
            try
            {
                if (registry.GetStepsForTarget(pawn) > 0
                    || registry.GetTotalStepsForOverseer(pawn) > 0)
                {
                    return true;
                }
            }
            catch
            {
                // 忽略。
            }

            return false;
        }

        // ===== 安全日志 =====

        /// <summary>
        /// 通用诊断日志。绝不由本方法向外抛异常。
        /// </summary>
        internal static void Write(
            string eventName,
            Pawn? pawn,
            string details,
            bool includeStack = false)
        {
            try
            {
                if (!Enabled)
                {
                    return;
                }

                string stack = string.Empty;
                if (includeStack)
                {
                    try
                    {
                        stack = new StackTrace(2, true).ToString();
                    }
                    catch
                    {
                        stack = "<stack-unavailable>";
                    }
                }

                WriteCore(eventName, SafeThingId(pawn), SafeDefName(pawn), SafePawnName(pawn),
                    details, includeStack ? stack : null);
            }
            catch
            {
                // 忽略：日志失败不得影响游戏。
            }
        }

        private static void WriteCore(
            string eventName,
            string thingId,
            string defName,
            string name,
            string details,
            string? stack)
        {
            int localSeq;
            lock (sessionLock)
            {
                localSeq = ++sequence;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append(LogPrefix);
            sb.Append(' ');
            sb.Append('#');
            sb.Append(localSeq.ToString("D6"));
            sb.Append(' ');
            sb.Append("EVENT=").Append(eventName);
            sb.Append(" PAWN=").Append(thingId);
            sb.Append(" NAME=").Append(name);
            sb.Append(" DEF=").Append(defName);

            try
            {
                sb.Append(" SCRIBE=").Append(Scribe.mode.ToString());
            }
            catch
            {
                sb.Append(" SCRIBE=<error>");
            }

            try
            {
                sb.Append(" PROGRAM=").Append(Current.ProgramState.ToString());
            }
            catch
            {
                sb.Append(" PROGRAM=<error>");
            }

            try
            {
                if (Find.TickManager != null)
                {
                    sb.Append(" TICK=").Append(Find.TickManager.TicksGame);
                }
                else
                {
                    sb.Append(" TICK=N/A");
                }
            }
            catch
            {
                sb.Append(" TICK=N/A");
            }

            sb.Append(" DETAILS=").Append(details ?? string.Empty);

            string line = sb.ToString();

            try
            {
                Log.Message(line);
            }
            catch
            {
                try
                {
                    global::System.Diagnostics.Debug.WriteLine(line);
                }
                catch
                {
                    // 放弃：不得影响加载。
                }
            }

            if (stack != null)
            {
                try
                {
                    Log.Message(LogPrefix + " STACK " + stack);
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        private static void WriteSelfError(string message)
        {
            try
            {
                Log.Warning(LogPrefix + " SELF-ERROR " + (message ?? string.Empty));
            }
            catch
            {
                // 忽略。
            }
        }

        // ===== Pawn 获取 =====

        internal static Pawn? GetPawnFromHealthTracker(Pawn_HealthTracker? health)
        {
            if (health == null)
            {
                return null;
            }

            try
            {
                if (pawnFromHealthTracker != null)
                {
                    return pawnFromHealthTracker(health);
                }
            }
            catch (Exception ex)
            {
                WriteSelfError("读取 Pawn_HealthTracker.pawn 失败：" + ex);
            }

            return null;
        }

        // ===== 首次死亡转换 =====

        internal static void ReportFirstDeadTransition(
            string methodName,
            Pawn? pawn,
            string? triggeringHediffDefName,
            bool deadBefore,
            bool deadAfter,
            float? consciousnessBefore,
            float? consciousnessAfter,
            string? details)
        {
            if (!Enabled)
            {
                return;
            }

            string? thingId = SafeThingId(pawn);
            if (thingId == null)
            {
                return;
            }

            bool already;
            lock (sessionLock)
            {
                already = !firstDeathLoggedPawnIds.Add(thingId);
            }

            if (already)
            {
                return;
            }

            if (deadBefore && !deadAfter)
            {
                // 不是“存活->死亡”的首次转换，跳过。
                return;
            }

            try
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(LogPrefix);
                sb.Append(' ');
                int localSeq;
                lock (sessionLock)
                {
                    localSeq = ++sequence;
                }

                sb.Append('#').Append(localSeq.ToString("D6")).Append(' ');
                sb.Append("*** FIRST DEAD TRANSITION ***");
                sb.Append(" PAWN=").Append(thingId);
                sb.Append(" NAME=").Append(SafePawnName(pawn));
                sb.Append(" METHOD=").Append(methodName ?? "?");
                sb.Append(" TRIGGER_HEDIFF=").Append(triggeringHediffDefName ?? "null");
                try
                {
                    sb.Append(" SCRIBE=").Append(Scribe.mode.ToString());
                }
                catch
                {
                    sb.Append(" SCRIBE=<error>");
                }

                try
                {
                    sb.Append(" PROGRAM=").Append(Current.ProgramState.ToString());
                }
                catch
                {
                    sb.Append(" PROGRAM=<error>");
                }

                sb.Append(" CONSCIOUSNESS_BEFORE=")
                    .Append(FormatFloat(consciousnessBefore));
                sb.Append(" CONSCIOUSNESS_AFTER=")
                    .Append(FormatFloat(consciousnessAfter));
                sb.Append(" DETAILS=").Append(details ?? string.Empty);

                try
                {
                    Log.Message(sb.ToString());
                }
                catch
                {
                    try
                    {
                        global::System.Diagnostics.Debug.WriteLine(sb.ToString());
                    }
                    catch
                    {
                        // 忽略。
                    }
                }

                // 操作前后 Hediff 列表、数据处理记录、身份记录、完整调用栈。
                try
                {
                    Log.Message(LogPrefix + " FIRST-DEAD HEDIFF-LIST-BEFORE " +
                        SafeThingId(pawn) + " " + BuildHediffList(pawn, "BEFORE"));
                }
                catch
                {
                    // 忽略。
                }

                try
                {
                    Log.Message(LogPrefix + " FIRST-DEAD HEDIFF-LIST-AFTER " +
                        SafeThingId(pawn) + " " + BuildHediffList(pawn, "AFTER"));
                }
                catch
                {
                    // 忽略。
                }

                try
                {
                    Log.Message(LogPrefix + " FIRST-DEAD DATAPROC-BEFORE " +
                        SafeThingId(pawn) + " " + BuildDataProcessingSnapshot(pawn));
                }
                catch
                {
                    // 忽略。
                }

                try
                {
                    Log.Message(LogPrefix + " FIRST-DEAD DATAPROC-AFTER " +
                        SafeThingId(pawn) + " " + BuildDataProcessingSnapshot(pawn));
                }
                catch
                {
                    // 忽略。
                }

                try
                {
                    Log.Message(LogPrefix + " FIRST-DEAD MECHANITOR-RECORD " +
                        SafeThingId(pawn) + " " + BuildMechanitorRecordSnapshot(pawn));
                }
                catch
                {
                    // 忽略。
                }

                try
                {
                    Log.Message(LogPrefix + " FIRST-DEAD STACK " +
                        new StackTrace(1, true).ToString());
                }
                catch
                {
                    // 忽略。
                }
            }
            catch
            {
                // 忽略。
            }
        }

        // ===== 快照 =====

        internal static string BuildPawnSnapshot(Pawn? pawn)
        {
            if (pawn == null)
            {
                return "PAWN=null";
            }

            StringBuilder sb = new StringBuilder();
            try
            {
                sb.Append("THINGID=").Append(SafeThingId(pawn));
                sb.Append('|');
                sb.Append("NAME=").Append(SafePawnName(pawn));
                sb.Append('|');
                sb.Append("DEF=").Append(SafeDefName(pawn));
                sb.Append('|');
                sb.Append("KIND=").Append(pawn.kindDef?.defName ?? "null");
            }
            catch (Exception ex)
            {
                sb.Append("<error:").Append(ex.GetType().Name).Append('>');
            }

            try
            {
                sb.Append('|');
                sb.Append("DEAD=").Append(pawn.Dead);
                sb.Append('|');
                sb.Append("DESTROYED=").Append(pawn.Destroyed);
                sb.Append('|');
                sb.Append("DISCARDED=").Append(pawn.Discarded);
                sb.Append('|');
                sb.Append("SPAWNED=").Append(pawn.Spawned);
            }
            catch (Exception ex)
            {
                sb.Append("<error:").Append(ex.GetType().Name).Append('>');
            }

            try
            {
                Pawn_HealthTracker? health = pawn.health;
                sb.Append('|');
                sb.Append("HEALTH_STATE=").Append(health?.State.ToString() ?? "null");
                sb.Append('|');
                sb.Append("IS_BEING_KILLED=").Append(health?.isBeingKilled == true);
            }
            catch (Exception ex)
            {
                sb.Append("<error:").Append(ex.GetType().Name).Append('>');
            }

            try
            {
                Map? map = pawn.Map;
                sb.Append('|');
                sb.Append("MAP_ID=").Append(map?.uniqueID.ToString() ?? "null");
                IntVec3 pos = pawn.Position;
                sb.Append('|');
                sb.Append("POS=").Append(
                    pos.IsValid ? pos.ToString() : "Invalid");
            }
            catch (Exception ex)
            {
                sb.Append("<error:").Append(ex.GetType().Name).Append('>');
            }

            // 意识状态：只读取，不触发死亡判断。
            try
            {
                PawnCapacityDef? consciousness = PawnCapacityDefOf.Consciousness;
                if (consciousness != null && pawn.health?.capacities != null)
                {
                    float level = pawn.health.capacities.GetLevel(consciousness);
                    sb.Append('|');
                    sb.Append("CONSCIOUSNESS_LEVEL=").Append(level.ToString("F4"));
                    bool capable = pawn.health.capacities.CapableOf(consciousness);
                    sb.Append('|');
                    sb.Append("CONSCIOUSNESS_CAPABLE=").Append(capable);
                }
                else
                {
                    sb.Append("|CONSCIOUSNESS=<unavailable>");
                }
            }
            catch (Exception ex)
            {
                sb.Append("<error:").Append(ex.GetType().Name).Append('>');
            }

            try
            {
                sb.Append('|');
                sb.Append("HEDIFFS=[").Append(BuildHediffList(pawn, "SNAPSHOT")).Append(']');
            }
            catch (Exception ex)
            {
                sb.Append("<error:").Append(ex.GetType().Name).Append('>');
            }

            return sb.ToString();
        }

        internal static string BuildHediffList(Pawn? pawn, string context)
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                HediffSet? set = pawn?.health?.hediffSet;
                if (set?.hediffs == null)
                {
                    sb.Append("null");
                    return sb.ToString();
                }

                List<Hediff> hediffs = set.hediffs;
                for (int i = 0; i < hediffs.Count; i++)
                {
                    Hediff? hediff = hediffs[i];
                    if (hediff == null)
                    {
                        continue;
                    }

                    if (i > 0)
                    {
                        sb.Append("; ");
                    }

                    sb.Append('{');
                    sb.Append("TYPE=").Append(hediff.GetType().Name);
                    sb.Append(' ');
                    sb.Append("DEF=").Append(hediff.def?.defName ?? "null");
                    sb.Append(' ');
                    sb.Append("SEV=").Append(hediff.Severity.ToString("F2"));
                    sb.Append(' ');
                    try
                    {
                        sb.Append("STAGE=").Append(hediff.CurStageIndex);
                    }
                    catch (Exception ex)
                    {
                        sb.Append("STAGE=<error:").Append(ex.GetType().Name).Append('>');
                    }

                    sb.Append(' ');
                    sb.Append("PART=").Append(hediff.Part?.LabelCap ?? "null");
                    sb.Append(' ');
                    sb.Append("IS_DP=")
                        .Append(hediff is Hediff_DataProcessingAllocationBase
                            || hediff is Hediff_DynamicConsciousnessBonusBase);

                    // 当前 Stage 对 Consciousness 的修正（可能失败，单独捕获）。
                    try
                    {
                        HediffStage? stage = hediff.CurStage;
                        if (stage?.capMods != null)
                        {
                            foreach (PawnCapacityModifier mod in stage.capMods)
                            {
                                if (mod.capacity == PawnCapacityDefOf.Consciousness)
                                {
                                    sb.Append(' ');
                                    sb.Append("CONS_OFFSET=").Append(mod.offset.ToString("F4"));
                                    sb.Append(' ');
                                    sb.Append("CONS_POSTFACTOR=")
                                        .Append(mod.postFactor.ToString("F4"));
                                    sb.Append(' ');
                                    sb.Append("CONS_SETMAX=")
                                        .Append(mod.setMax.ToString("F4"));
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        sb.Append(" CONS=<error:").Append(ex.GetType().Name).Append('>');
                    }

                    sb.Append('}');
                }
            }
            catch (Exception ex)
            {
                sb.Append("<error:").Append(ex.GetType().Name).Append('>');
            }

            return sb.ToString();
        }

        internal static string BuildMechanitorRecordSnapshot(Pawn? pawn)
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                if (!GameComponent_MechanoidMechanitorRegistry
                        .TryGetMechanitorRecord(pawn, out MechanoidMechanitorRecord? record))
                {
                    sb.Append("HAS_RECORD=false");
                    return sb.ToString();
                }

                sb.Append("HAS_RECORD=true");
                sb.Append('|');
                sb.Append("ORIGIN=").Append(record?.Origin.ToString() ?? "null");
                sb.Append('|');
                sb.Append("CHIP_BANDWIDTH_BONUS=")
                    .Append(record?.ChipBandwidthBonus.ToString() ?? "null");
                sb.Append('|');
                sb.Append("ROLE_WORK_INIT=")
                    .Append(record?.RoleWorkSettingsInitialized.ToString() ?? "null");
                sb.Append('|');
                sb.Append("SELF_WORK_MODE=")
                    .Append(record?.SelfWorkMode?.defName ?? "null");

                try
                {
                    bool isHost = GameComponent_MechanoidMechanitorRegistry
                        .IsMechanicalConsciousnessHost(pawn);
                    sb.Append('|');
                    sb.Append("IS_CONSCIOUSNESS_HOST=").Append(isHost);
                }
                catch
                {
                    sb.Append("|IS_CONSCIOUSNESS_HOST=<error>");
                }
            }
            catch (Exception ex)
            {
                sb.Append("<error:").Append(ex.GetType().Name).Append('>');
            }

            return sb.ToString();
        }

        internal static string BuildDataProcessingSnapshot(Pawn? pawn)
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                GameComponent_DataProcessingAllocationRegistry? registry =
                    GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;
                if (registry == null)
                {
                    sb.Append("REGISTRY=null");
                    return sb.ToString();
                }

                sb.Append("REGISTRY=present");

                try
                {
                    sb.Append('|');
                    sb.Append("STEPS_FOR_TARGET=").Append(registry.GetStepsForTarget(pawn));
                }
                catch
                {
                    sb.Append("|STEPS_FOR_TARGET=<error>");
                }

                try
                {
                    sb.Append('|');
                    sb.Append("TOTAL_STEPS_FOR_OVERSEER=")
                        .Append(registry.GetTotalStepsForOverseer(pawn));
                }
                catch
                {
                    sb.Append("|TOTAL_STEPS_FOR_OVERSEER=<error>");
                }

                try
                {
                    DataProcessingDynamicTargetRecord? config =
                        registry.GetDynamicTargetRecord(null, pawn);
                    sb.Append('|');
                    if (config == null)
                    {
                        sb.Append("DYNAMIC_TARGET_CONFIG=null");
                    }
                    else
                    {
                        sb.Append("DYNAMIC_TARGET_CONFIG{enabled=")
                            .Append(config.enabled)
                            .Append(", overseer=").Append(SafeThingId(config.overseer))
                            .Append(", normalSteps=").Append(config.normalSteps)
                            .Append(", defaultSpec=").Append(config.defaultSpecialization)
                            .Append('}');
                    }
                }
                catch
                {
                    sb.Append("|DYNAMIC_TARGET_CONFIG=<error>");
                }

                try
                {
                    sb.Append('|');
                    sb.Append("CURRENT_DATASTREAM_HEDIFF=")
                        .Append(FindHediffDefName(
                            pawn, DataProcessingAllocationUtility.DataStreamDistributionDef));
                }
                catch
                {
                    sb.Append("|CURRENT_DATASTREAM_HEDIFF=<error>");
                }

                try
                {
                    sb.Append('|');
                    sb.Append("CURRENT_COMMAND_FOCUS_HEDIFF=")
                        .Append(FindCommandFocusDefName(pawn));
                }
                catch
                {
                    sb.Append("|CURRENT_COMMAND_FOCUS_HEDIFF=<error>");
                }
            }
            catch (Exception ex)
            {
                sb.Append("<error:").Append(ex.GetType().Name).Append('>');
            }

            return sb.ToString();
        }

        private static string FindHediffDefName(Pawn? pawn, HediffDef? def)
        {
            if (pawn == null || def == null)
            {
                return "null";
            }

            HediffSet? set = pawn.health?.hediffSet;
            if (set?.hediffs == null)
            {
                return "null";
            }

            foreach (Hediff hediff in set.hediffs)
            {
                if (hediff?.def == def)
                {
                    return def.defName;
                }
            }

            return "null";
        }

        private static string FindCommandFocusDefName(Pawn? pawn)
        {
            if (pawn == null)
            {
                return "null";
            }

            HediffSet? set = pawn.health?.hediffSet;
            if (set?.hediffs == null)
            {
                return "null";
            }

            foreach (Hediff hediff in set.hediffs)
            {
                if (hediff?.def != null
                    && DataProcessingAllocationUtility.IsAnyCommandFocusDef(hediff.def))
                {
                    return hediff.def.defName;
                }
            }

            return "null";
        }

        internal static string BuildParallelThoughtArraySnapshot(
            CompParallelThoughtArray? comp)
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                if (comp == null)
                {
                    sb.Append("COMP=null");
                    return sb.ToString();
                }

                Building? building = comp.parent as Building;
                sb.Append("BUILDING_THINGID=").Append(SafeThingId(building));
                sb.Append('|');
                sb.Append("SPAWNED=").Append(comp.parent?.Spawned == true);
                sb.Append('|');
                try
                {
                    sb.Append("MAP_ID=").Append(comp.parent?.Map?.uniqueID.ToString() ?? "null");
                }
                catch
                {
                    sb.Append("MAP_ID=<error>");
                }

                sb.Append('|');
                sb.Append("TARGET=").Append(SafeThingId(comp.Target));
                sb.Append('|');
                sb.Append("TARGET_VALID=").Append(comp.IsTargetValid);
                sb.Append('|');
                sb.Append("CONFIGURED_BOOST=").Append(comp.ConfiguredBoostPercent);
                sb.Append('|');
                sb.Append("EFFECTIVE_BOOST=").Append(comp.EffectiveBoostPercent);
                sb.Append('|');

                try
                {
                    // lastEffectiveBoostPercent 为私有字段，使用只读反射。
                    FieldInfo? field = AccessTools.Field(
                        typeof(CompParallelThoughtArray), "lastEffectiveBoostPercent");
                    object? val = field?.GetValue(comp);
                    sb.Append("LAST_EFFECTIVE_BOOST=")
                        .Append(val?.ToString() ?? "null");
                }
                catch
                {
                    sb.Append("LAST_EFFECTIVE_BOOST=<error>");
                }

                sb.Append('|');
                sb.Append("IS_OPERATING=").Append(comp.IsOperating);
                sb.Append('|');

                try
                {
                    CompPowerTrader? power = comp.parent?.TryGetComp<CompPowerTrader>();
                    sb.Append("POWER_TRADER=").Append(power != null);
                    sb.Append('|');
                    sb.Append("POWER_ON=").Append(power?.PowerOn == true);
                    sb.Append('|');
                    sb.Append("POWER_OUTPUT=")
                        .Append(power != null ? power.PowerOutput.ToString("F1") : "null");
                }
                catch
                {
                    sb.Append("POWER=<error>");
                }

                sb.Append('|');
                sb.Append("REQUESTED_POWER=")
                    .Append(comp.RequestedPowerConsumption.ToString("F1"));
                sb.Append('|');

                try
                {
                    sb.Append("FACTION=")
                        .Append(comp.parent?.Faction?.Name ?? "null");
                }
                catch
                {
                    sb.Append("FACTION=<error>");
                }
            }
            catch (Exception ex)
            {
                sb.Append("<error:").Append(ex.GetType().Name).Append('>');
            }

            return sb.ToString();
        }

        // ===== 通用辅助 =====

        internal static bool TryGetConsciousness(Pawn? pawn, out float value)
        {
            value = 0f;
            try
            {
                PawnCapacityDef? consciousness = PawnCapacityDefOf.Consciousness;
                if (consciousness != null && pawn != null && pawn.health.capacities != null)
                {
                    value = pawn.health.capacities.GetLevel(consciousness);
                    return true;
                }
            }
            catch
            {
                // 忽略。
            }

            return false;
        }

        internal static Hediff? FindHediffArgument(object[] args)
        {
            if (args == null)
            {
                return null;
            }

            foreach (object arg in args)
            {
                if (arg is Hediff hediff)
                {
                    return hediff;
                }
            }

            return null;
        }

        internal static string FormatMethod(MethodBase? method)
        {
            try
            {
                if (method == null)
                {
                    return "?";
                }

                Type? declaring = method.DeclaringType;
                return (declaring?.FullName ?? "?") + "." + method.Name
                    + "(" + string.Join(", ",
                        Array.ConvertAll(
                            method.GetParameters(),
                            p => (p.ParameterType.Name + " " + p.Name))) + ")";
            }
            catch
            {
                return "?";
            }
        }

        internal static string FormatDamageInfo(object[] args)
        {
            if (args == null)
            {
                return "null";
            }

            foreach (object arg in args)
            {
                if (arg is DamageInfo dinfo)
                {
                    try
                    {
                        return "DamageInfo{Amount=" + dinfo.Amount
                            + ", Def=" + (dinfo.Def?.defName ?? "null")
                            + ", Part=" + (dinfo.HitPart?.LabelCap ?? "null") + "}";
                    }
                    catch
                    {
                        return "DamageInfo<error>";
                    }
                }
            }

            return "null";
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

                if (pawn.Name is Name name)
                {
                    return name.ToStringFull ?? "null";
                }

                return pawn.LabelShortCap ?? "null";
            }
            catch
            {
                return "<error>";
            }
        }

        internal static string SafeDefName(Pawn? pawn)
        {
            try
            {
                return pawn?.def?.defName ?? "null";
            }
            catch
            {
                return "<error>";
            }
        }

        private static string FormatFloat(float? value)
        {
            if (value == null)
            {
                return "null";
            }

            try
            {
                return value.Value.ToString("F4");
            }
            catch
            {
                return "<error>";
            }
        }
    }
}
