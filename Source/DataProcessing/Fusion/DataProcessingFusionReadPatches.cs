using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 兼容数据处理模块现有的注册表、仪表盘和生命周期修正层。
    /// 只替换下列明确列出的消费者内部的两个读取调用；不修改原版方法本身，
    /// 不改变带宽、控制组、Job 或真实监管关系。新增消费者请直接使用 Resolver。
    /// </summary>
    [HarmonyPatch]
    internal static class DataProcessingFusionReadPatches
    {
        private static readonly MethodInfo ActualOverseer = AccessTools.Method(
            typeof(MechanitorUtility), nameof(MechanitorUtility.GetOverseer),
            new[] { typeof(Pawn) });
        private static readonly MethodInfo ActualSubjects = AccessTools.PropertyGetter(
            typeof(Pawn_MechanitorTracker), nameof(Pawn_MechanitorTracker.OverseenPawns));
        private static readonly MethodInfo AllocationOverseer = AccessTools.Method(
            typeof(DataProcessingOverseerResolver),
            nameof(DataProcessingOverseerResolver.GetAllocationOverseer));
        private static readonly MethodInfo AllocationSubjects = AccessTools.Method(
            typeof(DataProcessingOverseerResolver),
            nameof(DataProcessingOverseerResolver.GetAllocationSubjects));

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return Require(typeof(DataProcessingAllocationUtility), "IsValidAllocationPair");
            yield return Require(typeof(GameComponent_DataProcessingAllocationRegistry),
                "CollectDynamicAllocationTargets");
            yield return Require(typeof(GameComponent_DataProcessingAllocationRegistry),
                "FindOverseerGoverningTarget");
            yield return Require(typeof(DataProcessingPawnLifecycleCoordinator), "CollectCurrentTargets");
            yield return Require(typeof(DataProcessingPawnLifecycleCoordinator), "ResolveCurrentOverseer");
            yield return Require(typeof(DataProcessingDraftStateRefreshQueue), "ResolveCurrentOverseer");
            yield return Require(typeof(DataProcessingDormantFixedAllocationRecoveryQueue),
                "ResolveCurrentOverseer");
            yield return Require(typeof(DataProcessingDormantPinCleanupPatch),
                "ResolveCurrentExternalOverseer");
            yield return Require(typeof(DataProcessingDormantSpecializationCleanupPatch),
                "ResolveCurrentOverseer");
            yield return Require(typeof(DataProcessingDeathSafeTargetClearPatch), "ResolveCurrentOverseer");
            yield return Require(typeof(DataProcessingDefaultTemplateAndCopyFixUtility),
                "CollectActiveMechanoids");

            // UI 的编译器闭包也可能持有读取调用；数据处理窗口已全部合并到
            // Dialog_DataProcessingAllocationDashboard 这一分部类，反射其单一 CLR 类型即可覆盖全部 UI 部分。
            foreach (Type type in new[]
            {
                typeof(Dialog_DataProcessingAllocationDashboard)
            })
            {
                foreach (MethodBase method in DeclaredMethodsAndClosures(type))
                    yield return method;
            }
        }

        internal static MethodInfo Require(Type type, string name)
        {
            MethodInfo? found = null;
            foreach (MethodInfo method in type.GetMethods(
                         BindingFlags.Public | BindingFlags.NonPublic
                         | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.Name != name) continue;
                if (found != null) throw new AmbiguousMatchException(type.FullName + "." + name);
                found = method;
            }
            return found ?? throw new MissingMethodException(type.FullName, name);
        }

        private static IEnumerable<MethodBase> DeclaredMethodsAndClosures(Type type)
        {
            foreach (MethodInfo method in type.GetMethods(
                         BindingFlags.Public | BindingFlags.NonPublic
                         | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.ContainsGenericParameters || method.GetMethodBody() == null) continue;
                // 仅在启动安装时读取 IL，未调用这两个入口的 UI 方法不安装补丁。
                foreach (KeyValuePair<OpCode, object> instruction in PatchProcessor.ReadMethodBody(method))
                {
                    if ((instruction.Key == OpCodes.Call || instruction.Key == OpCodes.Callvirt)
                        && (Equals(instruction.Value, ActualOverseer)
                            || Equals(instruction.Value, ActualSubjects)))
                    {
                        yield return method;
                        break;
                    }
                }
            }
            foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                foreach (MethodBase method in DeclaredMethodsAndClosures(nested))
                    yield return method;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(ActualOverseer))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AllocationOverseer;
                }
                else if (instruction.Calls(ActualSubjects))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AllocationSubjects;
                }
                // 保留指令原有的 labels 和 exception blocks。
                yield return instruction;
            }
        }
    }

    /// <summary>
    /// 跨组件读档时，只把破坏性清理延后到 LoadedGame；不跳过正常运行期清理。
    /// LoadedGame 已处于 Inactive，此时完整会话引用可经 Resolver 直接查询。
    /// </summary>
    [HarmonyPatch]
    internal static class DataProcessingFusionPostLoadGuardPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[]
            {
                "CleanupInvalidRecords", "CleanupInvalidPinRecords",
                "CleanupInvalidSpecializationRecords", "CleanupInvalidDynamicTargetRecords",
                "CleanupInvalidDynamicAllocationRecords"
            })
                yield return DataProcessingFusionReadPatches.Require(
                    typeof(GameComponent_DataProcessingAllocationRegistry), name);
        }

        [HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            return Scribe.mode != LoadSaveMode.PostLoadInit
                || !GameComponent_MechFusionSessionRegistry.HasAnySession;
        }
    }
}
