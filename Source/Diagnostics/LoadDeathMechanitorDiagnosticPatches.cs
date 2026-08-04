using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械族机械师身份组件与注册表诊断补丁。仅保存被动快照。
    /// </summary>
    internal static class LoadDeathMechanitorDiagnosticPatches
    {
        [HarmonyPatch(
            typeof(CompNativeMechanoidMechanitor),
            nameof(CompNativeMechanoidMechanitor.PostExposeData))]
        internal static class NativeCompPostExposeDataPatch
        {
            private static void Prefix(
                CompNativeMechanoidMechanitor __instance,
                ref LoadDeathPawnState? __state)
            {
                __state = null;
                try
                {
                    if (!LoadDeathDiagnosticUtility.Active
                        || Scribe.mode != LoadSaveMode.PostLoadInit)
                    {
                        return;
                    }

                    Pawn? pawn = __instance.parent as Pawn;
                    if (!LoadDeathDiagnosticUtility.ShouldTracePawn(pawn))
                    {
                        return;
                    }

                    __state = LoadDeathDiagnosticUtility.CaptureState(
                        pawn,
                        "CompNativeMechanoidMechanitor.PostExposeData",
                        null,
                        true);
                    LoadDeathDiagnosticUtility.WriteWithStack(
                        "NativeComp.PostExposeData.Enter",
                        pawn,
                        "BEFORE={" + __state.passivePawnBefore + "}"
                        + " IDENTITY_HEDIFFS_BEFORE={" + __state.hediffsBefore + "}"
                        + " RECORD_BEFORE={" + __state.mechanitorBefore + "}"
                        + " DATAPROC_BEFORE={" + __state.dataProcessingBefore + "}",
                        __state.stackTrace);
                }
                catch
                {
                    // 忽略。
                }
            }

            private static void Postfix(
                CompNativeMechanoidMechanitor __instance,
                LoadDeathPawnState? __state)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Active
                        || Scribe.mode != LoadSaveMode.PostLoadInit)
                    {
                        return;
                    }

                    Pawn? pawn = __instance.parent as Pawn;
                    if (__state == null && !LoadDeathDiagnosticUtility.ShouldTracePawn(pawn))
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "NativeComp.PostExposeData.Exit",
                        pawn,
                        "DEAD_BEFORE=" + (__state?.deadBefore.ToString() ?? "unavailable")
                        + " DEAD_AFTER=" + LoadDeathDiagnosticUtility.SafeDead(pawn)
                        + " AFTER={" + LoadDeathDiagnosticUtility.BuildPassivePawnSnapshot(pawn) + "}"
                        + " IDENTITY_HEDIFFS_AFTER={" + LoadDeathDiagnosticUtility.BuildPassiveHediffList(pawn) + "}"
                        + " RECORD_AFTER={" + LoadDeathDiagnosticUtility.BuildMechanitorRecordSnapshot(pawn) + "}"
                        + " DATAPROC_AFTER={" + LoadDeathDiagnosticUtility.BuildDataProcessingSnapshot(pawn) + "}",
                        false);
                    LoadDeathDiagnosticUtility.ReportFirstDeadTransition(
                        __state,
                        pawn,
                        "CompNativeMechanoidMechanitor.PostExposeData",
                        null);
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        [HarmonyPatch(
            typeof(GameComponent_MechanoidMechanitorRegistry),
            nameof(GameComponent_MechanoidMechanitorRegistry.EnsureNativeMechanitorRecord),
            new[] { typeof(Pawn) })]
        internal static class EnsureNativeMechanitorRecordPatch
        {
            private static void Prefix(
                Pawn? pawn,
                ref LoadDeathPawnState? __state)
            {
                __state = null;
                try
                {
                    if (!LoadDeathDiagnosticUtility.Active
                        || !LoadDeathDiagnosticUtility.ShouldTracePawn(pawn))
                    {
                        return;
                    }

                    __state = LoadDeathDiagnosticUtility.CaptureState(
                        pawn,
                        "GameComponent_MechanoidMechanitorRegistry.EnsureNativeMechanitorRecord",
                        null,
                        true);
                    LoadDeathDiagnosticUtility.WriteWithStack(
                        "MechanitorRegistry.EnsureNative.Enter",
                        pawn,
                        "RECORD_BEFORE={" + __state.mechanitorBefore + "}"
                        + " IDENTITY_HEDIFFS_BEFORE={" + __state.hediffsBefore + "}"
                        + " PAWN_BEFORE={" + __state.passivePawnBefore + "}"
                        + " DATAPROC_BEFORE={" + __state.dataProcessingBefore + "}",
                        __state.stackTrace);
                }
                catch
                {
                    // 忽略。
                }
            }

            private static void Postfix(
                Pawn? pawn,
                bool __result,
                LoadDeathPawnState? __state)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Active || __state == null)
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "MechanitorRegistry.EnsureNative.Exit",
                        pawn,
                        "RETURN=" + __result
                        + " DEAD_BEFORE=" + __state.deadBefore
                        + " DEAD_AFTER=" + LoadDeathDiagnosticUtility.SafeDead(pawn)
                        + " RECORD_BEFORE={" + __state.mechanitorBefore + "}"
                        + " RECORD_AFTER={" + LoadDeathDiagnosticUtility.BuildMechanitorRecordSnapshot(pawn) + "}"
                        + " IDENTITY_HEDIFFS_BEFORE={" + __state.hediffsBefore + "}"
                        + " IDENTITY_HEDIFFS_AFTER={" + LoadDeathDiagnosticUtility.BuildPassiveHediffList(pawn) + "}"
                        + " PAWN_AFTER={" + LoadDeathDiagnosticUtility.BuildPassivePawnSnapshot(pawn) + "}"
                        + " DATAPROC_AFTER={" + LoadDeathDiagnosticUtility.BuildDataProcessingSnapshot(pawn) + "}",
                        false);
                    LoadDeathDiagnosticUtility.ReportFirstDeadTransition(
                        __state,
                        pawn,
                        "GameComponent_MechanoidMechanitorRegistry.EnsureNativeMechanitorRecord",
                        null);
                }
                catch
                {
                    // 忽略。
                }
            }
        }
    }
}
