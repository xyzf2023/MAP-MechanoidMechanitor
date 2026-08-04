using System.Reflection;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械族机械师注册表与身份组件诊断补丁：仅记录，不修改任何逻辑。
    /// </summary>
    internal static class LoadDeathMechanitorDiagnosticPatches
    {
        // ===== CompNativeMechanoidMechanitor.PostExposeData =====

        [HarmonyPatch(
            typeof(CompNativeMechanoidMechanitor),
            nameof(CompNativeMechanoidMechanitor.PostExposeData))]
        internal static class NativeCompPostExposeDataPatch
        {
            private static void Prefix(CompNativeMechanoidMechanitor __instance)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    if (Scribe.mode != LoadSaveMode.PostLoadInit)
                    {
                        return;
                    }

                    Pawn? pawn = __instance.parent as Pawn;
                    if (pawn == null || !LoadDeathDiagnosticUtility.ShouldTracePawn(pawn))
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "NativeComp.PostExposeData.Enter",
                        pawn,
                        "RECORD_BEFORE="
                            + LoadDeathDiagnosticUtility.BuildMechanitorRecordSnapshot(pawn)
                        + " IDENTITY_HEDIFFS_BEFORE="
                            + LoadDeathDiagnosticUtility.BuildHediffList(pawn, "IDENTITY_BEFORE")
                        + " DEAD=" + pawn.Dead
                        + " | SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(pawn),
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }

            private static void Postfix(CompNativeMechanoidMechanitor __instance)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    if (Scribe.mode != LoadSaveMode.PostLoadInit)
                    {
                        return;
                    }

                    Pawn? pawn = __instance.parent as Pawn;
                    if (pawn == null || !LoadDeathDiagnosticUtility.ShouldTracePawn(pawn))
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "NativeComp.PostExposeData.Exit",
                        pawn,
                        "RECORD_AFTER="
                            + LoadDeathDiagnosticUtility.BuildMechanitorRecordSnapshot(pawn)
                        + " IDENTITY_HEDIFFS_AFTER="
                            + LoadDeathDiagnosticUtility.BuildHediffList(pawn, "IDENTITY_AFTER")
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

        // ===== EnsureNativeMechanitorRecord =====

        [HarmonyPatch(
            typeof(GameComponent_MechanoidMechanitorRegistry),
            nameof(GameComponent_MechanoidMechanitorRegistry.EnsureNativeMechanitorRecord))]
        internal static class EnsureNativeMechanitorRecordPatch
        {
            private sealed class State
            {
                public bool hadRecordBefore;
                public string? originBefore;
                public string? identityHediffsBefore;
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
                    if (pawn == null)
                    {
                        return;
                    }

                    __state.hadRecordBefore =
                        GameComponent_MechanoidMechanitorRegistry
                            .TryGetMechanitorRecord(pawn, out MechanoidMechanitorRecord? before);
                    __state.originBefore = before?.Origin.ToString();
                    __state.identityHediffsBefore =
                        LoadDeathDiagnosticUtility.BuildHediffList(pawn, "IDENTITY_BEFORE");
                    LoadDeathDiagnosticUtility.TryGetConsciousness(pawn, out float cons);
                    __state.consciousnessBefore = cons;
                    __state.deadBefore = pawn.Dead;

                    if (!LoadDeathDiagnosticUtility.ShouldTracePawn(pawn))
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "MechanitorRegistry.EnsureNative.Enter",
                        pawn,
                        "HAD_RECORD_BEFORE=" + __state.hadRecordBefore
                        + " ORIGIN_BEFORE=" + (__state.originBefore ?? "null")
                        + " IDENTITY_HEDIFFS_BEFORE=" + __state.identityHediffsBefore
                        + " | SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(pawn),
                        true);
                }
                catch
                {
                    // 忽略。
                }
            }

            private static void Postfix(Pawn? pawn, bool __result, ref State __state)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    if (pawn == null)
                    {
                        return;
                    }

                    GameComponent_MechanoidMechanitorRegistry
                        .TryGetMechanitorRecord(pawn, out MechanoidMechanitorRecord? after);
                    string originAfter = after?.Origin.ToString() ?? "null";
                    string identityHediffsAfter =
                        LoadDeathDiagnosticUtility.BuildHediffList(pawn, "IDENTITY_AFTER");
                    LoadDeathDiagnosticUtility.TryGetConsciousness(pawn, out float consAfter);
                    bool deadAfter = pawn.Dead;

                    bool transition = __state != null && __state.deadBefore == false
                        && deadAfter == true;

                    if (!LoadDeathDiagnosticUtility.ShouldTracePawn(pawn) && !transition)
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "MechanitorRegistry.EnsureNative.Exit",
                        pawn,
                        "RETURN=" + __result
                        + " HAD_RECORD_BEFORE=" + (__state?.hadRecordBefore.ToString() ?? "?")
                        + " ORIGIN_BEFORE=" + (__state?.originBefore ?? "null")
                        + " ORIGIN_AFTER=" + originAfter
                        + " IDENTITY_HEDIFFS_BEFORE=" + (__state?.identityHediffsBefore ?? "?")
                        + " IDENTITY_HEDIFFS_AFTER=" + identityHediffsAfter
                        + " DEAD_TRANSITION=" + transition
                        + " CONS_BEFORE=" + (__state?.consciousnessBefore?.ToString("F4") ?? "null")
                        + " CONS_AFTER=" + consAfter.ToString("F4")
                        + " | SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(pawn),
                        false);

                    if (transition && __state != null)
                    {
                        LoadDeathDiagnosticUtility.ReportFirstDeadTransition(
                            "GameComponent_MechanoidMechanitorRegistry.EnsureNativeMechanitorRecord",
                            pawn,
                            null,
                            __state.deadBefore,
                            deadAfter,
                            __state.consciousnessBefore,
                            consAfter,
                            "EnsureNativeMechanitorRecord 调用后存活->死亡。");
                    }
                }
                catch
                {
                    // 忽略。
                }
            }
        }
    }
}
