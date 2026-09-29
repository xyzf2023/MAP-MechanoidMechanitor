using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.RimSkyBlock
{
    // 两个独立模块共用安装/回滚流程；此处不保存 Game、Pawn 或第三方组件实例。
    internal abstract class RimSkyBlockCompatibilityModule : IThirdPartyCompatibilityModule
    {
        protected sealed class PatchBinding
        {
            internal readonly MethodInfo Target;
            internal readonly MethodInfo Patch;
            internal readonly bool Postfix;

            internal PatchBinding(MethodInfo target, Type patchType, string patchName, bool postfix = false)
            {
                Target = target;
                Patch = AccessTools.Method(patchType, patchName)
                    ?? throw new InvalidOperationException($"无法解析空岛兼容方法 {patchName}。");
                Postfix = postfix;
            }
        }

        public abstract string ModuleId { get; }
        public abstract string DisplayName { get; }
        public string PackageId => RimSkyBlockCompatibilityUtility.PackageId;

        protected abstract bool TryResolve(ModContentPack mod, List<PatchBinding> bindings, out string failure);

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            ModContentPack? mod = ThirdPartyCompatibilityTargetResolver.FindRunningMod(PackageId);
            if (mod == null)
                return ThirdPartyCompatibilityResult.CreateInactive(ModuleId, DisplayName, PackageId);

            List<PatchBinding> bindings = new List<PatchBinding>();
            List<PatchBinding> attempted = new List<PatchBinding>();
            try
            {
                // 包括仪式匿名筛选器在内的全部关键目标都确认后，才开始安装。
                if (!TryResolve(mod, bindings, out string failure))
                    return ThirdPartyCompatibilityResult.CreateTargetChanged(
                        ModuleId, DisplayName, PackageId, failure);

                foreach (PatchBinding binding in bindings)
                {
                    attempted.Add(binding);
                    harmony.Patch(binding.Target,
                        postfix: binding.Postfix ? new HarmonyMethod(binding.Patch) : null,
                        transpiler: binding.Postfix ? null : new HarmonyMethod(binding.Patch));
                }
            }
            catch (Exception ex)
            {
                foreach (PatchBinding binding in attempted.AsEnumerable().Reverse())
                {
                    try { harmony.Unpatch(binding.Target, binding.Patch); }
                    catch { /* 保留初始失败原因，不移除其他模块或其他 MOD 的补丁。 */ }
                }
                return ThirdPartyCompatibilityResult.CreateFailed(ModuleId, DisplayName, PackageId,
                    "空岛兼容安装失败，已尝试回滚本模块的全部补丁。", ex,
                    bindings.Select(b => (MethodBase)b.Target).ToArray());
            }
            return ThirdPartyCompatibilityResult.CreateApplied(ModuleId, DisplayName, PackageId,
                "仅扩展正式机械族机械师资格，保留空岛原有流程。");
        }
    }

    internal sealed class RimSkyBlockImperialTaskCompatibility : RimSkyBlockCompatibilityModule
    {
        public override string ModuleId => "RimSkyBlock.ImperialTasks";
        public override string DisplayName => "空岛：帝国远行队任务";

        protected override bool TryResolve(ModContentPack mod, List<PatchBinding> bindings, out string failure)
        {
            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                mod, "RimSkyBlock.WorldObject_BestowingCeremony", "FindBestPawnOfSkill", typeof(Pawn),
                new[] { typeof(Caravan), typeof(SkillDef) }, out MethodInfo? target, out failure))
                return false;

            bindings.Add(new PatchBinding(target!, typeof(RimSkyBlockImperialTaskPatch),
                nameof(RimSkyBlockImperialTaskPatch.Postfix), true));
            return true;
        }
    }

    internal sealed class RimSkyBlockCoronationCompatibility : RimSkyBlockCompatibilityModule
    {
        private const string ManagerType = "RimSkyBlock.GameComponent_CoronationManager";
        public override string ModuleId => "RimSkyBlock.Coronation";
        public override string DisplayName => "空岛：机械族加冕";

        protected override bool TryResolve(ModContentPack mod, List<PatchBinding> bindings, out string failure)
        {
            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                    mod, "RimSkyBlock.PendingCoronation", out Type? pending, out failure)
                || !ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                    mod, "RimSkyBlock.Building_CouncilDais", out Type? dais, out failure)
                || !ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                    mod, ManagerType, "ShowCandidateMenu", typeof(void),
                    new[] { typeof(string), typeof(string) }, out MethodInfo? candidates, out failure)
                || !ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                    mod, ManagerType, "CanRequestCoronation", typeof(bool),
                    new[] { typeof(Pawn), typeof(RoyalTitleDef), typeof(string).MakeByRefType() },
                    out MethodInfo? request, out failure)
                || !ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                    mod, ManagerType, "IsStillValid", typeof(bool),
                    new[] { pending!, typeof(string).MakeByRefType() }, out MethodInfo? valid, out failure)
                || !ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                    mod, ManagerType, "OpenOriginalRitualDialog", typeof(void),
                    new[] { pending!, dais! }, out MethodInfo? openDialog, out failure))
                return false;

            // 匿名函数的数字后缀随上游编辑而变化。仅接受该方法所属编译器生成类型内，
            // 名称归属、返回值、完整参数列表均吻合且唯一的 PawnFilter，不按模糊名字盲补。
            List<MethodInfo> filters = openDialog!.DeclaringType!
                .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .Where(t => t.IsDefined(typeof(CompilerGeneratedAttribute), false))
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                .Where(m => m.Name.StartsWith("<OpenOriginalRitualDialog>b__", StringComparison.Ordinal)
                    && m.ReturnType == typeof(bool)
                    && m.GetParameters().Select(p => p.ParameterType)
                        .SequenceEqual(new[] { typeof(Pawn), typeof(bool), typeof(bool) }))
                .ToList();
            if (filters.Count != 1)
            {
                failure = $"空岛加冕 PawnFilter 预期唯一，实际找到 {filters.Count} 个。";
                return false;
            }

            MethodInfo? ritualCandidates = AccessTools.DeclaredMethod(typeof(Dialog_BeginRitual),
                nameof(Dialog_BeginRitual.CreateRitualRoleAssignments),
                new[] { typeof(Precept_Ritual), typeof(TargetInfo), typeof(Map),
                    typeof(Dialog_BeginRitual.PawnFilter), typeof(List<Pawn>),
                    typeof(Dictionary<string, Pawn>), typeof(Pawn) });
            if (ritualCandidates == null || !ritualCandidates.IsStatic
                || ritualCandidates.ReturnType != typeof(RitualRoleAssignments))
            {
                failure = "原版仪式候选构建方法的签名发生变化。";
                return false;
            }

            Type patchType = typeof(RimSkyBlockCoronationPatch);
            bindings.Add(new PatchBinding(candidates!, patchType, nameof(RimSkyBlockCoronationPatch.CandidateTranspiler)));
            bindings.Add(new PatchBinding(request!, patchType, nameof(RimSkyBlockCoronationPatch.EligibilityTranspiler)));
            bindings.Add(new PatchBinding(valid!, patchType, nameof(RimSkyBlockCoronationPatch.EligibilityTranspiler)));
            bindings.Add(new PatchBinding(filters[0], patchType, nameof(RimSkyBlockCoronationPatch.FilterTranspiler)));
            bindings.Add(new PatchBinding(ritualCandidates, patchType, nameof(RimSkyBlockCoronationPatch.RitualCandidatesTranspiler)));
            return true;
        }
    }
}
