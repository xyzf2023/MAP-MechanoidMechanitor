using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.VehicleFramework
{
    // 依据 SmashPhil/Vehicle-Framework release/1.6 的
    // 1e8ec34e1b6a481255d70bcaa54fbd05b44b49dc；本机 VF 1.6.2144 的签名及身份拒绝 IL 一致。
    // 仅动态解析目标 MOD 自己的程序集，不引入 Vehicles.dll 静态依赖。
    internal sealed class VehicleFrameworkOperatorCompatibility : IThirdPartyCompatibilityModule
    {
        public string ModuleId => "VehicleFramework.MechOperator";
        public string DisplayName => "Vehicle Framework：机械族机械师驾驶与炮手资格";
        public string PackageId => "SmashPhil.VehicleFramework";

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            VehicleFrameworkOperatorPatch.Enabled = false;
            ModContentPack? mod = ThirdPartyCompatibilityTargetResolver.FindRunningMod(PackageId);
            if (mod == null)
                return ThirdPartyCompatibilityResult.CreateInactive(ModuleId, DisplayName, PackageId);

            MethodInfo? target = null;
            MethodInfo? patch = null;
            bool attempted = false;
            try
            {
                if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(mod,
                        "Vehicles.VehicleRoleHandler", out Type? handlerType, out string failure)
                    || !ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(mod,
                        "Vehicles.HandlingType", out Type? handlingType, out failure))
                    return Changed(failure);

                // int 形参仅用于接收已核实的枚举栈值；枚举成员变化时不猜测新角色语义。
                if (!handlingType!.IsEnum || Enum.GetUnderlyingType(handlingType) != typeof(int)
                    || !handlingType.IsDefined(typeof(FlagsAttribute), false)
                    || Enum.GetNames(handlingType).Length != 3
                    || !HasEnumValue(handlingType, "None", 0)
                    || !HasEnumValue(handlingType, "Movement", 1)
                    || !HasEnumValue(handlingType, "Turret", 2))
                    return Changed("VF HandlingType 不再是预期的 int 标志枚举（None=0、Movement=1、Turret=2）。");

                int matches = 0;
                foreach (MethodInfo candidate in handlerType!.GetMethods(
                             BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    ParameterInfo[] parameters = candidate.GetParameters();
                    if (candidate.Name != "CanOperateRole" || candidate.ReturnType != typeof(bool)
                        || parameters.Length != 2 || parameters[0].ParameterType != typeof(Pawn)
                        || parameters[1].ParameterType != handlingType)
                        continue;
                    target = candidate;
                    matches++;
                }
                if (matches != 1 || target == null || target.ContainsGenericParameters
                    || target.IsGenericMethod || target.GetMethodBody() == null)
                    return Changed("VF 公开静态 Boolean CanOperateRole(Pawn, HandlingType) 签名不再唯一或没有方法体。", target);

                // 游戏安装模块时预检原始 IL，安装时再次校验；不执行目标方法。
                try
                {
                    VehicleFrameworkOperatorPatch.Transpiler(PatchProcessor.GetOriginalInstructions(target));
                }
                catch (InvalidOperationException ex)
                {
                    return Changed(ex.Message, target);
                }

                patch = AccessTools.DeclaredMethod(typeof(VehicleFrameworkOperatorPatch),
                    nameof(VehicleFrameworkOperatorPatch.Transpiler), new[] { typeof(IEnumerable<CodeInstruction>) });
                if (patch == null || !patch.IsStatic || patch.ReturnType != typeof(IEnumerable<CodeInstruction>))
                    throw new MissingMethodException(nameof(VehicleFrameworkOperatorPatch.Transpiler));

                attempted = true;
                harmony.Patch(target, transpiler: new HarmonyMethod(patch));
                VehicleFrameworkOperatorPatch.Enabled = true;
                return ThirdPartyCompatibilityResult.CreateApplied(ModuleId, DisplayName, PackageId,
                    "仅扩展驾驶／炮手资格中的殖民地机械体排除；正式登记且当前可操纵的玩家机械师继续接受 VF 全部其他检查。");
            }
            catch (Exception ex)
            {
                VehicleFrameworkOperatorPatch.Enabled = false;
                if (attempted && target != null && patch != null)
                {
                    try { harmony.Unpatch(target, patch); }
                    catch { /* 保留安装异常；残留 helper 仍执行原机械体判断。 */ }
                }
                return ThirdPartyCompatibilityResult.CreateFailed(ModuleId, DisplayName, PackageId,
                    "VF 操作资格兼容安装失败，已关闭新增资格并尝试回滚本模块补丁。", ex,
                    target == null ? Array.Empty<MethodBase>() : new MethodBase[] { target });
            }
        }

        private static bool HasEnumValue(Type type, string name, int value) =>
            Enum.IsDefined(type, name) && Convert.ToInt32(Enum.Parse(type, name)) == value;

        private ThirdPartyCompatibilityResult Changed(string reason, MethodInfo? target = null) =>
            ThirdPartyCompatibilityResult.CreateTargetChanged(ModuleId, DisplayName, PackageId, reason,
                target == null ? Array.Empty<MethodBase>() : new MethodBase[] { target });
    }
}
