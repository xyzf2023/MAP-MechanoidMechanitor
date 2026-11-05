using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.MobileDragoon
{
    /// <summary>只为月亮驾驶 MobileDragoon 安装兼容；第三方类型全部从其所属 MOD 动态解析。</summary>
    internal sealed class MobileDragoonCompatibility : IThirdPartyCompatibilityModule
    {
        internal const string TargetPackageId = "Aoba.DeadManSwitch.MobileDragoon";
        private const string FrameworkPackageId = "Aoba.Exosuit.Framework";
        private const string BayTypeName = "Exosuit.Building_MaintenanceBay";
        private const string DriverTypeName = "Exosuit.JobDriver_GetInWalkerCore";
        private const string ParkingTypeName = "Exosuit.CompAssignableToPawn_Parking";

        public string ModuleId => "MobileDragoon";
        public string DisplayName => "失能机关：龙骑兵（月亮驾驶）";
        public string PackageId => TargetPackageId;

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            MobileDragoonMoonPilotPatch.Enabled = false;
            if (ThirdPartyCompatibilityTargetResolver.FindRunningMod(TargetPackageId) == null)
            {
                return ThirdPartyCompatibilityResult.CreateInactive(ModuleId, DisplayName, PackageId);
            }

            ModContentPack? framework =
                ThirdPartyCompatibilityTargetResolver.FindRunningMod(FrameworkPackageId);
            if (framework == null)
            {
                return ThirdPartyCompatibilityResult.CreateTargetChanged(
                    ModuleId, DisplayName, PackageId,
                    "MobileDragoon 已加载，但未找到前置 Aoba.Exosuit.Framework，兼容已跳过。");
            }

            if (!TryResolveTargets(framework, out MethodInfo[] targets, out string failure))
            {
                return ThirdPartyCompatibilityResult.CreateTargetChanged(
                    ModuleId, DisplayName, PackageId, failure);
            }

            if (!MobileDragoonMoonRenderPatch.TryResolveTargets(framework,
                    out List<MobileDragoonMoonRenderPatch.PatchTarget> renderTargets, out failure))
            {
                return ThirdPartyCompatibilityResult.CreateTargetChanged(
                    ModuleId, DisplayName, PackageId, failure);
            }

            MethodInfo[] patches =
            {
                AccessTools.Method(typeof(MobileDragoonMoonPilotPatch),
                    nameof(MobileDragoonMoonPilotPatch.TranspilerAcceptablePawnKind)),
                AccessTools.Method(typeof(MobileDragoonMoonPilotPatch),
                    nameof(MobileDragoonMoonPilotPatch.TranspilerReservations)),
                AccessTools.Method(typeof(MobileDragoonMoonPilotPatch),
                    nameof(MobileDragoonMoonPilotPatch.PostfixAssigningCandidates)),
                AccessTools.Method(typeof(MobileDragoonMoonInteractionPatch),
                    nameof(MobileDragoonMoonInteractionPatch.PostfixBayOptions)),
                AccessTools.Method(typeof(MobileDragoonMoonInteractionPatch),
                    nameof(MobileDragoonMoonInteractionPatch.PostfixPawnGizmos))
            };

            try
            {
                // 驾驶、渲染方法及框架子工作器均已确认，才开始安装。
                // Transpiler 对调用数量和异常块作严格校验，结构不符时抛出并回滚整个模块。
                harmony.Patch(targets[0], transpiler: new HarmonyMethod(patches[0]));
                harmony.Patch(targets[1], transpiler: new HarmonyMethod(patches[1]));
                for (int i = 2; i < targets.Length; i++)
                    harmony.Patch(targets[i], postfix: new HarmonyMethod(patches[i]));
                foreach (MobileDragoonMoonRenderPatch.PatchTarget target in renderTargets)
                {
                    harmony.Patch(target.Original,
                        prefix: target.IsPrefix ? new HarmonyMethod(target.Patch) : null,
                        postfix: target.IsPrefix ? null : new HarmonyMethod(target.Patch));
                }
                // 静态菜单提供器也遵守此开关，全部安装成功后才开放任何新增入口。
                MobileDragoonMoonPilotPatch.Enabled = true;
            }
            catch (Exception ex)
            {
                MobileDragoonMoonPilotPatch.Enabled = false;
                foreach (MobileDragoonMoonRenderPatch.PatchTarget target in renderTargets)
                {
                    try
                    {
                        harmony.Unpatch(target.Original, target.Patch);
                    }
                    catch
                    {
                        // 只回滚本模块的渲染补丁，不覆盖最初的异常。
                    }
                }
                for (int i = 0; i < targets.Length; i++)
                {
                    try
                    {
                        harmony.Unpatch(targets[i], patches[i]);
                    }
                    catch
                    {
                        // 不覆盖最初的安装异常，也不移除其他模块的补丁。
                    }
                }

                List<MethodBase> diagnosticTargets = new List<MethodBase>(targets);
                foreach (MobileDragoonMoonRenderPatch.PatchTarget target in renderTargets)
                {
                    diagnosticTargets.Add(target.Original);
                }
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId, DisplayName, PackageId,
                    "安装月亮驾驶龙骑兵兼容失败，已尝试回滚本模块全部补丁。", ex, diagnosticTargets.ToArray());
            }

            return ThirdPartyCompatibilityResult.CreateApplied(
                ModuleId, DisplayName, PackageId,
                "月亮驾驶龙骑兵的身份、体型、免驾驶服、驾驶员候选、手动上下机及服装渲染兼容。");
        }

        private static bool TryResolveTargets(
            ModContentPack framework, out MethodInfo[] targets, out string failure)
        {
            targets = Array.Empty<MethodInfo>();
            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                    framework, BayTypeName, "AcceptablePawnKind", typeof(AcceptanceReport),
                    new[] { typeof(Pawn) }, out MethodInfo? acceptable, out failure)
                || !ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                    framework, DriverTypeName, "TryMakePreToilReservations", typeof(bool),
                    new[] { typeof(bool) }, out MethodInfo? reservations, out failure)
                || !ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                    framework, ParkingTypeName, "get_AssigningCandidates", typeof(IEnumerable<Pawn>),
                    Type.EmptyTypes, out MethodInfo? candidates, out failure)
                || !ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                    framework, BayTypeName, "GetFloatMenuOptions", typeof(IEnumerable<FloatMenuOption>),
                    new[] { typeof(Pawn) }, out MethodInfo? bayOptions, out failure)
                || !ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                    framework, "Exosuit.Exosuit_Core", out Type? coreType, out failure)
                || !ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                    framework, "Exosuit.ExosuitExt", out Type? extensionType, out failure))
            {
                return false;
            }

            if (!typeof(Building).IsAssignableFrom(acceptable!.DeclaringType)
                || !typeof(JobDriver).IsAssignableFrom(reservations!.DeclaringType)
                || !typeof(CompAssignableToPawn).IsAssignableFrom(candidates!.DeclaringType)
                || !typeof(Apparel).IsAssignableFrom(coreType!)
                || !typeof(DefModExtension).IsAssignableFrom(extensionType!))
            {
                failure = "Exosuit 整备台、任务、分配组件或核心的基类发生变化。";
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                    framework, BayTypeName, "get_Core", coreType!, Type.EmptyTypes,
                    out MethodInfo? coreGetter, out failure)
                || !ThirdPartyCompatibilityTargetResolver.TryResolveUniqueField(
                    extensionType!, "RequiredApparelTag", typeof(string),
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                    out FieldInfo? requiredApparelTag, out failure))
            {
                return false;
            }

            MethodInfo? pawnGizmos = typeof(Pawn).GetMethod(nameof(Pawn.GetGizmos),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                null, Type.EmptyTypes, null);
            if (pawnGizmos == null || pawnGizmos.ReturnType != typeof(IEnumerable<Gizmo>))
            {
                failure = "原版 Pawn.GetGizmos 的签名发生变化。";
                return false;
            }

            MobileDragoonMoonPilotPatch.Configure(coreType!, coreGetter!, requiredApparelTag!);
            if (!MobileDragoonMoonPilotPatch.ValidateTargets(acceptable!, reservations!, out failure))
                return false;

            MobileDragoonMoonInteractionPatch.Configure(coreType!, acceptable!);
            targets = new[] { acceptable!, reservations!, candidates!, bayOptions!, pawnGizmos };
            return true;
        }
    }
}
