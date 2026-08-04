using System;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 原版健康状态诊断补丁。全部 Prefix/Postfix/Finalizer 仅记录，不改变返回值或异常。
    /// </summary>
    internal static class LoadDeathHealthDiagnosticPatches
    {
        [HarmonyPatch(typeof(Game), nameof(Game.LoadGame))]
        internal static class GameLoadGamePatch
        {
            private static void Prefix()
            {
                LoadDeathDiagnosticUtility.BeginLoadSession("Game.LoadGame Prefix");
                LoadDeathDiagnosticUtility.Write(
                    "Game.LoadGame.Enter",
                    null,
                    "存档加载开始。",
                    false);
            }

            private static void Postfix()
            {
                LoadDeathDiagnosticUtility.Write(
                    "Game.LoadGame.Exit",
                    null,
                    "Game.LoadGame 正常返回。",
                    false);
            }

            private static Exception? Finalizer(Exception? __exception)
            {
                LoadDeathDiagnosticUtility.EndLoadSession(
                    "Game.LoadGame Finalizer",
                    __exception);
                return __exception;
            }
        }

        [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.ExposeData))]
        internal static class HealthExposeDataPatch
        {
            private static void Prefix(
                Pawn_HealthTracker __instance,
                ref LoadDeathPawnState? __state)
            {
                __state = null;
                try
                {
                    if (!LoadDeathDiagnosticUtility.Active
                        || (Scribe.mode != LoadSaveMode.LoadingVars
                            && Scribe.mode != LoadSaveMode.ResolvingCrossRefs
                            && Scribe.mode != LoadSaveMode.PostLoadInit))
                    {
                        return;
                    }

                    Pawn? pawn = LoadDeathDiagnosticUtility.GetPawnFromHealthTracker(__instance);
                    if (!LoadDeathDiagnosticUtility.ShouldTracePawn(pawn))
                    {
                        return;
                    }

                    __state = LoadDeathDiagnosticUtility.CaptureState(
                        pawn,
                        "Pawn_HealthTracker.ExposeData",
                        null,
                        false);
                    LoadDeathDiagnosticUtility.Write(
                        "Health.ExposeData.Enter",
                        pawn,
                        "MODE=" + Scribe.mode
                        + " BEFORE={" + __state.passivePawnBefore + "}"
                        + " HEDIFFS={" + __state.hediffsBefore + "}",
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }

            private static void Postfix(
                Pawn_HealthTracker __instance,
                LoadDeathPawnState? __state)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Active)
                    {
                        return;
                    }

                    Pawn? pawn = LoadDeathDiagnosticUtility.GetPawnFromHealthTracker(__instance);
                    if (__state == null && !LoadDeathDiagnosticUtility.ShouldTracePawn(pawn))
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "Health.ExposeData.Exit",
                        pawn,
                        "MODE=" + Scribe.mode
                        + " DEAD_BEFORE=" + (__state?.deadBefore.ToString() ?? "unavailable")
                        + " DEAD_AFTER=" + LoadDeathDiagnosticUtility.SafeDead(pawn)
                        + " AFTER={" + LoadDeathDiagnosticUtility.BuildPassivePawnSnapshot(pawn) + "}"
                        + " HEDIFFS={" + LoadDeathDiagnosticUtility.BuildPassiveHediffList(pawn) + "}",
                        false);
                    LoadDeathDiagnosticUtility.ReportFirstDeadTransition(
                        __state,
                        pawn,
                        "Pawn_HealthTracker.ExposeData",
                        null);
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        [HarmonyPatch(
            typeof(Pawn_HealthTracker),
            nameof(Pawn_HealthTracker.CheckForStateChange),
            new[] { typeof(DamageInfo?), typeof(Hediff) })]
        internal static class HealthCheckForStateChangePatch
        {
            private static void Prefix(
                Pawn_HealthTracker __instance,
                DamageInfo? dinfo,
                Hediff hediff,
                ref LoadDeathPawnState? __state)
            {
                CaptureHealthOperation(
                    __instance,
                    "Health.CheckForStateChange.Enter",
                    "Pawn_HealthTracker.CheckForStateChange",
                    hediff,
                    FormatDamage(dinfo),
                    ref __state);
            }

            private static void Postfix(
                Pawn_HealthTracker __instance,
                Hediff hediff,
                LoadDeathPawnState? __state)
            {
                CompleteHealthOperation(
                    __instance,
                    "Health.CheckForStateChange.Exit",
                    "Pawn_HealthTracker.CheckForStateChange",
                    hediff,
                    __state);
            }
        }

        [HarmonyPatch(
            typeof(Pawn_HealthTracker),
            nameof(Pawn_HealthTracker.AddHediff),
            new[]
            {
                typeof(Hediff),
                typeof(BodyPartRecord),
                typeof(DamageInfo?),
                typeof(DamageWorker.DamageResult)
            })]
        internal static class HealthAddHediffPatch
        {
            private static void Prefix(
                Pawn_HealthTracker __instance,
                Hediff hediff,
                DamageInfo? dinfo,
                ref LoadDeathPawnState? __state)
            {
                CaptureHealthOperation(
                    __instance,
                    "Health.AddHediff.Enter",
                    "Pawn_HealthTracker.AddHediff(Hediff,...)",
                    hediff,
                    FormatDamage(dinfo),
                    ref __state);
            }

            private static void Postfix(
                Pawn_HealthTracker __instance,
                Hediff hediff,
                LoadDeathPawnState? __state)
            {
                CompleteHealthOperation(
                    __instance,
                    "Health.AddHediff.Exit",
                    "Pawn_HealthTracker.AddHediff(Hediff,...)",
                    hediff,
                    __state);
            }
        }

        [HarmonyPatch(
            typeof(Pawn_HealthTracker),
            nameof(Pawn_HealthTracker.RemoveHediff),
            new[] { typeof(Hediff) })]
        internal static class HealthRemoveHediffPatch
        {
            private static void Prefix(
                Pawn_HealthTracker __instance,
                Hediff hediff,
                ref LoadDeathPawnState? __state)
            {
                CaptureHealthOperation(
                    __instance,
                    "Health.RemoveHediff.Enter",
                    "Pawn_HealthTracker.RemoveHediff",
                    hediff,
                    string.Empty,
                    ref __state);
            }

            private static void Postfix(
                Pawn_HealthTracker __instance,
                Hediff hediff,
                LoadDeathPawnState? __state)
            {
                CompleteHealthOperation(
                    __instance,
                    "Health.RemoveHediff.Exit",
                    "Pawn_HealthTracker.RemoveHediff",
                    hediff,
                    __state);
            }
        }

        [HarmonyPatch(
            typeof(Pawn_HealthTracker),
            nameof(Pawn_HealthTracker.Notify_HediffChanged),
            new[] { typeof(Hediff) })]
        internal static class HealthNotifyHediffChangedPatch
        {
            private static void Prefix(
                Pawn_HealthTracker __instance,
                Hediff hediff,
                ref LoadDeathPawnState? __state)
            {
                CaptureHealthOperation(
                    __instance,
                    "Health.NotifyHediffChanged.Enter",
                    "Pawn_HealthTracker.Notify_HediffChanged",
                    hediff,
                    string.Empty,
                    ref __state);
            }

            private static void Postfix(
                Pawn_HealthTracker __instance,
                Hediff hediff,
                LoadDeathPawnState? __state)
            {
                CompleteHealthOperation(
                    __instance,
                    "Health.NotifyHediffChanged.Exit",
                    "Pawn_HealthTracker.Notify_HediffChanged",
                    hediff,
                    __state);
            }
        }

        [HarmonyPatch(
            typeof(Pawn),
            nameof(Pawn.Kill),
            new[] { typeof(DamageInfo?), typeof(Hediff) })]
        internal static class PawnKillPatch
        {
            private static void Prefix(
                Pawn __instance,
                DamageInfo? dinfo,
                Hediff exactCulprit,
                ref LoadDeathPawnState? __state)
            {
                __state = null;
                try
                {
                    if (!LoadDeathDiagnosticUtility.Active
                        || !LoadDeathDiagnosticUtility.ShouldTracePawn(__instance))
                    {
                        return;
                    }

                    __state = LoadDeathDiagnosticUtility.CaptureState(
                        __instance,
                        "Pawn.Kill",
                        exactCulprit?.def?.defName,
                        true);
                    LoadDeathDiagnosticUtility.WriteWithStack(
                        "Pawn.Kill.Enter",
                        __instance,
                        "DAMAGE=" + FormatDamage(dinfo)
                        + " EXACT_CULPRIT=" + (exactCulprit?.def?.defName ?? "null")
                        + " BEFORE={" + __state.passivePawnBefore + "}"
                        + " HEDIFFS={" + __state.hediffsBefore + "}"
                        + " DATAPROC={" + __state.dataProcessingBefore + "}"
                        + " MECHANITOR={" + __state.mechanitorBefore + "}",
                        __state.stackTrace);
                }
                catch
                {
                    // 不得阻止 Kill。
                }
            }

            private static void Postfix(
                Pawn __instance,
                Hediff exactCulprit,
                LoadDeathPawnState? __state)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Active || __state == null)
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "Pawn.Kill.Exit",
                        __instance,
                        "DEAD=" + LoadDeathDiagnosticUtility.SafeDead(__instance)
                        + " DESTROYED=" + __instance.Destroyed
                        + " CORPSE=" + LoadDeathDiagnosticUtility.SafeThingId(__instance.Corpse)
                        + " AFTER={" + LoadDeathDiagnosticUtility.BuildPassivePawnSnapshot(__instance) + "}"
                        + " HEDIFFS={" + LoadDeathDiagnosticUtility.BuildPassiveHediffList(__instance) + "}"
                        + " DATAPROC={" + LoadDeathDiagnosticUtility.BuildDataProcessingSnapshot(__instance) + "}"
                        + " MECHANITOR={" + LoadDeathDiagnosticUtility.BuildMechanitorRecordSnapshot(__instance) + "}",
                        false);
                    LoadDeathDiagnosticUtility.ReportFirstDeadTransition(
                        __state,
                        __instance,
                        "Pawn.Kill",
                        exactCulprit?.def?.defName);
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        [HarmonyPatch(
            typeof(Pawn),
            nameof(Pawn.SpawnSetup),
            new[] { typeof(Map), typeof(bool) })]
        internal static class PawnSpawnSetupPatch
        {
            private static void Prefix(Pawn __instance, Map map, bool respawningAfterLoad)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Active)
                    {
                        return;
                    }

                    bool trace = LoadDeathDiagnosticUtility.ShouldTracePawn(__instance);
                    bool deadMech = __instance.RaceProps?.IsMechanoid == true
                        && LoadDeathDiagnosticUtility.SafeDead(__instance);
                    if (!trace && !deadMech)
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "Pawn.SpawnSetup.Enter",
                        __instance,
                        "RESPAWNING_AFTER_LOAD=" + respawningAfterLoad
                        + " MAP=" + (map?.uniqueID.ToString() ?? "null")
                        + " DEAD_BEFORE_SPAWN_SETUP=" + LoadDeathDiagnosticUtility.SafeDead(__instance)
                        + " SNAPSHOT={" + LoadDeathDiagnosticUtility.BuildPassivePawnSnapshot(__instance) + "}"
                        + " HEDIFFS={" + LoadDeathDiagnosticUtility.BuildPassiveHediffList(__instance) + "}",
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }

            private static void Postfix(Pawn __instance, Map map, bool respawningAfterLoad)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Active
                        || (!LoadDeathDiagnosticUtility.ShouldTracePawn(__instance)
                            && !(__instance.RaceProps?.IsMechanoid == true
                                && LoadDeathDiagnosticUtility.SafeDead(__instance))))
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "Pawn.SpawnSetup.Exit",
                        __instance,
                        "RESPAWNING_AFTER_LOAD=" + respawningAfterLoad
                        + " MAP=" + (map?.uniqueID.ToString() ?? "null")
                        + " DEAD=" + LoadDeathDiagnosticUtility.SafeDead(__instance)
                        + " SNAPSHOT={" + LoadDeathDiagnosticUtility.BuildPassivePawnSnapshot(__instance) + "}",
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        private static void CaptureHealthOperation(
            Pawn_HealthTracker tracker,
            string eventName,
            string methodName,
            Hediff? hediff,
            string extra,
            ref LoadDeathPawnState? state)
        {
            state = null;
            try
            {
                if (!LoadDeathDiagnosticUtility.Active)
                {
                    return;
                }

                Pawn? pawn = LoadDeathDiagnosticUtility.GetPawnFromHealthTracker(tracker);
                if (!LoadDeathDiagnosticUtility.ShouldTracePawn(pawn))
                {
                    return;
                }

                state = LoadDeathDiagnosticUtility.CaptureState(
                    pawn,
                    methodName,
                    hediff?.def?.defName,
                    true);
                LoadDeathDiagnosticUtility.WriteWithStack(
                    eventName,
                    pawn,
                    "HEDIFF=" + (hediff?.def?.defName ?? "null")
                    + " TYPE=" + (hediff?.GetType().FullName ?? "null")
                    + " EXTRA=" + extra
                    + " BEFORE={" + state.passivePawnBefore + "}"
                    + " HEDIFFS={" + state.hediffsBefore + "}"
                    + " DATAPROC={" + state.dataProcessingBefore + "}"
                    + " MECHANITOR={" + state.mechanitorBefore + "}",
                    state.stackTrace);
            }
            catch
            {
                // 忽略。
            }
        }

        private static void CompleteHealthOperation(
            Pawn_HealthTracker tracker,
            string eventName,
            string methodName,
            Hediff? hediff,
            LoadDeathPawnState? state)
        {
            try
            {
                if (!LoadDeathDiagnosticUtility.Active || state == null)
                {
                    return;
                }

                Pawn? pawn = LoadDeathDiagnosticUtility.GetPawnFromHealthTracker(tracker);
                LoadDeathDiagnosticUtility.Write(
                    eventName,
                    pawn,
                    "HEDIFF=" + (hediff?.def?.defName ?? "null")
                    + " DEAD_BEFORE=" + state.deadBefore
                    + " DEAD_AFTER=" + LoadDeathDiagnosticUtility.SafeDead(pawn)
                    + " AFTER={" + LoadDeathDiagnosticUtility.BuildPassivePawnSnapshot(pawn) + "}"
                    + " HEDIFFS={" + LoadDeathDiagnosticUtility.BuildPassiveHediffList(pawn) + "}"
                    + " DATAPROC={" + LoadDeathDiagnosticUtility.BuildDataProcessingSnapshot(pawn) + "}"
                    + " MECHANITOR={" + LoadDeathDiagnosticUtility.BuildMechanitorRecordSnapshot(pawn) + "}",
                    false);
                LoadDeathDiagnosticUtility.ReportFirstDeadTransition(
                    state,
                    pawn,
                    methodName,
                    hediff?.def?.defName);
            }
            catch
            {
                // 忽略。
            }
        }

        private static string FormatDamage(DamageInfo? dinfo)
        {
            try
            {
                return dinfo.HasValue ? dinfo.Value.ToString() : "null";
            }
            catch
            {
                return "<error>";
            }
        }
    }
}
