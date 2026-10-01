using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.NewRatkin
{
    // 两项功能分别注册、安装和回滚；共享的仅是本 MOD 内部的单补丁安装流程。
    internal abstract class NewRatkinCompatibilityModule : IThirdPartyCompatibilityModule
    {
        public abstract string ModuleId { get; }
        public abstract string DisplayName { get; }
        public string PackageId => NewRatkinCompatibilityUtility.PackageId;
        protected abstract Type PatchType { get; }
        protected abstract string AppliedDetail { get; }
        protected abstract bool TryResolve(ModContentPack mod, out MethodInfo? target, out string failure);
        protected virtual void ResetConfiguration() { }

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            ModContentPack? mod = ThirdPartyCompatibilityTargetResolver.FindRunningMod(PackageId);
            if (mod == null)
                return ThirdPartyCompatibilityResult.CreateInactive(ModuleId, DisplayName, PackageId);

            MethodInfo? target = null;
            MethodInfo? patch = null;
            bool attempted = false;
            try
            {
                if (!TryResolve(mod, out target, out string failure) || target == null)
                {
                    ResetConfiguration();
                    return ThirdPartyCompatibilityResult.CreateTargetChanged(ModuleId, DisplayName, PackageId, failure);
                }

                patch = AccessTools.DeclaredMethod(PatchType, "Transpiler", new[] { typeof(IEnumerable<CodeInstruction>) });
                if (patch == null || !patch.IsStatic || patch.ReturnType != typeof(IEnumerable<CodeInstruction>))
                    throw new MissingMethodException(PatchType.FullName, "Transpiler");

                // 安装前使用原始 IL 核对锚点。Transpiler 安装时再次校验其他补丁处理后的指令。
                try
                {
                    object? rewritten = patch.Invoke(null, new object[] { PatchProcessor.GetOriginalInstructions(target) });
                    ((IEnumerable<CodeInstruction>)rewritten!).ToList();
                }
                catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
                {
                    ResetConfiguration();
                    return ThirdPartyCompatibilityResult.CreateTargetChanged(ModuleId, DisplayName, PackageId,
                        ex.InnerException!.Message, target);
                }

                attempted = true;
                harmony.Patch(target, transpiler: new HarmonyMethod(patch));
            }
            catch (Exception ex)
            {
                if (attempted && target != null && patch != null)
                {
                    try { harmony.Unpatch(target, patch); }
                    catch { /* 保留原始异常，只尝试撤销本模块自己的补丁。 */ }
                }
                ResetConfiguration();
                return ThirdPartyCompatibilityResult.CreateFailed(ModuleId, DisplayName, PackageId,
                    "鼠族游商兼容安装失败，已尝试回滚本模块补丁。", ex,
                    target == null ? Array.Empty<MethodBase>() : new MethodBase[] { target });
            }
            return ThirdPartyCompatibilityResult.CreateApplied(ModuleId, DisplayName, PackageId, AppliedDetail);
        }
    }

    internal sealed class NewRatkinWanderingTraderInteractionCompatibility : NewRatkinCompatibilityModule
    {
        public override string ModuleId => "NewRatkin.WanderingTrader.Interaction";
        public override string DisplayName => "鼠族游商：机械族机械师互动";
        protected override Type PatchType => typeof(NewRatkinWanderingTraderMenuPatch);
        protected override string AppliedDetail => "已扩展游商驻留和行进菜单的殖民者身份门槛，保留原有交互任务与限制。";

        protected override bool TryResolve(ModContentPack mod, out MethodInfo? target, out string failure)
        {
            target = null;
            const string typeName = "NewRatkin.LordToil_WanderingCaravanIdle";
            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(mod, typeName, out Type? type, out failure))
                return false;

            Type[] parameters = { typeof(Lord), typeof(Pawn), typeof(Pawn) };
            MethodInfo[] entries = type!.GetMethods(BindingFlags.Static | BindingFlags.Public
                    | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(m => m.Name == "GetFloatMenuOptionsForLeader"
                    && !m.IsGenericMethod
                    && m.ReturnType == typeof(IEnumerable<FloatMenuOption>)
                    && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters))
                .ToArray();
            if (entries.Length != 1)
            {
                failure = $"鼠族游商 GetFloatMenuOptionsForLeader(Lord, Pawn, Pawn) 静态菜单入口预期唯一，实际 {entries.Length} 个。";
                return false;
            }

            MethodInfo? moveNext = AccessTools.EnumeratorMoveNext(entries[0]);
            Type? stateMachine = moveNext?.DeclaringType;
            if (moveNext == null || moveNext.IsStatic || moveNext.ReturnType != typeof(bool)
                || moveNext.GetParameters().Length != 0
                || stateMachine == null || stateMachine.DeclaringType != type
                || stateMachine.Assembly != type.Assembly
                || !typeof(IEnumerator<FloatMenuOption>).IsAssignableFrom(stateMachine))
            {
                failure = "鼠族游商菜单入口的迭代器 MoveNext 无法精确确认。";
                return false;
            }
            target = moveNext;
            failure = string.Empty;
            return true;
        }
    }

    internal sealed class NewRatkinWanderingTraderIncidentCompatibility : NewRatkinCompatibilityModule
    {
        public override string ModuleId => "NewRatkin.WanderingTrader.Incident";
        public override string DisplayName => "鼠族游商：纯机械殖民地事件";
        protected override Type PatchType => typeof(NewRatkinWanderingTraderIncidentPatch);
        protected override string AppliedDetail => "仅为鼠族游商事件扩展执行时的殖民者存在判断，地图上的正式玩家机械族机械师可充当殖民地成员。";
        protected override void ResetConfiguration() => NewRatkinWanderingTraderIncidentPatch.Reset();

        protected override bool TryResolve(ModContentPack mod, out MethodInfo? target, out string failure)
        {
            target = null;
            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(mod,
                    "NewRatkin.IncidentWorker_RatkinWanderingTrader", "TryExecuteWorker", typeof(bool),
                    new[] { typeof(IncidentParms) }, out MethodInfo? executeWorker, out failure))
                return false;

            Type? workerType = executeWorker!.DeclaringType;
            IncidentDef? incident = DefDatabase<IncidentDef>.GetNamedSilentFail("RK_Incident_WanderingTrader");
            if (workerType == null || !typeof(IncidentWorker).IsAssignableFrom(workerType)
                || incident == null || incident.modContentPack != mod || incident.workerClass != workerType)
            {
                failure = "鼠族游商事件 Def、所属 MOD 或 workerClass 与预期不符。";
                return false;
            }

            MethodInfo? tryExecute = AccessTools.DeclaredMethod(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute),
                new[] { typeof(IncidentParms) });
            if (tryExecute == null || tryExecute.IsStatic || tryExecute.ReturnType != typeof(bool))
            {
                failure = "原版 IncidentWorker.TryExecute(IncidentParms) 的签名发生变化。";
                return false;
            }
            NewRatkinWanderingTraderIncidentPatch.Configure(incident, workerType);
            target = tryExecute;
            failure = string.Empty;
            return true;
        }
    }
}
