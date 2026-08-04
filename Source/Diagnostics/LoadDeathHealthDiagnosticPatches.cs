using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 原版健康状态诊断补丁：纯观察性质，全部 Prefix 继续执行原方法，
    /// 不阻止/延迟任何逻辑，不修改返回值。
    /// </summary>
    internal static class LoadDeathHealthDiagnosticPatches
    {
        // ===== Game.LoadGame 会话边界 =====

        [HarmonyPatch(typeof(Game), nameof(Game.LoadGame))]
        internal static class GameLoadGameSessionPatch
        {
            private static void Prefix()
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.ResetSession("Game.LoadGame Prefix");
                    LoadDeathDiagnosticUtility.Write(
                        "Game.LoadGame.Enter",
                        null,
                        "存档加载开始。",
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }

            private static void Postfix()
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "Game.LoadGame.Exit",
                        null,
                        "存档加载返回（未执行任何清理或同步）。",
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        // ===== Pawn_HealthTracker.ExposeData =====

        [HarmonyPatch(
            typeof(Pawn_HealthTracker),
            nameof(Pawn_HealthTracker.ExposeData))]
        internal static class HealthExposeDataPatch
        {
            private static void Prefix(Pawn_HealthTracker __instance)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    if (Scribe.mode != LoadSaveMode.LoadingVars
                        && Scribe.mode != LoadSaveMode.ResolvingCrossRefs
                        && Scribe.mode != LoadSaveMode.PostLoadInit)
                    {
                        return;
                    }

                    Pawn? pawn =
                        LoadDeathDiagnosticUtility.GetPawnFromHealthTracker(__instance);
                    if (pawn == null || !LoadDeathDiagnosticUtility.ShouldTracePawn(pawn))
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "Health.ExposeData.Enter",
                        pawn,
                        "SCRIBE=" + Scribe.mode
                        + " | SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(pawn),
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }

            private static void Postfix(Pawn_HealthTracker __instance)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    if (Scribe.mode != LoadSaveMode.LoadingVars
                        && Scribe.mode != LoadSaveMode.ResolvingCrossRefs
                        && Scribe.mode != LoadSaveMode.PostLoadInit)
                    {
                        return;
                    }

                    Pawn? pawn =
                        LoadDeathDiagnosticUtility.GetPawnFromHealthTracker(__instance);
                    if (pawn == null || !LoadDeathDiagnosticUtility.ShouldTracePawn(pawn))
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "Health.ExposeData.Exit",
                        pawn,
                        "SCRIBE=" + Scribe.mode
                        + " | SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(pawn),
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        // ===== Pawn_HealthTracker.CheckForStateChange =====

        [HarmonyPatch(
            typeof(Pawn_HealthTracker),
            nameof(Pawn_HealthTracker.CheckForStateChange))]
        internal static class HealthCheckForStateChangePatch
        {
            private sealed class State
            {
                public string? thingId;
                public bool deadBefore;
                public float? consciousnessBefore;
                public string? triggeringHediffDefName;
                public string? methodName;
            }

            private static void Prefix(
                Pawn_HealthTracker __instance,
                DamageInfo? dinfo,
                Hediff hediff,
                ref State __state)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    Pawn? pawn =
                        LoadDeathDiagnosticUtility.GetPawnFromHealthTracker(__instance);
                    __state = new State
                    {
                        methodName = "Pawn_HealthTracker.CheckForStateChange",
                    };

                    if (pawn == null)
                    {
                        return;
                    }

                    __state.thingId = LoadDeathDiagnosticUtility.SafeThingId(pawn);
                    __state.deadBefore = pawn.Dead;
                    LoadDeathDiagnosticUtility.TryGetConsciousness(
                        pawn, out float cons);
                    __state.consciousnessBefore = cons;
                    __state.triggeringHediffDefName = hediff?.def?.defName;

                    if (!LoadDeathDiagnosticUtility.ShouldTracePawn(pawn))
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "Health.CheckForStateChange.Enter",
                        pawn,
                        "DAMAGE=" + LoadDeathDiagnosticUtility.FormatDamageInfo(
                            new object[] { dinfo! })
                        + " | HEDIFF=" + (__state.triggeringHediffDefName ?? "null")
                        + " | SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(pawn),
                        true);
                }
                catch
                {
                    // 忽略。不在 Prefix 中调用死亡判断。
                }
            }

            private static void Postfix(
                Pawn_HealthTracker __instance,
                ref State __state)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    Pawn? pawn =
                        LoadDeathDiagnosticUtility.GetPawnFromHealthTracker(__instance);
                    if (pawn == null)
                    {
                        return;
                    }

                    bool deadAfter = pawn.Dead;
                    LoadDeathDiagnosticUtility.TryGetConsciousness(
                        pawn, out float consAfter);
                    float? consBefore = __state?.consciousnessBefore;

                    if (!LoadDeathDiagnosticUtility.ShouldTracePawn(pawn)
                        && !(__state != null && __state.deadBefore == false
                            && deadAfter == true))
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "Health.CheckForStateChange.Exit",
                        pawn,
                        "DEAD_BEFORE=" + (__state?.deadBefore.ToString() ?? "?")
                        + " DEAD_AFTER=" + deadAfter
                        + " CONS_BEFORE=" + (consBefore?.ToString("F4") ?? "null")
                        + " CONS_AFTER=" + consAfter.ToString("F4")
                        + " | SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(pawn),
                        false);

                    if (__state != null && __state.deadBefore == false
                        && deadAfter == true)
                    {
                        LoadDeathDiagnosticUtility.ReportFirstDeadTransition(
                            __state.methodName ?? "Pawn_HealthTracker.CheckForStateChange",
                            pawn,
                            __state.triggeringHediffDefName,
                            __state.deadBefore,
                            deadAfter,
                            consBefore,
                            consAfter,
                            "CheckForStateChange 调用后存活->死亡。");
                    }
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        // ===== Hediff 变化入口：AddHediff / RemoveHediff / Notify_HediffChanged =====

        [HarmonyPatch(
            typeof(Pawn_HealthTracker),
            nameof(Pawn_HealthTracker.AddHediff),
            new[] { typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) })]
        internal static class HealthAddHediffPatch
        {
            private sealed class State
            {
                public bool deadBefore;
                public float? consciousnessBefore;
                public string? triggeringHediffDefName;
                public string? methodName;
                public string? thingId;
            }

            private static void Prefix(Pawn_HealthTracker __instance, Hediff hediff, ref State __state)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    Pawn? pawn = LoadDeathDiagnosticUtility.GetPawnFromHealthTracker(__instance);
                    __state = new State
                    {
                        methodName = "Pawn_HealthTracker.AddHediff(Hediff,...)",
                        triggeringHediffDefName = hediff?.def?.defName,
                    };
                    if (pawn == null)
                    {
                        return;
                    }

                    __state.thingId = LoadDeathDiagnosticUtility.SafeThingId(pawn);
                    __state.deadBefore = pawn.Dead;
                    LoadDeathDiagnosticUtility.TryGetConsciousness(pawn, out float cons);
                    __state.consciousnessBefore = cons;

                    if (!LoadDeathDiagnosticUtility.ShouldTracePawn(pawn))
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "Health.AddHediff.Enter",
                        pawn,
                        "HEDIFF=" + (__state.triggeringHediffDefName ?? "null")
                        + " TYPE=" + (hediff?.GetType().Name ?? "?")
                        + " SEV=" + (hediff?.Severity.ToString("F2") ?? "?")
                        + " PART=" + (hediff?.Part?.LabelCap ?? "null")
                        + " | SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(pawn),
                        true);
                }
                catch
                {
                    // 忽略。
                }
            }

            private static void Postfix(Pawn_HealthTracker __instance, ref State __state)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    Pawn? pawn = LoadDeathDiagnosticUtility.GetPawnFromHealthTracker(__instance);
                    if (pawn == null)
                    {
                        return;
                    }

                    bool deadAfter = pawn.Dead;
                    LoadDeathDiagnosticUtility.TryGetConsciousness(pawn, out float consAfter);
                    float? consBefore = __state?.consciousnessBefore;

                    if (!LoadDeathDiagnosticUtility.ShouldTracePawn(pawn)
                        && !(__state != null && __state.deadBefore == false && deadAfter == true))
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "Health.AddHediff.Exit",
                        pawn,
                        "DEAD_BEFORE=" + (__state?.deadBefore.ToString() ?? "?")
                        + " DEAD_AFTER=" + deadAfter
                        + " CONS_BEFORE=" + (consBefore?.ToString("F4") ?? "null")
                        + " CONS_AFTER=" + consAfter.ToString("F4")
                        + " CONS_DELTA=" + ((consBefore.HasValue)
                            ? (consAfter - consBefore.Value).ToString("F4")
                            : "null")
                        + " | SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(pawn),
                        false);

                    if (__state != null && __state.deadBefore == false && deadAfter == true)
                    {
                        LoadDeathDiagnosticUtility.ReportFirstDeadTransition(
                            __state.methodName ?? "Pawn_HealthTracker.AddHediff",
                            pawn,
                            __state.triggeringHediffDefName,
                            __state.deadBefore,
                            deadAfter,
                            consBefore,
                            consAfter,
                            "AddHediff 调用后存活->死亡。");
                    }
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        [HarmonyPatch(
            typeof(Pawn_HealthTracker),
            nameof(Pawn_HealthTracker.RemoveHediff))]
        internal static class HealthRemoveHediffPatch
        {
            private sealed class State
            {
                public bool deadBefore;
                public float? consciousnessBefore;
                public string? triggeringHediffDefName;
                public string? methodName;
            }

            private static void Prefix(Pawn_HealthTracker __instance, Hediff hediff, ref State __state)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    Pawn? pawn = LoadDeathDiagnosticUtility.GetPawnFromHealthTracker(__instance);
                    __state = new State
                    {
                        methodName = "Pawn_HealthTracker.RemoveHediff",
                        triggeringHediffDefName = hediff?.def?.defName,
                    };
                    if (pawn == null)
                    {
                        return;
                    }

                    __state.deadBefore = pawn.Dead;
                    LoadDeathDiagnosticUtility.TryGetConsciousness(pawn, out float cons);
                    __state.consciousnessBefore = cons;

                    if (!LoadDeathDiagnosticUtility.ShouldTracePawn(pawn))
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "Health.RemoveHediff.Enter",
                        pawn,
                        "HEDIFF=" + (__state.triggeringHediffDefName ?? "null")
                        + " TYPE=" + (hediff?.GetType().Name ?? "?")
                        + " SEV=" + (hediff?.Severity.ToString("F2") ?? "?")
                        + " PART=" + (hediff?.Part?.LabelCap ?? "null")
                        + " | SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(pawn),
                        true);
                }
                catch
                {
                    // 忽略。
                }
            }

            private static void Postfix(Pawn_HealthTracker __instance, ref State __state)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    Pawn? pawn = LoadDeathDiagnosticUtility.GetPawnFromHealthTracker(__instance);
                    if (pawn == null)
                    {
                        return;
                    }

                    bool deadAfter = pawn.Dead;
                    LoadDeathDiagnosticUtility.TryGetConsciousness(pawn, out float consAfter);
                    float? consBefore = __state?.consciousnessBefore;

                    if (!LoadDeathDiagnosticUtility.ShouldTracePawn(pawn)
                        && !(__state != null && __state.deadBefore == false && deadAfter == true))
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "Health.RemoveHediff.Exit",
                        pawn,
                        "DEAD_BEFORE=" + (__state?.deadBefore.ToString() ?? "?")
                        + " DEAD_AFTER=" + deadAfter
                        + " CONS_BEFORE=" + (consBefore?.ToString("F4") ?? "null")
                        + " CONS_AFTER=" + consAfter.ToString("F4")
                        + " | SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(pawn),
                        false);

                    if (__state != null && __state.deadBefore == false && deadAfter == true)
                    {
                        LoadDeathDiagnosticUtility.ReportFirstDeadTransition(
                            __state.methodName ?? "Pawn_HealthTracker.RemoveHediff",
                            pawn,
                            __state.triggeringHediffDefName,
                            __state.deadBefore,
                            deadAfter,
                            consBefore,
                            consAfter,
                            "RemoveHediff 调用后存活->死亡。");
                    }
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        [HarmonyPatch(
            typeof(Pawn_HealthTracker),
            nameof(Pawn_HealthTracker.Notify_HediffChanged))]
        internal static class HealthNotifyHediffChangedPatch
        {
            private sealed class State
            {
                public bool deadBefore;
                public float? consciousnessBefore;
                public string? triggeringHediffDefName;
                public string? methodName;
            }

            private static void Prefix(Pawn_HealthTracker __instance, Hediff hediff, ref State __state)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    Pawn? pawn = LoadDeathDiagnosticUtility.GetPawnFromHealthTracker(__instance);
                    __state = new State
                    {
                        methodName = "Pawn_HealthTracker.Notify_HediffChanged",
                        triggeringHediffDefName = hediff?.def?.defName,
                    };
                    if (pawn == null)
                    {
                        return;
                    }

                    __state.deadBefore = pawn.Dead;
                    LoadDeathDiagnosticUtility.TryGetConsciousness(pawn, out float cons);
                    __state.consciousnessBefore = cons;

                    if (!LoadDeathDiagnosticUtility.ShouldTracePawn(pawn))
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "Health.NotifyHediffChanged.Enter",
                        pawn,
                        "HEDIFF=" + (__state.triggeringHediffDefName ?? "null")
                        + " TYPE=" + (hediff?.GetType().Name ?? "?")
                        + " SEV=" + (hediff?.Severity.ToString("F2") ?? "?")
                        + " PART=" + (hediff?.Part?.LabelCap ?? "null")
                        + " | SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(pawn),
                        true);
                }
                catch
                {
                    // 忽略。
                }
            }

            private static void Postfix(Pawn_HealthTracker __instance, ref State __state)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    Pawn? pawn = LoadDeathDiagnosticUtility.GetPawnFromHealthTracker(__instance);
                    if (pawn == null)
                    {
                        return;
                    }

                    bool deadAfter = pawn.Dead;
                    LoadDeathDiagnosticUtility.TryGetConsciousness(pawn, out float consAfter);
                    float? consBefore = __state?.consciousnessBefore;

                    if (!LoadDeathDiagnosticUtility.ShouldTracePawn(pawn)
                        && !(__state != null && __state.deadBefore == false && deadAfter == true))
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "Health.NotifyHediffChanged.Exit",
                        pawn,
                        "DEAD_BEFORE=" + (__state?.deadBefore.ToString() ?? "?")
                        + " DEAD_AFTER=" + deadAfter
                        + " CONS_BEFORE=" + (consBefore?.ToString("F4") ?? "null")
                        + " CONS_AFTER=" + consAfter.ToString("F4")
                        + " | SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(pawn),
                        false);

                    if (__state != null && __state.deadBefore == false && deadAfter == true)
                    {
                        LoadDeathDiagnosticUtility.ReportFirstDeadTransition(
                            __state.methodName ?? "Pawn_HealthTracker.Notify_HediffChanged",
                            pawn,
                            __state.triggeringHediffDefName,
                            __state.deadBefore,
                            deadAfter,
                            consBefore,
                            consAfter,
                            "Notify_HediffChanged 调用后存活->死亡。");
                    }
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        // ===== Pawn.Kill =====

        [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
        internal static class PawnKillPatch
        {
            private static void Prefix(Pawn __instance, DamageInfo? dinfo, Hediff exactCulprit)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    Pawn pawn = __instance;
                    // 无条件关注：Kill 必然是关键入口。
                    LoadDeathDiagnosticUtility.ShouldTracePawn(pawn);

                    LoadDeathDiagnosticUtility.Write(
                        "Pawn.Kill.Enter",
                        pawn,
                        "DAMAGE=" + LoadDeathDiagnosticUtility.FormatDamageInfo(
                            new object[] { dinfo! })
                        + " EXACT_CULPRIT=" + (exactCulprit?.def?.defName ?? "null")
                        + " | MECHANITOR_RECORD="
                            + LoadDeathDiagnosticUtility.BuildMechanitorRecordSnapshot(pawn)
                        + " | DATAPROC="
                            + LoadDeathDiagnosticUtility.BuildDataProcessingSnapshot(pawn)
                        + " | ALL_HEDIFFS="
                            + LoadDeathDiagnosticUtility.BuildHediffList(pawn, "KILL")
                        + " | SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(pawn),
                        true);
                }
                catch
                {
                    // 忽略。不得阻止或延迟 Kill。
                }
            }

            private static void Postfix(Pawn __instance)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    Pawn pawn = __instance;
                    LoadDeathDiagnosticUtility.Write(
                        "Pawn.Kill.Exit",
                        pawn,
                        "DEAD=" + pawn.Dead
                        + " DESTROYED=" + pawn.Destroyed
                        + " HAS_CORPSE=" + (pawn.Corpse != null)
                        + " MAP=" + (pawn.Map?.uniqueID.ToString() ?? "null")
                        + " POS=" + (pawn.Position.IsValid ? pawn.Position.ToString() : "Invalid")
                        + " | SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(pawn),
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        // ===== Pawn.SpawnSetup =====

        [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
        internal static class PawnSpawnSetupPatch
        {
            private static void Prefix(Pawn __instance, Map map, bool respawningAfterLoad)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    Pawn pawn = __instance;
                    // 已关注、或机械体在 SpawnSetup 前已经 Dead 时都记录（用于确认其他同类受害者）。
                    bool isWatched = LoadDeathDiagnosticUtility.ShouldTracePawn(pawn);
                    bool mechanoidDeadBeforeSpawn =
                        pawn.RaceProps?.IsMechanoid == true && pawn.Dead;
                    if (!isWatched && !mechanoidDeadBeforeSpawn)
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "Pawn.SpawnSetup.Enter",
                        pawn,
                        "RESPAWNING_AFTER_LOAD=" + respawningAfterLoad
                        + " MAP_ID=" + (map?.uniqueID.ToString() ?? "null")
                        + " DEAD_BEFORE_SPAWN_SETUP=" + pawn.Dead
                        + " | SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(pawn),
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
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    Pawn pawn = __instance;
                    if (!LoadDeathDiagnosticUtility.ShouldTracePawn(pawn)
                        && !(pawn.RaceProps?.IsMechanoid == true && pawn.Dead))
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "Pawn.SpawnSetup.Exit",
                        pawn,
                        "RESPAWNING_AFTER_LOAD=" + respawningAfterLoad
                        + " MAP_ID=" + (map?.uniqueID.ToString() ?? "null")
                        + " DEAD=" + pawn.Dead
                        + " | SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(pawn),
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }
        }
    }
}
