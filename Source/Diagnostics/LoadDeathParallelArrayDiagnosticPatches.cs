using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 并行思维阵列与动态意识刷新诊断。只读取 Comp 私有字段和 Hediff 缓存字段，
    /// 不调用 IsOperating、EffectiveBoostPercent、CurStage 或任何刷新方法。
    /// </summary>
    internal static class LoadDeathParallelArrayDiagnosticPatches
    {
        [HarmonyPatch(
            typeof(CompParallelThoughtArray),
            nameof(CompParallelThoughtArray.PostExposeData))]
        internal static class ParallelArrayPostExposeDataPatch
        {
            private static void Prefix(CompParallelThoughtArray __instance)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Active)
                    {
                        return;
                    }

                    Pawn? target = __instance.Target;
                    if (target == null || !LoadDeathDiagnosticUtility.ShouldTracePawn(target))
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "ParallelArray.PostExposeData.Enter",
                        target,
                        "MODE=" + Scribe.mode
                        + " ARRAY_BEFORE={" + LoadDeathDiagnosticUtility.BuildParallelThoughtArraySnapshot(__instance) + "}"
                        + " TARGET_BEFORE={" + LoadDeathDiagnosticUtility.BuildPassivePawnSnapshot(target) + "}"
                        + " HEDIFFS_BEFORE={" + LoadDeathDiagnosticUtility.BuildPassiveHediffList(target) + "}",
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }

            private static void Postfix(CompParallelThoughtArray __instance)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Active)
                    {
                        return;
                    }

                    Pawn? target = __instance.Target;
                    if (target == null || !LoadDeathDiagnosticUtility.ShouldTracePawn(target))
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "ParallelArray.PostExposeData.Exit",
                        target,
                        "MODE=" + Scribe.mode
                        + " ARRAY_AFTER={" + LoadDeathDiagnosticUtility.BuildParallelThoughtArraySnapshot(__instance) + "}"
                        + " TARGET_AFTER={" + LoadDeathDiagnosticUtility.BuildPassivePawnSnapshot(target) + "}"
                        + " HEDIFFS_AFTER={" + LoadDeathDiagnosticUtility.BuildPassiveHediffList(target) + "}",
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        [HarmonyPatch(
            typeof(CompParallelThoughtArray),
            nameof(CompParallelThoughtArray.PostSpawnSetup),
            new[] { typeof(bool) })]
        internal static class ParallelArrayPostSpawnSetupPatch
        {
            private static void Prefix(
                CompParallelThoughtArray __instance,
                bool respawningAfterLoad,
                ref LoadDeathPawnState? __state)
            {
                CaptureArrayOperation(
                    __instance,
                    "ParallelArray.PostSpawnSetup.Enter",
                    "CompParallelThoughtArray.PostSpawnSetup"
                    + " RESPAWNING_AFTER_LOAD=" + respawningAfterLoad,
                    ref __state);
            }

            private static void Postfix(
                CompParallelThoughtArray __instance,
                bool respawningAfterLoad,
                LoadDeathPawnState? __state)
            {
                CompleteArrayOperation(
                    __instance,
                    "ParallelArray.PostSpawnSetup.Exit",
                    "CompParallelThoughtArray.PostSpawnSetup"
                    + " RESPAWNING_AFTER_LOAD=" + respawningAfterLoad,
                    __state);
            }
        }

        [HarmonyPatch(
            typeof(CompParallelThoughtArray),
            nameof(CompParallelThoughtArray.ReevaluateOperatingState),
            new[] { typeof(bool) })]
        internal static class ParallelArrayReevaluatePatch
        {
            private static void Prefix(
                CompParallelThoughtArray __instance,
                bool forceRefresh,
                ref LoadDeathPawnState? __state)
            {
                CaptureArrayOperation(
                    __instance,
                    "ParallelArray.Reevaluate.Enter",
                    "CompParallelThoughtArray.ReevaluateOperatingState"
                    + " FORCE_REFRESH=" + forceRefresh,
                    ref __state);
            }

            private static void Postfix(
                CompParallelThoughtArray __instance,
                bool forceRefresh,
                LoadDeathPawnState? __state)
            {
                CompleteArrayOperation(
                    __instance,
                    "ParallelArray.Reevaluate.Exit",
                    "CompParallelThoughtArray.ReevaluateOperatingState"
                    + " FORCE_REFRESH=" + forceRefresh,
                    __state);
            }
        }

        [HarmonyPatch(
            typeof(ParallelThoughtArrayUtility),
            nameof(ParallelThoughtArrayUtility.RefreshTargetDynamicConsciousness),
            new[] { typeof(Pawn) })]
        internal static class ParallelArrayRefreshTargetPatch
        {
            private static void Prefix(Pawn? target, ref LoadDeathPawnState? __state)
            {
                CapturePawnRefresh(
                    target,
                    "ParallelArray.RefreshTargetConsciousness.Enter",
                    "ParallelThoughtArrayUtility.RefreshTargetDynamicConsciousness",
                    ref __state);
            }

            private static void Postfix(Pawn? target, LoadDeathPawnState? __state)
            {
                CompletePawnRefresh(
                    target,
                    "ParallelArray.RefreshTargetConsciousness.Exit",
                    "ParallelThoughtArrayUtility.RefreshTargetDynamicConsciousness",
                    __state);
            }
        }

        [HarmonyPatch(
            typeof(DynamicConsciousnessBonusUtility),
            nameof(DynamicConsciousnessBonusUtility.RefreshForPawn),
            new[] { typeof(Pawn) })]
        internal static class DynamicConsciousnessRefreshForPawnPatch
        {
            private static void Prefix(Pawn? pawn, ref LoadDeathPawnState? __state)
            {
                CapturePawnRefresh(
                    pawn,
                    "DynamicConsciousness.RefreshForPawn.Enter",
                    "DynamicConsciousnessBonusUtility.RefreshForPawn",
                    ref __state);
            }

            private static void Postfix(Pawn? pawn, LoadDeathPawnState? __state)
            {
                CompletePawnRefresh(
                    pawn,
                    "DynamicConsciousness.RefreshForPawn.Exit",
                    "DynamicConsciousnessBonusUtility.RefreshForPawn",
                    __state);
            }
        }

        [HarmonyPatch(
            typeof(Hediff_DynamicConsciousnessBonusBase),
            nameof(Hediff_DynamicConsciousnessBonusBase.RefreshDynamicEffects),
            new[] { typeof(bool) })]
        internal static class DynamicConsciousnessRefreshHediffPatch
        {
            private sealed class State
            {
                public LoadDeathPawnState? pawnState;
                public string? cacheBefore;
            }

            private static void Prefix(
                Hediff_DynamicConsciousnessBonusBase __instance,
                bool notifyHealth,
                ref State? __state)
            {
                __state = null;
                try
                {
                    if (!LoadDeathDiagnosticUtility.Active)
                    {
                        return;
                    }

                    Pawn? pawn = __instance.pawn;
                    if (pawn == null || !LoadDeathDiagnosticUtility.ShouldTracePawn(pawn))
                    {
                        return;
                    }

                    __state = new State
                    {
                        pawnState = LoadDeathDiagnosticUtility.CaptureState(
                            pawn,
                            "Hediff_DynamicConsciousnessBonusBase.RefreshDynamicEffects",
                            __instance.def?.defName,
                            true),
                        cacheBefore = LoadDeathDiagnosticUtility
                            .BuildDynamicConsciousnessCacheSnapshot(__instance)
                    };

                    LoadDeathDiagnosticUtility.WriteWithStack(
                        "DynamicConsciousness.RefreshHediff.Enter",
                        pawn,
                        "HEDIFF=" + (__instance.def?.defName ?? "null")
                        + " NOTIFY_HEALTH=" + notifyHealth
                        + " CACHE_BEFORE={" + __state.cacheBefore + "}"
                        + " PAWN_BEFORE={" + __state.pawnState.passivePawnBefore + "}"
                        + " HEDIFFS_BEFORE={" + __state.pawnState.hediffsBefore + "}"
                        + " DATAPROC_BEFORE={" + __state.pawnState.dataProcessingBefore + "}",
                        __state.pawnState.stackTrace);
                }
                catch
                {
                    // 忽略。
                }
            }

            private static void Postfix(
                Hediff_DynamicConsciousnessBonusBase __instance,
                bool notifyHealth,
                State? __state)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Active
                        || __state?.pawnState == null)
                    {
                        return;
                    }

                    Pawn? pawn = __instance.pawn;
                    LoadDeathDiagnosticUtility.Write(
                        "DynamicConsciousness.RefreshHediff.Exit",
                        pawn,
                        "HEDIFF=" + (__instance.def?.defName ?? "null")
                        + " NOTIFY_HEALTH=" + notifyHealth
                        + " CACHE_BEFORE={" + __state.cacheBefore + "}"
                        + " CACHE_AFTER={"
                            + LoadDeathDiagnosticUtility.BuildDynamicConsciousnessCacheSnapshot(__instance)
                            + "}"
                        + " DEAD_BEFORE=" + __state.pawnState.deadBefore
                        + " DEAD_AFTER=" + LoadDeathDiagnosticUtility.SafeDead(pawn)
                        + " PAWN_AFTER={" + LoadDeathDiagnosticUtility.BuildPassivePawnSnapshot(pawn) + "}"
                        + " HEDIFFS_AFTER={" + LoadDeathDiagnosticUtility.BuildPassiveHediffList(pawn) + "}"
                        + " DATAPROC_AFTER={" + LoadDeathDiagnosticUtility.BuildDataProcessingSnapshot(pawn) + "}",
                        false);
                    LoadDeathDiagnosticUtility.ReportFirstDeadTransition(
                        __state.pawnState,
                        pawn,
                        "Hediff_DynamicConsciousnessBonusBase.RefreshDynamicEffects",
                        __instance.def?.defName);
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        private static void CaptureArrayOperation(
            CompParallelThoughtArray comp,
            string eventName,
            string methodName,
            ref LoadDeathPawnState? state)
        {
            state = null;
            try
            {
                if (!LoadDeathDiagnosticUtility.Active)
                {
                    return;
                }

                Pawn? target = comp.Target;
                if (target == null || !LoadDeathDiagnosticUtility.ShouldTracePawn(target))
                {
                    return;
                }

                state = LoadDeathDiagnosticUtility.CaptureState(
                    target,
                    methodName,
                    null,
                    true);
                LoadDeathDiagnosticUtility.WriteWithStack(
                    eventName,
                    target,
                    "ARRAY_BEFORE={" + LoadDeathDiagnosticUtility.BuildParallelThoughtArraySnapshot(comp) + "}"
                    + " TARGET_BEFORE={" + state.passivePawnBefore + "}"
                    + " HEDIFFS_BEFORE={" + state.hediffsBefore + "}"
                    + " DATAPROC_BEFORE={" + state.dataProcessingBefore + "}",
                    state.stackTrace);
            }
            catch
            {
                // 忽略。
            }
        }

        private static void CompleteArrayOperation(
            CompParallelThoughtArray comp,
            string eventName,
            string methodName,
            LoadDeathPawnState? state)
        {
            try
            {
                if (!LoadDeathDiagnosticUtility.Active || state == null)
                {
                    return;
                }

                Pawn? target = comp.Target;
                LoadDeathDiagnosticUtility.Write(
                    eventName,
                    target,
                    "ARRAY_AFTER={" + LoadDeathDiagnosticUtility.BuildParallelThoughtArraySnapshot(comp) + "}"
                    + " DEAD_BEFORE=" + state.deadBefore
                    + " DEAD_AFTER=" + LoadDeathDiagnosticUtility.SafeDead(target)
                    + " TARGET_AFTER={" + LoadDeathDiagnosticUtility.BuildPassivePawnSnapshot(target) + "}"
                    + " HEDIFFS_AFTER={" + LoadDeathDiagnosticUtility.BuildPassiveHediffList(target) + "}"
                    + " DATAPROC_AFTER={" + LoadDeathDiagnosticUtility.BuildDataProcessingSnapshot(target) + "}",
                    false);
                LoadDeathDiagnosticUtility.ReportFirstDeadTransition(
                    state,
                    target,
                    methodName,
                    null);
            }
            catch
            {
                // 忽略。
            }
        }

        private static void CapturePawnRefresh(
            Pawn? pawn,
            string eventName,
            string methodName,
            ref LoadDeathPawnState? state)
        {
            state = null;
            try
            {
                if (!LoadDeathDiagnosticUtility.Active
                    || pawn == null
                    || !LoadDeathDiagnosticUtility.ShouldTracePawn(pawn))
                {
                    return;
                }

                state = LoadDeathDiagnosticUtility.CaptureState(
                    pawn,
                    methodName,
                    null,
                    true);
                LoadDeathDiagnosticUtility.WriteWithStack(
                    eventName,
                    pawn,
                    "PAWN_BEFORE={" + state.passivePawnBefore + "}"
                    + " HEDIFFS_BEFORE={" + state.hediffsBefore + "}"
                    + " DATAPROC_BEFORE={" + state.dataProcessingBefore + "}",
                    state.stackTrace);
            }
            catch
            {
                // 忽略。
            }
        }

        private static void CompletePawnRefresh(
            Pawn? pawn,
            string eventName,
            string methodName,
            LoadDeathPawnState? state)
        {
            try
            {
                if (!LoadDeathDiagnosticUtility.Active || state == null)
                {
                    return;
                }

                LoadDeathDiagnosticUtility.Write(
                    eventName,
                    pawn,
                    "DEAD_BEFORE=" + state.deadBefore
                    + " DEAD_AFTER=" + LoadDeathDiagnosticUtility.SafeDead(pawn)
                    + " PAWN_AFTER={" + LoadDeathDiagnosticUtility.BuildPassivePawnSnapshot(pawn) + "}"
                    + " HEDIFFS_AFTER={" + LoadDeathDiagnosticUtility.BuildPassiveHediffList(pawn) + "}"
                    + " DATAPROC_AFTER={" + LoadDeathDiagnosticUtility.BuildDataProcessingSnapshot(pawn) + "}",
                    false);
                LoadDeathDiagnosticUtility.ReportFirstDeadTransition(
                    state,
                    pawn,
                    methodName,
                    null);
            }
            catch
            {
                // 忽略。
            }
        }
    }
}
