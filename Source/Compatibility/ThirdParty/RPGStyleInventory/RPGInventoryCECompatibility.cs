using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.RPGStyleInventory
{
    /// <summary>仅作用于 Revamped 的 CE 集成；不接管 CE 的弹药、战斗或方案系统。</summary>
    internal sealed class RPGInventoryCECompatibility : IThirdPartyCompatibilityModule
    {
        private const string CEType = "CEPatches.RPG_CEPatch+RPG_CEPatches";
        private const string AccessType = "CEPatches.CEAccess";
        private const string CEHarmonyId = "net.avilmask.rimworld.mod.RPG_CEPatches";
        public string ModuleId => "RPGInventory.Revamped.CombatExtended";
        public string DisplayName => "RPG 装备栏 Revamped + CE：机械体装备菜单";
        public string PackageId => RPGInventoryRuntime.RevampedPackageId;

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            RPGInventoryCEPatches.Enabled = false;
            ModContentPack? mod = ThirdPartyCompatibilityTargetResolver.FindRunningMod(PackageId);
            if (mod == null || ThirdPartyCompatibilityTargetResolver.FindRunningMod("CETeam.CombatExtended") == null)
                return ThirdPartyCompatibilityResult.CreateInactive(ModuleId, DisplayName, PackageId);
            List<RPGInventoryBinding> bindings = new List<RPGInventoryBinding>();
            List<RPGInventoryBinding> attempted = new List<RPGInventoryBinding>();
            RPGInventoryContext? context = null;
            try
            {
                try
                {
                    if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(mod, RPGInventoryRuntime.TabTypeName,
                            out Type? tabType, out string failure))
                        throw new InvalidOperationException(failure);
                    context = RPGInventoryRuntime.For(tabType!);
                    if (context?.Enabled != true)
                        throw new InvalidOperationException("Revamped 装备操作模块未就绪，不安装 CE 适配。");
                    context.CEEnabled = false;
                    Type ceType = ResolveType(mod, CEType);
                    Type accessType = ResolveType(mod, AccessType);
                    MethodInfo menu = ResolveStatic(ceType, "DropDownThingMenu", typeof(List<FloatMenuOption>),
                        typeof(object), typeof(Thing), typeof(bool));
                    MethodInfo notification = ResolveStatic(ceType, "InterfaceDropPrefix", typeof(void), typeof(object), typeof(Thing));
                    MethodInfo haul = ResolveStatic(ceType, "InterfaceDropHaul", typeof(void), typeof(Pawn), typeof(Thing), typeof(Pawn));
                    MethodInfo switchWeapon = ResolveStatic(accessType, "trySwitchToWeapon", typeof(void), typeof(object), typeof(ThingWithComps));
                    MethodInfo questLock = ResolveStatic(accessType, "isItemQuestLocked", typeof(bool), typeof(Pawn), typeof(Thing));
                    MethodInfo popupPrefix = ResolveStatic(ceType, "PopupMenuPrefix", typeof(bool), typeof(object),
                        typeof(List<FloatMenuOption>).MakeByRefType(), typeof(Pawn), typeof(Thing), typeof(bool));
                    MethodInfo rowPostfix = ResolveStatic(ceType, "DrawThingRowPostfix", typeof(void), typeof(object),
                        typeof(float).MakeByRefType(), typeof(float), typeof(Thing));
                    MethodInfo popup = AccessTools.DeclaredMethod(tabType, "PopupMenu", new[] { typeof(Pawn), typeof(Thing), typeof(bool) });
                    MethodInfo row = AccessTools.DeclaredMethod(tabType, "DrawThingRow", new[]
                        { typeof(float).MakeByRefType(), typeof(float), typeof(Thing), typeof(bool) });
                    MethodInfo transfer = AccessTools.DeclaredMethod(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.TryTransferEquipmentToContainer),
                        new[] { typeof(ThingWithComps), typeof(ThingOwner) });
                    if (popup == null || row == null || transfer == null || transfer.ReturnType != typeof(bool))
                        throw new InvalidOperationException("CE 装备菜单或原版容器转移签名无法确认。");
                    RPGInventoryCEPatches.Context = context;
                    RPGInventoryCEPatches.QuestLock = questLock;
                    RPGInventoryCEPatches.SwitchWeapon = switchWeapon;
                    RPGInventoryCEPatches.DropHaul = haul;
                    RPGInventoryCEPatches.MenuTranspiler(PatchProcessor.GetOriginalInstructions(menu)).ToList();
                    List<CodeInstruction> menuIL = PatchProcessor.GetOriginalInstructions(menu).ToList();
                    int info = menuIL.FindIndex(code => code.opcode == System.Reflection.Emit.OpCodes.Ldstr && Equals(code.operand, "ThingInfo"));
                    int outfitRead = menuIL.FindIndex(code => code.LoadsField(AccessTools.Field(typeof(Pawn), nameof(Pawn.outfits))));
                    if (info < 0 || outfitRead <= info)
                        throw new InvalidOperationException("CE 菜单的信息卡与操作区顺序变化。");

                    // MOD 静态构造的执行顺序不固定。按原有入口初始化一次，再验证实际 Harmony 绑定。
                    RuntimeHelpers.RunClassConstructor(ceType.DeclaringType!.TypeHandle);
                    RequireBinding(popup, popupPrefix, HarmonyPatchType.Prefix);
                    RequireBinding(row, rowPostfix, HarmonyPatchType.Postfix);
                    RequireBinding(context.Drop, notification, HarmonyPatchType.Prefix);

                    Add(bindings, menu, nameof(RPGInventoryCEPatches.MenuTranspiler), HarmonyPatchType.Transpiler);
                    Add(bindings, menu, nameof(RPGInventoryCEPatches.MenuPostfix), HarmonyPatchType.Postfix);
                    Add(bindings, notification, nameof(RPGInventoryCEPatches.DropNotificationPrefix), HarmonyPatchType.Prefix);
                    Add(bindings, haul, nameof(RPGInventoryCEPatches.HaulPrefix), HarmonyPatchType.Prefix);
                    Add(bindings, switchWeapon, nameof(RPGInventoryCEPatches.SwitchPrefix), HarmonyPatchType.Prefix);
                    Add(bindings, transfer, nameof(RPGInventoryCEPatches.TransferPrefix), HarmonyPatchType.Prefix);
                }
                catch (InvalidOperationException ex)
                {
                    return ThirdPartyCompatibilityResult.CreateTargetChanged(ModuleId, DisplayName, PackageId, ex.Message);
                }
                foreach (RPGInventoryBinding binding in bindings)
                {
                    attempted.Add(binding);
                    RPGInventoryCompatibility.Install(harmony, binding);
                }
                RPGInventoryCEPatches.Enabled = true;
                context!.CEEnabled = true;
            }
            catch (Exception ex)
            {
                RPGInventoryCEPatches.Enabled = false;
                if (context != null) context.CEEnabled = false;
                RPGInventoryCompatibility.Rollback(harmony, attempted);
                return ThirdPartyCompatibilityResult.CreateFailed(ModuleId, DisplayName, PackageId,
                    "已关闭 CE 适配并尝试仅回滚本模块补丁。", ex,
                    bindings.Select(binding => (MethodBase)binding.Target).ToArray());
            }
            return ThirdPartyCompatibilityResult.CreateApplied(ModuleId, DisplayName, PackageId,
                "已保护 CE 槽位和列表菜单、专武收起、普通武器切换与带宽确认；缺少服装 Tracker 时安全枚举空列表。");
        }

        private static void Add(List<RPGInventoryBinding> bindings, MethodInfo target, string patch, HarmonyPatchType kind) =>
            RPGInventoryCompatibility.Add(bindings, target, patch, kind, typeof(RPGInventoryCEPatches));

        private static Type ResolveType(ModContentPack mod, string name)
        {
            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(mod, name, out Type? type, out string failure))
                throw new InvalidOperationException(failure);
            return type!;
        }

        private static MethodInfo ResolveStatic(Type type, string name, Type result, params Type[] parameters)
        {
            MethodInfo[] matches = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(method => method.Name == name && method.ReturnType == result && !method.IsGenericMethod
                    && !method.ContainsGenericParameters && !method.IsAbstract
                    && method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameters)).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException(type.FullName + "." + name + " 的完整签名不唯一。");
            return matches[0];
        }

        private static void RequireBinding(MethodInfo target, MethodInfo patch, HarmonyPatchType kind)
        {
            Patches? info = Harmony.GetPatchInfo(target);
            IEnumerable<Patch> installed = kind == HarmonyPatchType.Prefix
                ? info?.Prefixes ?? Enumerable.Empty<Patch>() : info?.Postfixes ?? Enumerable.Empty<Patch>();
            if (installed.Count(binding => binding.owner == CEHarmonyId && binding.PatchMethod == patch) != 1)
                throw new InvalidOperationException("RPG CE 上游补丁绑定无法唯一确认：" + patch.Name);
        }
    }
}
