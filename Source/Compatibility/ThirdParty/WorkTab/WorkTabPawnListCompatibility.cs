using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.WorkTab
{
    // 名单显示与详细优先级分别安装，避免其中一个目标变化使另一项功能失效。
    internal sealed class WorkTabPawnListCompatibility : IThirdPartyCompatibilityModule
    {
        public string ModuleId => "WorkTab.MechPawnList";
        public string DisplayName => "Work Tab：机械族机械师名单显示";
        public string PackageId => WorkTabCompatibilityUtility.PackageId;

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            WorkTabPawnListPatch.Enabled = false;
            ModContentPack? mod = ThirdPartyCompatibilityTargetResolver.FindRunningMod(PackageId);
            if (mod == null)
                return ThirdPartyCompatibilityResult.CreateInactive(ModuleId, DisplayName, PackageId);

            MethodInfo? target = null;
            MethodInfo? patch = null;
            bool attempted = false;
            try
            {
                // 只解析该窗口自己声明的实例 Getter，不补丁所有 Pawn 表格的基类入口。
                if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(mod,
                        "WorkTab.MainTabWindow_WorkTab", "get_Pawns", typeof(IEnumerable<Pawn>),
                        Type.EmptyTypes, out target, out string failure))
                    return ThirdPartyCompatibilityResult.CreateTargetChanged(ModuleId, DisplayName, PackageId, failure);

                if (target == null || target.IsAbstract || target.ContainsGenericParameters
                    || !target.IsSpecialName || !typeof(MainTabWindow_PawnTable).IsAssignableFrom(target.DeclaringType))
                    return ThirdPartyCompatibilityResult.CreateTargetChanged(ModuleId, DisplayName, PackageId,
                        "WorkTab：窗口 Pawns Getter 不再属于可用的 Pawn 表格窗口。");

                patch = AccessTools.DeclaredMethod(typeof(WorkTabPawnListPatch), "Postfix",
                    new[] { typeof(IEnumerable<Pawn>).MakeByRefType() });
                if (patch == null || !patch.IsStatic || patch.ReturnType != typeof(void))
                    throw new MissingMethodException(typeof(WorkTabPawnListPatch).FullName, "Postfix");

                attempted = true;
                harmony.Patch(target, postfix: new HarmonyMethod(patch));
                WorkTabPawnListPatch.Enabled = true;
            }
            catch (Exception ex)
            {
                // 即使撤销失败，残留的 Postfix 也不再扩展名单。
                WorkTabPawnListPatch.Enabled = false;
                if (attempted && target != null && patch != null)
                {
                    try { harmony.Unpatch(target, patch); }
                    catch { /* 保留原始异常，只撤销本模块自己的补丁。 */ }
                }
                return ThirdPartyCompatibilityResult.CreateFailed(ModuleId, DisplayName, PackageId,
                    "WorkTab 名单兼容安装失败，已尝试回滚本模块补丁。", ex,
                    target == null ? Array.Empty<MethodBase>() : new MethodBase[] { target });
            }

            return ThirdPartyCompatibilityResult.CreateApplied(ModuleId, DisplayName, PackageId,
                "WorkTab 窗口已复用原版机械师名单追加规则，受显示开关控制，并保留剧本原有名单。");
        }
    }
}
