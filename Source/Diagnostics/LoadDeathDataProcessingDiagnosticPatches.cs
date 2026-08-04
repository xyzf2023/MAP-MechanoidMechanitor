using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 数据处理注册表与生命周期协调器诊断补丁：仅记录，不修改任何逻辑，
    /// 不改变 ClearTarget / SuspendOverseerRuntime 的返回值与执行顺序。
    /// </summary>
    internal static class LoadDeathDataProcessingDiagnosticPatches
    {
        // 使用中性的低优先级：仅保证诊断补丁不抢在现有补丁之前，不改变任何既有补丁优先级。
        private const int ClearTargetPrefixPriority = Priority.Low;
        private const int ClearTargetPostfixPriority = Priority.Low;

        // ===== GameComponent_DataProcessingAllocationRegistry.ExposeData =====

        [HarmonyPatch(
            typeof(GameComponent_DataProcessingAllocationRegistry),
            nameof(GameComponent_DataProcessingAllocationRegistry.ExposeData))]
        internal static class DataRegistryExposeDataPatch
        {
            private static void Prefix(GameComponent_DataProcessingAllocationRegistry __instance)
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

                    LoadDeathDiagnosticUtility.Write(
                        "DataRegistry.ExposeData.Enter",
                        null,
                        "SCRIBE=" + Scribe.mode
                        + " " + SummarizeRegistry(__instance, "正义", "刃"),
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }

            private static void Postfix(GameComponent_DataProcessingAllocationRegistry __instance)
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

                    LoadDeathDiagnosticUtility.Write(
                        "DataRegistry.ExposeData.Exit",
                        null,
                        "SCRIBE=" + Scribe.mode
                        + " " + SummarizeRegistry(__instance, "正义", "刃"),
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        // ===== GameComponent_DataProcessingAllocationRegistry.LoadedGame =====

        [HarmonyPatch(
            typeof(GameComponent_DataProcessingAllocationRegistry),
            nameof(GameComponent_DataProcessingAllocationRegistry.LoadedGame))]
        internal static class DataRegistryLoadedGamePatch
        {
            private static void Prefix(GameComponent_DataProcessingAllocationRegistry __instance)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "DataRegistry.LoadedGame.Enter",
                        null,
                        "PROGRAM=" + SafeProgramState()
                        + " " + SummarizeRegistry(__instance, "正义", "刃"),
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }

            private static void Postfix(GameComponent_DataProcessingAllocationRegistry __instance)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "DataRegistry.LoadedGame.Exit",
                        null,
                        "PROGRAM=" + SafeProgramState()
                        + " " + SummarizeRegistry(__instance, "正义", "刃"),
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        // ===== ClearTarget =====

        [HarmonyPatch(
            typeof(GameComponent_DataProcessingAllocationRegistry),
            nameof(GameComponent_DataProcessingAllocationRegistry.ClearTarget),
            new[] { typeof(Pawn) })]
        [HarmonyPriority(ClearTargetPrefixPriority)]
        internal static class DataRegistryClearTargetPatch
        {
            private static void Prefix(
                GameComponent_DataProcessingAllocationRegistry __instance,
                Pawn? target)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "DataRegistry.ClearTarget.Enter",
                        target,
                        "SCRIBE=" + Scribe.mode
                        + " HAS_DYNAMIC_CONFIG=" + HasDynamicConfig(__instance, target)
                        + " HAS_ACTUAL_ALLOCATION=" + HasActualAllocation(__instance, target)
                        + " CURRENT_COMMAND_FOCUS="
                            + LoadDeathDiagnosticUtility.BuildDataProcessingSnapshot(target)
                        + " TARGET_DEAD=" + (target?.Dead == true)
                        + " TARGET_DESTROYED=" + (target?.Destroyed == true)
                        + " | DIAGNOSTIC PREFIX ENTERED (执行顺序不假定)",
                        true);
                }
                catch
                {
                    // 忽略。
                }
            }

            [HarmonyPriority(ClearTargetPostfixPriority)]
            private static void Postfix(
                GameComponent_DataProcessingAllocationRegistry __instance,
                Pawn? target)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "DataRegistry.ClearTarget.Exit",
                        target,
                        "SCRIBE=" + Scribe.mode
                        + " " + LoadDeathDiagnosticUtility.BuildDataProcessingSnapshot(target),
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        // ===== SuspendOverseerRuntime =====

        [HarmonyPatch(
            typeof(DataProcessingPawnLifecycleCoordinator),
            nameof(DataProcessingPawnLifecycleCoordinator.SuspendOverseerRuntime))]
        internal static class LifecycleSuspendOverseerRuntimePatch
        {
            private static void Prefix(
                GameComponent_DataProcessingAllocationRegistry registry,
                Pawn? overseer)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "Lifecycle.SuspendOverseerRuntime.Enter",
                        overseer,
                        "SCRIBE=" + Scribe.mode
                        + " " + LoadDeathDiagnosticUtility.BuildDataProcessingSnapshot(overseer)
                        + " | SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(overseer),
                        true);
                }
                catch
                {
                    // 忽略。
                }
            }

            private static void Postfix(
                GameComponent_DataProcessingAllocationRegistry registry,
                Pawn? overseer)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "Lifecycle.SuspendOverseerRuntime.Exit",
                        overseer,
                        "SCRIBE=" + Scribe.mode
                        + " " + LoadDeathDiagnosticUtility.BuildDataProcessingSnapshot(overseer)
                        + " | SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(overseer),
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        // ===== 其他数据处理同步入口 =====

        [HarmonyPatch(
            typeof(GameComponent_DataProcessingAllocationRegistry),
            nameof(GameComponent_DataProcessingAllocationRegistry.SyncHediffForTarget),
            new[] { typeof(Pawn) })]
        internal static class DataRegistrySyncHediffForTargetPatch
        {
            private static void Prefix(
                GameComponent_DataProcessingAllocationRegistry __instance,
                Pawn? target)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    if (target != null && !LoadDeathDiagnosticUtility.ShouldTracePawn(target)
                        && Scribe.mode == LoadSaveMode.Inactive)
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "DataRegistry.SyncHediffForTarget.Enter",
                        target,
                        "SCRIBE=" + Scribe.mode
                        + " " + LoadDeathDiagnosticUtility.BuildDataProcessingSnapshot(target)
                        + " | SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(target),
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }

            private static void Postfix(
                GameComponent_DataProcessingAllocationRegistry __instance,
                Pawn? target)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    if (target != null && !LoadDeathDiagnosticUtility.ShouldTracePawn(target)
                        && Scribe.mode == LoadSaveMode.Inactive)
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "DataRegistry.SyncHediffForTarget.Exit",
                        target,
                        "SCRIBE=" + Scribe.mode
                        + " " + LoadDeathDiagnosticUtility.BuildDataProcessingSnapshot(target)
                        + " | SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(target),
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        [HarmonyPatch(
            typeof(GameComponent_DataProcessingAllocationRegistry),
            nameof(GameComponent_DataProcessingAllocationRegistry.SyncHediffsForOverseer),
            new[] { typeof(Pawn) })]
        internal static class DataRegistrySyncHediffsForOverseerPatch
        {
            private static void Prefix(
                GameComponent_DataProcessingAllocationRegistry __instance,
                Pawn? overseer)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    if (overseer != null && !LoadDeathDiagnosticUtility.ShouldTracePawn(overseer)
                        && Scribe.mode == LoadSaveMode.Inactive)
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "DataRegistry.SyncHediffsForOverseer.Enter",
                        overseer,
                        "SCRIBE=" + Scribe.mode
                        + " " + LoadDeathDiagnosticUtility.BuildDataProcessingSnapshot(overseer)
                        + " | SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(overseer),
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }

            private static void Postfix(
                GameComponent_DataProcessingAllocationRegistry __instance,
                Pawn? overseer)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    if (overseer != null && !LoadDeathDiagnosticUtility.ShouldTracePawn(overseer)
                        && Scribe.mode == LoadSaveMode.Inactive)
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "DataRegistry.SyncHediffsForOverseer.Exit",
                        overseer,
                        "SCRIBE=" + Scribe.mode
                        + " " + LoadDeathDiagnosticUtility.BuildDataProcessingSnapshot(overseer)
                        + " | SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(overseer),
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        [HarmonyPatch(
            typeof(GameComponent_DataProcessingAllocationRegistry),
            nameof(GameComponent_DataProcessingAllocationRegistry.ClearOverseer),
            new[] { typeof(Pawn) })]
        internal static class DataRegistryClearOverseerPatch
        {
            private static void Prefix(
                GameComponent_DataProcessingAllocationRegistry __instance,
                Pawn? overseer)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    if (overseer != null && !LoadDeathDiagnosticUtility.ShouldTracePawn(overseer)
                        && Scribe.mode == LoadSaveMode.Inactive)
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "DataRegistry.ClearOverseer.Enter",
                        overseer,
                        "SCRIBE=" + Scribe.mode
                        + " " + LoadDeathDiagnosticUtility.BuildDataProcessingSnapshot(overseer)
                        + " | SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(overseer),
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }

            private static void Postfix(
                GameComponent_DataProcessingAllocationRegistry __instance,
                Pawn? overseer)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    if (overseer != null && !LoadDeathDiagnosticUtility.ShouldTracePawn(overseer)
                        && Scribe.mode == LoadSaveMode.Inactive)
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "DataRegistry.ClearOverseer.Exit",
                        overseer,
                        "SCRIBE=" + Scribe.mode
                        + " " + LoadDeathDiagnosticUtility.BuildDataProcessingSnapshot(overseer)
                        + " | SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(overseer),
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        [HarmonyPatch(
            typeof(GameComponent_DataProcessingAllocationRegistry),
            nameof(GameComponent_DataProcessingAllocationRegistry.PrepareForExternalConsciousnessLoss),
            new[] { typeof(Pawn), typeof(float) })]
        internal static class DataRegistryPrepareForExternalConsciousnessLossPatch
        {
            private static void Prefix(
                GameComponent_DataProcessingAllocationRegistry __instance,
                Pawn? overseer,
                float consciousnessOffsetLoss)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    if (overseer != null && !LoadDeathDiagnosticUtility.ShouldTracePawn(overseer)
                        && Scribe.mode == LoadSaveMode.Inactive)
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "DataRegistry.PrepareForExternalConsciousnessLoss.Enter",
                        overseer,
                        "OFFSET_LOSS=" + consciousnessOffsetLoss.ToString("F4")
                        + " SCRIBE=" + Scribe.mode
                        + " " + LoadDeathDiagnosticUtility.BuildDataProcessingSnapshot(overseer)
                        + " | SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(overseer),
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }

            private static void Postfix(
                GameComponent_DataProcessingAllocationRegistry __instance,
                Pawn? overseer,
                float consciousnessOffsetLoss)
            {
                try
                {
                    if (!LoadDeathDiagnosticUtility.Enabled)
                    {
                        return;
                    }

                    if (overseer != null && !LoadDeathDiagnosticUtility.ShouldTracePawn(overseer)
                        && Scribe.mode == LoadSaveMode.Inactive)
                    {
                        return;
                    }

                    LoadDeathDiagnosticUtility.Write(
                        "DataRegistry.PrepareForExternalConsciousnessLoss.Exit",
                        overseer,
                        "OFFSET_LOSS=" + consciousnessOffsetLoss.ToString("F4")
                        + " SCRIBE=" + Scribe.mode
                        + " " + LoadDeathDiagnosticUtility.BuildDataProcessingSnapshot(overseer)
                        + " | SNAPSHOT=" + LoadDeathDiagnosticUtility.BuildPawnSnapshot(overseer),
                        false);
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        // ===== 只读辅助 =====

        private static string SummarizeRegistry(
            GameComponent_DataProcessingAllocationRegistry? registry,
            string nameA,
            string nameB)
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                if (registry == null)
                {
                    return "REGISTRY=null";
                }

                // 通过反射只读读取内部集合数量。
                int actual = ReadCount(registry, "records");
                int dynamicTargets = ReadCount(registry, "dynamicTargetRecords");
                int specs = ReadCount(registry, "specializationRecords");
                sb.Append("ACTUAL_ALLOC_COUNT=").Append(actual);
                sb.Append(' ');
                sb.Append("DYNAMIC_TARGET_COUNT=").Append(dynamicTargets);
                sb.Append(' ');
                sb.Append("SPEC_COUNT=").Append(specs);
            }
            catch (Exception ex)
            {
                sb.Append("<error:").Append(ex.GetType().Name).Append('>');
            }

            try
            {
                sb.Append(' ');
                sb.Append("RELATED_TO_WATCHED=")
                    .Append(BuildWatchedRelatedSnapshot(registry));
            }
            catch (Exception ex)
            {
                sb.Append("<error:").Append(ex.GetType().Name).Append('>');
            }

            return sb.ToString();
        }

        private static int ReadCount(object registry, string fieldName)
        {
            try
            {
                System.Reflection.FieldInfo? field = AccessTools.Field(
                    registry.GetType(), fieldName);
                object? value = field?.GetValue(registry);
                if (value is System.Collections.ICollection collection)
                {
                    return collection.Count;
                }
            }
            catch
            {
                // 忽略。
            }

            return -1;
        }

        private static string BuildWatchedRelatedSnapshot(
            GameComponent_DataProcessingAllocationRegistry? registry)
        {
            // 仅记录与“正义/刃/关注集合”相关的记录；不扫描全部世界 Pawn。
            StringBuilder sb = new StringBuilder();
            try
            {
                if (registry == null)
                {
                    return "null";
                }

                List<Pawn> watched = CollectWatchedPawns();
                if (watched.Count == 0)
                {
                    return "none";
                }

                foreach (Pawn pawn in watched)
                {
                    sb.Append('{');
                    sb.Append(LoadDeathDiagnosticUtility.SafeThingId(pawn));
                    sb.Append(":targetSteps=").Append(registry.GetStepsForTarget(pawn));
                    sb.Append(",overseerSteps=").Append(registry.GetTotalStepsForOverseer(pawn));
                    sb.Append('}');
                }
            }
            catch (Exception ex)
            {
                sb.Append("<error:").Append(ex.GetType().Name).Append('>');
            }

            return sb.ToString();
        }

        private static List<Pawn> CollectWatchedPawns()
        {
            List<Pawn> result = new List<Pawn>();
            try
            {
                // 通过当前已关注集合无法直接持有 Pawn 引用（只存 ThingID），
                // 退而求其次：在加载阶段使用“正义/刃”名称与注册表已加载记录判断。
                // 注意：CurrentRegistry 是注册表内部私有属性，此处改用其对外公开的
                // RegisteredMechanitors 只读属性，避免访问受保护成员。
                foreach (Pawn p in GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors)
                {
                    if (p != null)
                    {
                        result.Add(p);
                    }
                }
            }
            catch
            {
                // 忽略。
            }

            return result;
        }

        private static bool HasDynamicConfig(
            GameComponent_DataProcessingAllocationRegistry? registry,
            Pawn? target)
        {
            try
            {
                if (registry == null || target == null)
                {
                    return false;
                }

                return registry.GetDynamicTargetRecord(null, target) != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasActualAllocation(
            GameComponent_DataProcessingAllocationRegistry? registry,
            Pawn? target)
        {
            try
            {
                if (registry == null || target == null)
                {
                    return false;
                }

                return registry.GetStepsForTarget(target) > 0;
            }
            catch
            {
                return false;
            }
        }

        private static string SafeProgramState()
        {
            try
            {
                return Current.ProgramState.ToString();
            }
            catch
            {
                return "<error>";
            }
        }
    }
}
