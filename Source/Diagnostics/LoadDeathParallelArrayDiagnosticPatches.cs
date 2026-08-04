using System.Reflection;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 并行思维阵列与动态意识诊断补丁：仅记录，不修改任何逻辑，
    /// 不调用 ReevaluateOperatingState / RefreshDynamicEffects 等方法来获取结果。
    /// </summary>
    internal static class LoadDeathParallelArrayDiagnosticPatches
    {
        // ===== CompParallelThoughtArray.PostExposeData =====

        [HarmonyPatch(
            typeof(CompParallelThoughtArray),
            nameof(CompParallelThoughtArray.PostExposeData))]
        internal static class ParallelArrayPostExposeDataPatch
        {
            private static void Prefix(CompParallelThoughtArray __instance)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    Pawn? target = __instance.Target;
                    if (target == null && !LoadDeathDiagnosticUtility.ShouldTracePawn(target))
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "ParallelArray.PostExposeData.Enter",
                        target,
                        "SCRIBE=" + Scribe.mode
                        + " ARRAY=" + LoadDeathDiagnosticUtility.BuildParallelThoughtArraySnapshot(__instance)
                        + " TARGET_SNAPSHOT="
                            + LoadDeathDiagnosticUtility.BuildPawnSnapshot(target),
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
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    Pawn? target = __instance.Target;
                    if (target == null && !LoadDeathDiagnosticUtility.ShouldTracePawn(target))
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "ParallelArray.PostExposeData.Exit",
                        target,
                        "SCRIBE=" + Scribe.mode
                        + " ARRAY=" + LoadDeathDiagnosticUtility.BuildParallelThoughtArraySnapshot(__instance)
                        + " TARGET_SNAPSHOT="
                            + LoadDeathDiagnosticUtility.BuildPawnSnapshot(target),
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        // ===== CompParallelThoughtArray.PostSpawnSetup =====

        [HarmonyPatch(
            typeof(CompParallelThoughtArray),
            nameof(CompParallelThoughtArray.PostSpawnSetup))]
        internal static class ParallelArrayPostSpawnSetupPatch
        {
            private sealed class State
            {
                public float? consciousnessBefore;
                public bool deadBefore;
            }

            private static void Prefix(
                CompParallelThoughtArray __instance,
                bool respawningAfterLoad,
                ref State __state)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    Pawn? target = __instance.Target;
                    __state = new State();
                    if (target != null)
                    {
                        LoadDeathDiagnosticUtility.TryGetConsciousness(
                            target, out float cons);
                        __state.consciousnessBefore = cons;
                        __state.deadBefore = target.Dead;
                    }

                    if (target == null && !LoadDeathDiagnosticUtility.ShouldTracePawn(target))
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "ParallelArray.PostSpawnSetup.Enter",
                        target,
                        "RESPAWNING_AFTER_LOAD=" + respawningAfterLoad
                        + " ARRAY=" + LoadDeathDiagnosticUtility.BuildParallelThoughtArraySnapshot(__instance)
                        + " TARGET_SNAPSHOT="
                            + LoadDeathDiagnosticUtility.BuildPawnSnapshot(target),
                        true);
                }
                catch
                {
                    // 忽略。
                }
            }

            private static void Postfix(
                CompParallelThoughtArray __instance,
                bool respawningAfterLoad,
                ref State __state)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    Pawn? target = __instance.Target;
                    if (target == null && !LoadDeathDiagnosticUtility.ShouldTracePawn(target))
                    {
                        return;
                    }

                    float? consBefore = __state?.consciousnessBefore;
                    LoadDeathDiagnosticUtility.TryGetConsciousness(target, out float consAfter);
                    bool deadAfter = target?.Dead == true;

                    LoadDeathDiagnosticUtility.Write(
                        "ParallelArray.PostSpawnSetup.Exit",
                        target,
                        "RESPAWNING_AFTER_LOAD=" + respawningAfterLoad
                        + " DEAD_BEFORE=" + (__state?.deadBefore.ToString() ?? "?")
                        + " DEAD_AFTER=" + deadAfter
                        + " CONS_BEFORE=" + (consBefore?.ToString("F4") ?? "null")
                        + " CONS_AFTER=" + consAfter.ToString("F4")
                        + " ARRAY=" + LoadDeathDiagnosticUtility.BuildParallelThoughtArraySnapshot(__instance)
                        + " TARGET_SNAPSHOT="
                            + LoadDeathDiagnosticUtility.BuildPawnSnapshot(target),
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        // ===== CompParallelThoughtArray.ReevaluateOperatingState =====

        [HarmonyPatch(
            typeof(CompParallelThoughtArray),
            nameof(CompParallelThoughtArray.ReevaluateOperatingState))]
        internal static class ParallelArrayReevaluatePatch
        {
            private sealed class State
            {
                public float? consciousnessBefore;
                public bool deadBefore;
            }

            private static void Prefix(
                CompParallelThoughtArray __instance,
                bool forceRefresh,
                ref State __state)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    Pawn? target = __instance.Target;
                    __state = new State();
                    if (target != null)
                    {
                        LoadDeathDiagnosticUtility.TryGetConsciousness(
                            target, out float cons);
                        __state.consciousnessBefore = cons;
                        __state.deadBefore = target.Dead;
                    }

                    if (target == null && !LoadDeathDiagnosticUtility.ShouldTracePawn(target))
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "ParallelArray.Reevaluate.Enter",
                        target,
                        "FORCE_REFRESH=" + forceRefresh
                        + " ARRAY=" + LoadDeathDiagnosticUtility.BuildParallelThoughtArraySnapshot(__instance)
                        + " TARGET_SNAPSHOT="
                            + LoadDeathDiagnosticUtility.BuildPawnSnapshot(target),
                        true);
                }
                catch
                {
                    // 忽略。
                }
            }

            private static void Postfix(
                CompParallelThoughtArray __instance,
                bool forceRefresh,
                ref State __state)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    Pawn? target = __instance.Target;
                    if (target == null && !LoadDeathDiagnosticUtility.ShouldTracePawn(target))
                    {
                        return;
                    }

                    float? consBefore = __state?.consciousnessBefore;
                    LoadDeathDiagnosticUtility.TryGetConsciousness(target, out float consAfter);
                    bool deadAfter = target?.Dead == true;

                    LoadDeathDiagnosticUtility.Write(
                        "ParallelArray.Reevaluate.Exit",
                        target,
                        "FORCE_REFRESH=" + forceRefresh
                        + " DEAD_BEFORE=" + (__state?.deadBefore.ToString() ?? "?")
                        + " DEAD_AFTER=" + deadAfter
                        + " CONS_BEFORE=" + (consBefore?.ToString("F4") ?? "null")
                        + " CONS_AFTER=" + consAfter.ToString("F4")
                        + " ARRAY=" + LoadDeathDiagnosticUtility.BuildParallelThoughtArraySnapshot(__instance)
                        + " TARGET_SNAPSHOT="
                            + LoadDeathDiagnosticUtility.BuildPawnSnapshot(target),
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        // ===== ParallelThoughtArrayUtility.RefreshTargetDynamicConsciousness =====

        [HarmonyPatch(
            typeof(ParallelThoughtArrayUtility),
            nameof(ParallelThoughtArrayUtility.RefreshTargetDynamicConsciousness))]
        internal static class ParallelArrayRefreshTargetConsciousnessPatch
        {
            private sealed class State
            {
                public float? consciousnessBefore;
                public bool deadBefore;
            }

            private static void Prefix(Pawn? target, ref State __state)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    __state = new State();
                    if (target != null)
                    {
                        LoadDeathDiagnosticUtility.TryGetConsciousness(
                            target, out float cons);
                        __state.consciousnessBefore = cons;
                        __state.deadBefore = target.Dead;
                    }

                    if (target == null && !LoadDeathDiagnosticUtility.ShouldTracePawn(target))
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "ParallelArray.RefreshTargetConsciousness.Enter",
                        target,
                        "SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(target),
                        true);
                }
                catch
                {
                    // 忽略。
                }
            }

            private static void Postfix(Pawn? target, ref State __state)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    if (target == null && !LoadDeathDiagnosticUtility.ShouldTracePawn(target))
                    {
                        return;
                    }

                    float? consBefore = __state?.consciousnessBefore;
                    LoadDeathDiagnosticUtility.TryGetConsciousness(target, out float consAfter);
                    bool deadAfter = target?.Dead == true;

                    LoadDeathDiagnosticUtility.Write(
                        "ParallelArray.RefreshTargetConsciousness.Exit",
                        target,
                        "DEAD_BEFORE=" + (__state?.deadBefore.ToString() ?? "?")
                        + " DEAD_AFTER=" + deadAfter
                        + " CONS_BEFORE=" + (consBefore?.ToString("F4") ?? "null")
                        + " CONS_AFTER=" + consAfter.ToString("F4")
                        + " SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(target),
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        // ===== DynamicConsciousnessBonusUtility.RefreshForPawn =====

        [HarmonyPatch(
            typeof(DynamicConsciousnessBonusUtility),
            nameof(DynamicConsciousnessBonusUtility.RefreshForPawn))]
        internal static class DynamicConsciousnessRefreshForPawnPatch
        {
            private sealed class State
            {
                public float? consciousnessBefore;
                public bool deadBefore;
            }

            private static void Prefix(Pawn? pawn, ref State __state)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    __state = new State();
                    if (pawn != null)
                    {
                        LoadDeathDiagnosticUtility.TryGetConsciousness(
                            pawn, out float cons);
                        __state.consciousnessBefore = cons;
                        __state.deadBefore = pawn.Dead;
                    }

                    if (pawn == null && !LoadDeathDiagnosticUtility.ShouldTracePawn(pawn))
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "DynamicConsciousness.RefreshForPawn.Enter",
                        pawn,
                        "SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(pawn),
                        true);
                }
                catch
                {
                    // 忽略。
                }
            }

            private static void Postfix(Pawn? pawn, ref State __state)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    if (pawn == null && !LoadDeathDiagnosticUtility.ShouldTracePawn(pawn))
                    {
                        return;
                    }

                    float? consBefore = __state?.consciousnessBefore;
                    LoadDeathDiagnosticUtility.TryGetConsciousness(pawn, out float consAfter);
                    bool deadAfter = pawn?.Dead == true;

                    LoadDeathDiagnosticUtility.Write(
                        "DynamicConsciousness.RefreshForPawn.Exit",
                        pawn,
                        "DEAD_BEFORE=" + (__state?.deadBefore.ToString() ?? "?")
                        + " DEAD_AFTER=" + deadAfter
                        + " CONS_BEFORE=" + (consBefore?.ToString("F4") ?? "null")
                        + " CONS_AFTER=" + consAfter.ToString("F4")
                        + " SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(pawn),
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        // ===== Hediff_DynamicConsciousnessBonusBase.RefreshDynamicEffects =====

        [HarmonyPatch(
            typeof(Hediff_DynamicConsciousnessBonusBase),
            nameof(Hediff_DynamicConsciousnessBonusBase.RefreshDynamicEffects))]
        internal static class DynamicConsciousnessRefreshHediffPatch
        {
            private static void Prefix(
                Hediff_DynamicConsciousnessBonusBase __instance,
                bool notifyHealth,
                ref float? __state)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    Pawn? pawn = __instance.pawn;
                    if (pawn != null)
                    {
                        LoadDeathDiagnosticUtility.TryGetConsciousness(
                            pawn, out float cons);
                        __state = cons;
                    }

                    if (pawn == null && !LoadDeathDiagnosticUtility.ShouldTracePawn(pawn))
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "DynamicConsciousness.RefreshHediff.Enter",
                        pawn,
                        "HEDIFF=" + (__instance.def?.defName ?? "null")
                        + " VARIANT=" + __instance.GetType().Name
                        + " NOTIFY_HEALTH=" + notifyHealth
                        + " SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(pawn),
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }

            private static void Postfix(
                Hediff_DynamicConsciousnessBonusBase __instance,
                ref float? __state)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    Pawn? pawn = __instance.pawn;
                    if (pawn == null && !LoadDeathDiagnosticUtility.ShouldTracePawn(pawn))
                    {
                        return;
                    }

                    float? consBefore = __state;
                    LoadDeathDiagnosticUtility.TryGetConsciousness(pawn, out float consAfter);

                    LoadDeathDiagnosticUtility.Write(
                        "DynamicConsciousness.RefreshHediff.Exit",
                        pawn,
                        "HEDIFF=" + (__instance.def?.defName ?? "null")
                        + " OLD_CONS=" + (consBefore?.ToString("F4") ?? "null")
                        + " NEW_CONS=" + consAfter.ToString("F4")
                        + " DIFF=" + ((consBefore.HasValue)
                            ? (consAfter - consBefore.Value).ToString("F4")
                            : "null")
                        + " SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(pawn),
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
