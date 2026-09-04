using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.VanillaPsycastsExpanded
{
    /// <summary>
    /// 为第三方 MOD《Vanilla Psycasts Expanded / 原版灵能拓展》（VanillaExpanded.VPsycastsE）提供基础系统兼容。
    /// 仅当 VPE 与前置 VEF（OskarPotocki.VanillaFactionsExpanded.Core）均已加载，
    /// 且全部第三方反射目标按完整类型名精确解析成功后：
    /// 1) 为所有机械族 ThingDef 动态注入 VEF.Abilities.CompAbilities 能力容器，并补充 VPE 灵能树页签；
    /// 2) 安装六个显式 Harmony 补丁（机械师身份初始化 Postfix、旧档恢复 Postfix、
    ///    灵能中枢升级前 Prefix、灵能中枢完成后 Postfix、
    ///    VEF Ability Gizmo 可见性 Transpiler、VEF Ability 自动释放开关 Transpiler），
    ///    驱动幂等的 Pawn VPE 状态补齐。
    /// 未加载 VPE 时返回 Inactive（静默，不输出警告）；任一第三方目标签名不符返回 TargetChanged；
    /// Def 注入或补丁安装发生本模块自身异常返回 Failed。
    /// 全程不使用 VEF.dll / VPE.dll 静态引用，不添加 [HarmonyPatch]、不使用 PatchAll，
    /// 所有第三方类型均通过 ModContentPack 的完整类型名唯一反射解析。
    /// </summary>
    internal sealed class VanillaPsycastsExpandedCompatibility :
        IThirdPartyCompatibilityModule
    {
        private const string VpePackageId = "VanillaExpanded.VPsycastsE";
        private const string VefPackageId = "OskarPotocki.VanillaFactionsExpanded.Core";

        private const string CompAbilitiesTypeName = "VEF.Abilities.CompAbilities";
        private const string CompShieldBubbleTypeName = "VEF.Apparels.CompShieldBubble";
        private const string CompPropertiesShieldBubbleTypeName =
            "VEF.Apparels.CompProperties_ShieldBubble";
        private const string HediffPsycastAbilitiesTypeName =
            "VanillaPsycastsExpanded.Hediff_PsycastAbilities";
        private const string ITabPsycastsTypeName =
            "VanillaPsycastsExpanded.UI.ITab_Pawn_Psycasts";

        private const string VefAbilityTypeName = "VEF.Abilities.Ability";

        private const string VpeHediffDefName = "VPE_PsycastAbilityImplant";

        private const string ShieldTexturePath = "Other/ShieldBubble";
        private const float MinShieldSize = 1f;
        private const float MaxShieldSize = 1.5f;
        private const float EnergyLossPerDamage = 1f;

        public string ModuleId => "VanillaPsycastsExpanded";

        public string DisplayName => "原版灵能拓展（VanillaExpanded.VPsycastsE）";

        public string PackageId => VpePackageId;

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            // 第一步：VPE 未加载时立即返回 Inactive，不能输出警告。
            ModContentPack? vpeMod =
                ThirdPartyCompatibilityTargetResolver.FindRunningMod(VpePackageId);
            if (vpeMod == null)
            {
                return ThirdPartyCompatibilityResult.CreateInactive(
                    ModuleId, DisplayName, PackageId);
            }

            // 第二步：VPE 已加载后，再查找 VEF。
            ModContentPack? vefMod =
                ThirdPartyCompatibilityTargetResolver.FindRunningMod(VefPackageId);
            if (vefMod == null)
            {
                return ThirdPartyCompatibilityResult.CreateTargetChanged(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "原版灵能拓展（VPE）已加载，但前置 VEF" +
                    "（OskarPotocki.VanillaFactionsExpanded.Core）未找到，" +
                    "目标依赖结构不完整，兼容已安全跳过。");
            }

            // 第三步：全部第三方反射目标精确解析。任何一项不符即 TargetChanged，
            // 此时不修改任何 Def，也不安装任何补丁。
            if (!TryResolveAllTargets(
                    vpeMod,
                    vefMod,
                    out ResolvedTargets? targets,
                    out string resolveFailure))
            {
                return ThirdPartyCompatibilityResult.CreateTargetChanged(
                    ModuleId, DisplayName, PackageId, resolveFailure);
            }

            if (targets == null)
            {
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "原版灵能拓展兼容目标解析结果为空。",
                    new InvalidOperationException("ResolvedTargets is null."));
            }

            // 第四步：精确解析本项目自身的三个 Harmony 目标并校验签名。
            if (!TryResolveOwnHarmonyTargets(
                    out MethodInfo? ensureRoleStateMethod,
                    out MethodInfo? tryRestorePositiveSourcesMethod,
                    out MethodInfo? tryGainPsylinkLevelMethod,
                    out string ownTargetFailure))
            {
                return ThirdPartyCompatibilityResult.CreateTargetChanged(
                    ModuleId, DisplayName, PackageId, ownTargetFailure);
            }

            // 第五步：精确解析本项目四个补丁方法以及第五个入口 Transpiler，并校验签名。
            MethodInfo? postfixEnsureRoleState = GetPatchMethod(
                nameof(VanillaPsycastsExpandedCompatibilityPatches
                    .Postfix_EnsureRoleState));
            MethodInfo? postfixTryRestore = GetPatchMethod(
                nameof(VanillaPsycastsExpandedCompatibilityPatches
                    .Postfix_TryRestorePositiveSources));
            MethodInfo? prefixTryGainPsylinkLevel = GetPatchMethod(
                nameof(VanillaPsycastsExpandedCompatibilityPatches
                    .Prefix_TryGainPsylinkLevel));
            MethodInfo? postfixTryGainPsylinkLevel = GetPatchMethod(
                nameof(VanillaPsycastsExpandedCompatibilityPatches
                    .Postfix_TryGainPsylinkLevel));
            if (postfixEnsureRoleState == null
                || postfixTryRestore == null
                || prefixTryGainPsylinkLevel == null
                || postfixTryGainPsylinkLevel == null
                || !IsStaticVoidWithSingleParameter(postfixEnsureRoleState, typeof(Pawn))
                || !IsStaticVoidWithSingleParameter(postfixTryRestore, typeof(bool))
                || !IsStaticVoidWithSingleParameter(prefixTryGainPsylinkLevel, typeof(Pawn))
                || !IsStaticVoidWithSingleParameter(postfixTryGainPsylinkLevel, typeof(Pawn)))
            {
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "无法解析本项目的原版灵能拓展兼容补丁方法或其签名不符。",
                    new MissingMethodException(
                        typeof(VanillaPsycastsExpandedCompatibilityPatches).FullName,
                        nameof(VanillaPsycastsExpandedCompatibilityPatches
                            .Postfix_EnsureRoleState)
                        + " / "
                        + nameof(VanillaPsycastsExpandedCompatibilityPatches
                            .Postfix_TryRestorePositiveSources)
                        + " / "
                        + nameof(VanillaPsycastsExpandedCompatibilityPatches
                            .Prefix_TryGainPsylinkLevel)
                        + " / "
                        + nameof(VanillaPsycastsExpandedCompatibilityPatches
                            .Postfix_TryGainPsylinkLevel)));
            }

            // 第五个入口 Transpiler 精确解析与签名校验（static、
            // 返回 IEnumerable<CodeInstruction>、恰好一个 IEnumerable<CodeInstruction> 参数）。
            MethodInfo? transpilerAbilityShowGizmoOnPawn = GetPatchMethod(
                nameof(VanillaPsycastsExpandedCompatibilityPatches
                    .Transpiler_AbilityShowGizmoOnPawn));
            if (transpilerAbilityShowGizmoOnPawn == null
                || !IsValidTranspiler(transpilerAbilityShowGizmoOnPawn))
            {
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "无法解析本项目的原版灵能拓展兼容 Transpiler 方法或其签名不符。",
                    new MissingMethodException(
                        typeof(VanillaPsycastsExpandedCompatibilityPatches).FullName,
                        nameof(VanillaPsycastsExpandedCompatibilityPatches
                            .Transpiler_AbilityShowGizmoOnPawn)));
            }

            // 第六个入口 Transpiler（VEF.Abilities.Ability.get_AutoCast）精确解析与签名校验。
            // 必须与第五个入口使用同一套 IsValidTranspiler 签名校验，不得模糊寻找替代方法。
            MethodInfo? transpilerAbilityAutoCast = GetPatchMethod(
                nameof(VanillaPsycastsExpandedCompatibilityPatches
                    .Transpiler_AbilityAutoCast));
            if (transpilerAbilityAutoCast == null
                || !IsValidTranspiler(transpilerAbilityAutoCast))
            {
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "无法解析本项目的原版灵能拓展兼容自动释放 Transpiler 方法或其签名不符。",
                    new MissingMethodException(
                        typeof(VanillaPsycastsExpandedCompatibilityPatches).FullName,
                        nameof(VanillaPsycastsExpandedCompatibilityPatches
                            .Transpiler_AbilityAutoCast)));
            }

            // 第六步：先显式完成 VPE 灵能树页签（ITab_Pawn_Psycasts）类型的静态构造，
            // 避免随后 GetSharedInstance 在创建实例过程中触发该类型的静态构造，
            // 进而发生同类型共享实例的重复登记（重复键异常）。
            // 静态构造完成后，再取得已由灵拓自身登记的共享页签实例。
            // 这一切发生在 Runtime.Configure、Def 注入与 Harmony 补丁安装之前。
            try
            {
                RuntimeHelpers.RunClassConstructor(
                    targets.ITabPsycastsType.TypeHandle);
            }
            catch (Exception ex)
            {
                // 静态初始化失败：尚未执行 Runtime.Configure / Def 注入 / 补丁安装，无需回滚。
                return ThirdPartyCompatibilityResult.CreateTargetChanged(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "显式执行 VPE 灵能树页签类型的静态初始化失败，兼容已安全跳过：" + ex);
            }

            // 静态构造已成功执行（或此前已完成），灵拓已在内部完成共享实例的首次登记。
            // 再次取得共享实例，此时 InspectTabManager 应直接返回既有的共享实例，不再重复登记。
            InspectTabBase? sharedPsycastsTab;
            try
            {
                sharedPsycastsTab = InspectTabManager.GetSharedInstance(
                    targets.ITabPsycastsType);
            }
            catch (Exception ex)
            {
                return ThirdPartyCompatibilityResult.CreateTargetChanged(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "VPE 灵能树页签类型已完成静态初始化，但取得其共享实例失败，兼容已安全跳过：" + ex);
            }

            if (sharedPsycastsTab == null
                || !targets.ITabPsycastsType.IsInstanceOfType(sharedPsycastsTab))
            {
                return ThirdPartyCompatibilityResult.CreateTargetChanged(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "VPE 灵能树页签共享实例（静态初始化后取得）为空或类型不符，兼容已安全跳过。");
            }

            // 第七步：解析 VPE_PsycastAbilityImplant HediffDef，并校验 hediffClass 兼容性。
            HediffDef? vpeHediffDef =
                DefDatabase<HediffDef>.GetNamedSilentFail(VpeHediffDefName);
            if (vpeHediffDef == null
                || vpeHediffDef.hediffClass == null
                || !targets.HediffPsycastAbilitiesType.IsAssignableFrom(
                    vpeHediffDef.hediffClass))
            {
                return ThirdPartyCompatibilityResult.CreateTargetChanged(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    $"HediffDef {VpeHediffDefName} 缺失，或其 hediffClass 与解析出的" +
                    $" {HediffPsycastAbilitiesTypeName} 不兼容，兼容已安全跳过。");
            }

            // 第八步：以上所有目标全部成功后，才配置 Runtime、修改 Def、安装补丁。
            VanillaPsycastsExpandedCompatibilityRuntime.Configure(
                targets.CompAbilitiesType,
                targets.HediffPsycastAbilitiesType,
                vpeHediffDef,
                targets.InitializeFromPsylinkMethod);
            if (!VanillaPsycastsExpandedCompatibilityRuntime.IsConfigured)
            {
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "原版灵能拓展兼容运行时配置失败，兼容已停止。",
                    new InvalidOperationException(
                        "Runtime.Configure did not configure the runtime."));
            }

            DefInjectionTracker injectionTracker = new DefInjectionTracker();
            try
            {
                InjectAbilitiesIntoMechanoidDefs(
                    targets, sharedPsycastsTab, injectionTracker);
                InstallPatches(
                    harmony,
                    ensureRoleStateMethod!,
                    tryRestorePositiveSourcesMethod!,
                    tryGainPsylinkLevelMethod!,
                    targets.AbilityShowGizmoOnPawnMethod,
                    targets.AbilityAutoCastGetter,
                    postfixEnsureRoleState!,
                    postfixTryRestore!,
                    prefixTryGainPsylinkLevel!,
                    postfixTryGainPsylinkLevel!,
                    transpilerAbilityShowGizmoOnPawn!,
                    transpilerAbilityAutoCast!);
            }
            catch (Exception ex)
            {
                // 无条件回滚本轮新增的组件 / 页签，随后清除 Runtime 配置。
                // 绝不删除原本已经存在的内容。
                injectionTracker.Rollback();
                VanillaPsycastsExpandedCompatibilityRuntime.Clear();
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId,
                    DisplayName,
                    PackageId,
                    "应用原版灵能拓展兼容（Def 注入或 Harmony 补丁安装）时发生异常。",
                    ex,
                    ensureRoleStateMethod!,
                    tryRestorePositiveSourcesMethod!,
                    tryGainPsylinkLevelMethod!,
                    targets.AbilityShowGizmoOnPawnMethod,
                    targets.AbilityAutoCastGetter);
            }

            return ThirdPartyCompatibilityResult.CreateApplied(
                ModuleId,
                DisplayName,
                PackageId,
                $"已为 {injectionTracker.AddedCompCount} 个机械族定义新增 CompAbilities；"
                + $"{injectionTracker.ExistingCompCount} 个原本已有 CompAbilities；"
                + $"{injectionTracker.SkippedShieldConflictCount} 个因其他护盾 Comp 冲突跳过；"
                + $"成功补充 VPE 灵能页签 {injectionTracker.AddedTabCount} 个定义；"
                + "已安装六个兼容入口：机械师身份初始化修复"
                + "（MechanoidMechanitorRoleUtility.EnsureRoleState Postfix）、"
                + "旧档恢复修复（MechanoidMechanitorPostLoadSafetyCoordinator."
                + "TryRestorePositiveSources Postfix）、"
                + "灵能中枢升级前修复（PsychicCoreUtility.TryGainPsylinkLevel Prefix）、"
                + "灵能中枢完成后的即时修复（PsychicCoreUtility.TryGainPsylinkLevel Postfix）、"
                + "VEF Ability Gizmo 可见性修复"
                + "（VEF.Abilities.Ability.ShowGizmoOnPawn Transpiler）、"
                + "VEF Ability 自动释放开关修复"
                + "（VEF.Abilities.Ability.get_AutoCast Transpiler）。");
        }

        // ==================== 反射目标解析 ====================

        private static bool TryResolveAllTargets(
            ModContentPack vpeMod,
            ModContentPack vefMod,
            out ResolvedTargets? targets,
            out string failureReason)
        {
            targets = null;
            failureReason = string.Empty;

            // —— 5. 类型解析：从各自 ModContentPack 按完整类型名唯一解析。 ——
            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                    vefMod,
                    CompAbilitiesTypeName,
                    out Type? compAbilitiesType,
                    out string typeFailure))
            {
                failureReason = typeFailure;
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                    vefMod,
                    CompShieldBubbleTypeName,
                    out Type? shieldCompType,
                    out typeFailure))
            {
                failureReason = typeFailure;
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                    vefMod,
                    CompPropertiesShieldBubbleTypeName,
                    out Type? shieldPropsType,
                    out typeFailure))
            {
                failureReason = typeFailure;
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                    vpeMod,
                    HediffPsycastAbilitiesTypeName,
                    out Type? hediffPsycastAbilitiesType,
                    out typeFailure))
            {
                failureReason = typeFailure;
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                    vpeMod,
                    ITabPsycastsTypeName,
                    out Type? itabPsycastsType,
                    out typeFailure))
            {
                failureReason = typeFailure;
                return false;
            }

            // —— 5b. VEF.Abilities.Ability 类型解析（从 VEF 程序集按完整类型名唯一解析）。 ——
            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                    vefMod,
                    VefAbilityTypeName,
                    out Type? vefAbilityType,
                    out typeFailure))
            {
                failureReason = typeFailure;
                return false;
            }

            if (!vefAbilityType!.IsClass)
            {
                failureReason =
                    $"{VefAbilityTypeName} 不是 class，签名不符。";
                return false;
            }

            // —— 5c. VEF.Abilities.Ability.ShowGizmoOnPawn() 实例方法精确解析。 ——
            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                    vefMod,
                    VefAbilityTypeName,
                    "ShowGizmoOnPawn",
                    typeof(bool),
                    Type.EmptyTypes,
                    out MethodInfo? abilityShowGizmoOnPawnMethod,
                    out string showGizmoMethodFailure))
            {
                failureReason = showGizmoMethodFailure;
                return false;
            }

            // —— 5d. VEF.Abilities.Ability.get_AutoCast() 实例属性 getter 精确解析。 ——
            // 该 getter 的语义为：玩家控制殖民者时返回 autoCast 字段，
            // 否则返回 pawn.Spawned && CanAutoCast。机械族机械师被原版视作非殖民者，
            // 因此永远走 AI 分支，导致玩家无法关闭自动释放。需以 Transpiler 把唯一的
            // Pawn.get_IsColonistPlayerControlled 调用替换为玩家阵营正式机械族机械师判定。
            // 必须使用精确唯一解析，不得模糊匹配、不得枚举后取 First()。
            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                    vefMod,
                    VefAbilityTypeName,
                    "get_AutoCast",
                    typeof(bool),
                    Type.EmptyTypes,
                    out MethodInfo? abilityAutoCastGetter,
                    out string autoCastMethodFailure))
            {
                failureReason = autoCastMethodFailure;
                return false;
            }

            // 防御性校验：声明类型必须正是已解析的 VEF.Abilities.Ability，
            // 且为属性 getter（IsSpecialName）。任何不符视为第三方目标结构变化。
            if (abilityAutoCastGetter!.DeclaringType != vefAbilityType)
            {
                failureReason =
                    $"{VefAbilityTypeName}.get_AutoCast 的声明类型" +
                    $" {abilityAutoCastGetter.DeclaringType?.FullName} 与预期" +
                    $" {VefAbilityTypeName} 不符，兼容已安全跳过。";
                return false;
            }

            if (!abilityAutoCastGetter.IsSpecialName)
            {
                failureReason =
                    $"{VefAbilityTypeName}.get_AutoCast 不是属性 getter" +
                    $"（IsSpecialName 为 false），签名不符，兼容已安全跳过。";
                return false;
            }

            // —— 6. 继承关系校验。 ——
            if (!typeof(ThingComp).IsAssignableFrom(compAbilitiesType))
            {
                failureReason =
                    $"{CompAbilitiesTypeName} 不是 Verse.ThingComp 的子类，签名不符。";
                return false;
            }

            if (!shieldCompType!.IsAssignableFrom(compAbilitiesType))
            {
                failureReason =
                    $"{CompAbilitiesTypeName} 未继承 {CompShieldBubbleTypeName}，继承关系不符。";
                return false;
            }

            if (!typeof(ThingComp).IsAssignableFrom(shieldCompType))
            {
                failureReason =
                    $"{CompShieldBubbleTypeName} 不是 Verse.ThingComp 的子类，签名不符。";
                return false;
            }

            if (!typeof(CompProperties).IsAssignableFrom(shieldPropsType))
            {
                failureReason =
                    $"{CompPropertiesShieldBubbleTypeName} 不是 Verse.CompProperties 的子类，签名不符。";
                return false;
            }

            if (!typeof(Hediff).IsAssignableFrom(hediffPsycastAbilitiesType))
            {
                failureReason =
                    $"{HediffPsycastAbilitiesTypeName} 不是 Verse.Hediff 的子类，签名不符。";
                return false;
            }

            if (!typeof(ITab).IsAssignableFrom(itabPsycastsType))
            {
                failureReason =
                    $"{ITabPsycastsTypeName} 不是 Verse.ITab 的子类，签名不符。";
                return false;
            }

            // —— 7. 构造函数精确解析。 ——
            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueConstructor(
                    shieldPropsType!,
                    Type.EmptyTypes,
                    out ConstructorInfo? shieldPropsCtor,
                    out string ctorFailure))
            {
                failureReason = ctorFailure;
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueConstructor(
                    itabPsycastsType!,
                    Type.EmptyTypes,
                    out ConstructorInfo? itabCtor,
                    out ctorFailure))
            {
                failureReason = ctorFailure;
                return false;
            }

            // —— 8. 实例方法精确解析：Hediff_PsycastAbilities.InitializeFromPsylink(Hediff_Psylink) ——
            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(
                    vpeMod,
                    HediffPsycastAbilitiesTypeName,
                    "InitializeFromPsylink",
                    typeof(void),
                    new[] { typeof(Hediff_Psylink) },
                    out MethodInfo? initializeFromPsylinkMethod,
                    out string methodFailure))
            {
                failureReason = methodFailure;
                return false;
            }

            // —— 9. CompProperties_ShieldBubble 字段精确解析（Instance | Public | DeclaredOnly）。 ——
            const BindingFlags fieldFlags =
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.DeclaredOnly;

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueField(
                    shieldPropsType!,
                    "blockRangedAttack",
                    typeof(bool),
                    fieldFlags,
                    out FieldInfo? blockRangedAttackField,
                    out string fieldFailure))
            {
                failureReason = fieldFailure;
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueField(
                    shieldPropsType!,
                    "blockMeleeAttack",
                    typeof(bool),
                    fieldFlags,
                    out FieldInfo? blockMeleeAttackField,
                    out fieldFailure))
            {
                failureReason = fieldFailure;
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueField(
                    shieldPropsType!,
                    "showWhenDrafted",
                    typeof(bool),
                    fieldFlags,
                    out FieldInfo? showWhenDraftedField,
                    out fieldFailure))
            {
                failureReason = fieldFailure;
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueField(
                    shieldPropsType!,
                    "showOnHostiles",
                    typeof(bool),
                    fieldFlags,
                    out FieldInfo? showOnHostilesField,
                    out fieldFailure))
            {
                failureReason = fieldFailure;
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueField(
                    shieldPropsType!,
                    "showOnNeutralInCombat",
                    typeof(bool),
                    fieldFlags,
                    out FieldInfo? showOnNeutralInCombatField,
                    out fieldFailure))
            {
                failureReason = fieldFailure;
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueField(
                    shieldPropsType!,
                    "shieldTexPath",
                    typeof(string),
                    fieldFlags,
                    out FieldInfo? shieldTexPathField,
                    out fieldFailure))
            {
                failureReason = fieldFailure;
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueField(
                    shieldPropsType!,
                    "minShieldSize",
                    typeof(float),
                    fieldFlags,
                    out FieldInfo? minShieldSizeField,
                    out fieldFailure))
            {
                failureReason = fieldFailure;
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueField(
                    shieldPropsType!,
                    "maxShieldSize",
                    typeof(float),
                    fieldFlags,
                    out FieldInfo? maxShieldSizeField,
                    out fieldFailure))
            {
                failureReason = fieldFailure;
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueField(
                    shieldPropsType!,
                    "shieldColor",
                    typeof(Color),
                    fieldFlags,
                    out FieldInfo? shieldColorField,
                    out fieldFailure))
            {
                failureReason = fieldFailure;
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueField(
                    shieldPropsType!,
                    "EnergyLossPerDamage",
                    typeof(float),
                    fieldFlags,
                    out FieldInfo? energyLossPerDamageField,
                    out fieldFailure))
            {
                failureReason = fieldFailure;
                return false;
            }

            targets = new ResolvedTargets(
                compAbilitiesType!,
                shieldCompType!,
                shieldPropsType!,
                hediffPsycastAbilitiesType!,
                itabPsycastsType!,
                shieldPropsCtor!,
                itabCtor!,
                initializeFromPsylinkMethod!,
                blockRangedAttackField!,
                blockMeleeAttackField!,
                showWhenDraftedField!,
                showOnHostilesField!,
                showOnNeutralInCombatField!,
                shieldTexPathField!,
                minShieldSizeField!,
                maxShieldSizeField!,
                shieldColorField!,
                energyLossPerDamageField!,
                vefAbilityType!,
                abilityShowGizmoOnPawnMethod!,
                abilityAutoCastGetter!);
            return true;
        }

        private static bool TryResolveOwnHarmonyTargets(
            out MethodInfo? ensureRoleStateMethod,
            out MethodInfo? tryRestorePositiveSourcesMethod,
            out MethodInfo? tryGainPsylinkLevelMethod,
            out string failureReason)
        {
            ensureRoleStateMethod = null;
            tryRestorePositiveSourcesMethod = null;
            tryGainPsylinkLevelMethod = null;
            failureReason = string.Empty;

            ensureRoleStateMethod =
                typeof(MechanoidMechanitorRoleUtility).GetMethod(
                    nameof(MechanoidMechanitorRoleUtility.EnsureRoleState),
                    BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly);
            if (ensureRoleStateMethod == null
                || !ensureRoleStateMethod.IsStatic
                || ensureRoleStateMethod.ReturnType != typeof(void))
            {
                failureReason =
                    "本项目的 MechanoidMechanitorRoleUtility.EnsureRoleState(Pawn) 无法解析" +
                    "或签名不符（应为 static void），兼容已安全跳过。";
                return false;
            }

            ParameterInfo[] ensureParams = ensureRoleStateMethod.GetParameters();
            if (ensureParams.Length != 1
                || ensureParams[0].ParameterType != typeof(Pawn))
            {
                failureReason =
                    "本项目的 MechanoidMechanitorRoleUtility.EnsureRoleState(Pawn) 参数签名不符" +
                    "（应为单个 Verse.Pawn），兼容已安全跳过。";
                return false;
            }

            tryRestorePositiveSourcesMethod =
                typeof(MechanoidMechanitorPostLoadSafetyCoordinator).GetMethod(
                    "TryRestorePositiveSources",
                    BindingFlags.Static
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly);
            if (tryRestorePositiveSourcesMethod == null
                || !tryRestorePositiveSourcesMethod.IsStatic
                || tryRestorePositiveSourcesMethod.ReturnType != typeof(bool)
                || tryRestorePositiveSourcesMethod.GetParameters().Length != 0)
            {
                failureReason =
                    "本项目的 MechanoidMechanitorPostLoadSafetyCoordinator" +
                    ".TryRestorePositiveSources() 无法解析或签名不符" +
                    "（应为 private static bool 且无参数），兼容已安全跳过。";
                return false;
            }

            // 灵能中枢即时补齐目标：PsychicCoreUtility.TryGainPsylinkLevel(Pawn)。
            // 灵能中枢的 PostAdd / ChangeLevel 通过它获得或升级启灵神经；
            // 本模块在该方法两侧安装 Prefix / Postfix，为机械族机械师即时补齐 VPE 灵能状态。
            tryGainPsylinkLevelMethod =
                typeof(PsychicCoreUtility).GetMethod(
                    nameof(PsychicCoreUtility.TryGainPsylinkLevel),
                    BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly);
            if (tryGainPsylinkLevelMethod == null
                || !tryGainPsylinkLevelMethod.IsStatic
                || tryGainPsylinkLevelMethod.ReturnType != typeof(void))
            {
                failureReason =
                    "本项目的 PsychicCoreUtility.TryGainPsylinkLevel(Pawn) 无法解析" +
                    "或签名不符（应为 static void），兼容已安全跳过。";
                return false;
            }

            ParameterInfo[] psylinkParams = tryGainPsylinkLevelMethod.GetParameters();
            if (psylinkParams.Length != 1
                || psylinkParams[0].ParameterType != typeof(Pawn))
            {
                failureReason =
                    "本项目的 PsychicCoreUtility.TryGainPsylinkLevel(Pawn) 参数签名不符" +
                    "（应为单个 Verse.Pawn），兼容已安全跳过。";
                return false;
            }

            return true;
        }

        private static MethodInfo? GetPatchMethod(string methodName)
        {
            return typeof(VanillaPsycastsExpandedCompatibilityPatches).GetMethod(
                methodName,
                BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly);
        }

        private static bool IsStaticVoidWithSingleParameter(
            MethodInfo method,
            Type parameterType)
        {
            if (!method.IsStatic || method.ReturnType != typeof(void))
            {
                return false;
            }

            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 1
                && parameters[0].ParameterType == parameterType;
        }

        /// <summary>
        /// 校验第五个入口 Transpiler 的签名：static、返回 IEnumerable&lt;CodeInstruction&gt;、
        /// 恰好一个 IEnumerable&lt;CodeInstruction&gt; 参数。不得因校验而静态引用 VEF 类型。
        /// </summary>
        private static bool IsValidTranspiler(MethodInfo method)
        {
            if (!method.IsStatic
                || method.ReturnType != typeof(IEnumerable<CodeInstruction>))
            {
                return false;
            }

            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 1
                && parameters[0].ParameterType == typeof(IEnumerable<CodeInstruction>);
        }

        // ==================== Def 注入 ====================

        private static void InjectAbilitiesIntoMechanoidDefs(
            ResolvedTargets targets,
            InspectTabBase sharedPsycastsTab,
            DefInjectionTracker tracker)
        {
            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                if (def.race == null || !def.race.IsMechanoid)
                {
                    continue;
                }

                if (def.comps == null)
                {
                    def.comps = new List<CompProperties>();
                }

                bool hasAbilitiesComp = ContainsAbilitiesComp(def, targets);
                if (hasAbilitiesComp)
                {
                    tracker.ExistingCompCount++;
                }
                else if (TryAddAbilitiesComp(def, targets, tracker))
                {
                    tracker.AddedCompCount++;
                }
                else
                {
                    // 其他护盾 Comp 冲突：不移除、不替换、不添加 CompAbilities，也不添加页签。
                    tracker.SkippedShieldConflictCount++;
                    continue;
                }

                AddPsycastsTab(def, targets.ITabPsycastsType, sharedPsycastsTab, tracker);
            }

            // 护盾冲突种族只在 DevMode 下输出一次聚合提示，不逐个刷日志。
            if (tracker.SkippedShieldConflictCount > 0 && Prefs.DevMode)
            {
                Log.Message(
                    "[MAP-机械族机械师] 原版灵能拓展兼容："
                    + $"{tracker.SkippedShieldConflictCount} 个机械族定义因已自带其他 VEF 护盾"
                    + " Comp 而跳过，未添加 CompAbilities 与灵能页签。");
            }

        }

        private static bool ContainsAbilitiesComp(ThingDef def, ResolvedTargets targets)
        {
            List<CompProperties>? comps = def.comps;
            if (comps == null)
            {
                return false;
            }

            for (int i = 0; i < comps.Count; i++)
            {
                CompProperties? comp = comps[i];
                if (comp != null
                    && comp.compClass != null
                    && targets.CompAbilitiesType.IsAssignableFrom(comp.compClass))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 为单个机械族 ThingDef 创建并加入 CompAbilities 容器。
        /// 返回 true 表示本轮成功新增；返回 false 表示因其他护盾 Comp 冲突而跳过。
        /// 幂等：调用方已保证不存在 CompAbilities，这里仅负责“新增”或“冲突跳过”两种结果。
        /// </summary>
        private static bool TryAddAbilitiesComp(
            ThingDef def,
            ResolvedTargets targets,
            DefInjectionTracker tracker)
        {
            List<CompProperties>? comps = def.comps;
            if (comps != null)
            {
                for (int i = 0; i < comps.Count; i++)
                {
                    CompProperties? comp = comps[i];
                    if (comp == null)
                    {
                        continue;
                    }

                    // CompAbilities 本身不是护盾冲突（调用方已保证不存在，这里防御性跳过）。
                    if (comp.compClass != null
                        && targets.CompAbilitiesType.IsAssignableFrom(comp.compClass))
                    {
                        continue;
                    }

                    if (targets.ShieldPropertiesType.IsAssignableFrom(comp.GetType())
                        || (comp.compClass != null
                            && targets.ShieldCompType.IsAssignableFrom(comp.compClass)))
                    {
                        return false;
                    }
                }
            }

            object? rawProps;
            try
            {
                rawProps = targets.ShieldPropsCtor.Invoke(null);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "创建 VEF.Apparels.CompProperties_ShieldBubble 实例失败。",
                    ex);
            }

            if (!(rawProps is CompProperties props))
            {
                throw new InvalidOperationException(
                    "VEF.Apparels.CompProperties_ShieldBubble 创建结果无法转换为" +
                    " Verse.CompProperties。");
            }

            props.compClass = targets.CompAbilitiesType;
            ApplyShieldDefaults(props, targets);

            def.comps.Add(props);
            tracker.RecordAddedComp(def, props);

            // 加入 def.comps 后，与 VEF 原生人类初始化保持一致的生命周期回调。
            props.ResolveReferences(def);
            props.PostLoadSpecial(def);

            return true;
        }

        private static void ApplyShieldDefaults(
            CompProperties props,
            ResolvedTargets targets)
        {
            targets.BlockRangedAttackField.SetValue(props, true);
            targets.BlockMeleeAttackField.SetValue(props, false);
            targets.ShowWhenDraftedField.SetValue(props, true);
            targets.ShowOnHostilesField.SetValue(props, true);
            targets.ShowOnNeutralInCombatField.SetValue(props, true);
            targets.ShieldTexPathField.SetValue(props, ShieldTexturePath);
            targets.MinShieldSizeField.SetValue(props, MinShieldSize);
            targets.MaxShieldSizeField.SetValue(props, MaxShieldSize);
            targets.ShieldColorField.SetValue(props, new Color(1f, 1f, 1f, 1f));
            targets.EnergyLossPerDamageField.SetValue(props, EnergyLossPerDamage);
        }

        private static void AddPsycastsTab(
            ThingDef def,
            Type tabType,
            InspectTabBase sharedTab,
            DefInjectionTracker tracker)
        {
            def.inspectorTabs ??= new List<Type>();
            bool addedType = false;
            if (!def.inspectorTabs.Contains(tabType))
            {
                def.inspectorTabs.Add(tabType);
                addedType = true;
            }

            def.inspectorTabsResolved ??= new List<InspectTabBase>();
            bool addedResolved = false;
            if (!ContainsTabOfType(def.inspectorTabsResolved, tabType))
            {
                def.inspectorTabsResolved.Add(sharedTab);
                addedResolved = true;
            }

            if (addedType || addedResolved)
            {
                tracker.AddedTabCount++;
                tracker.RecordAddedTab(
                    def,
                    addedType ? tabType : null,
                    addedResolved ? sharedTab : null);
            }
        }

        private static bool ContainsTabOfType(
            List<InspectTabBase> tabs,
            Type tabType)
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                InspectTabBase? tab = tabs[i];
                if (tab != null && tab.GetType() == tabType)
                {
                    return true;
                }
            }

            return false;
        }

        // ==================== 补丁安装 ====================

        private static void InstallPatches(
            Harmony harmony,
            MethodInfo ensureRoleStateMethod,
            MethodInfo tryRestorePositiveSourcesMethod,
            MethodInfo tryGainPsylinkLevelMethod,
            MethodInfo abilityShowGizmoOnPawnMethod,
            MethodInfo abilityAutoCastGetter,
            MethodInfo postfixEnsureRoleState,
            MethodInfo postfixTryRestore,
            MethodInfo prefixTryGainPsylinkLevel,
            MethodInfo postfixTryGainPsylinkLevel,
            MethodInfo transpilerAbilityShowGizmoOnPawn,
            MethodInfo transpilerAbilityAutoCast)
        {
            try
            {
                // 六个补丁入口在统一 try 中依次显式安装；任一安装异常时，
                // 由下方 catch 通过具体 original / patch MethodInfo 定向撤销全部本模块补丁，
                // 不调用 UnpatchAll，不移除其他 MOD 或模块的补丁。
                harmony.Patch(
                    ensureRoleStateMethod,
                    postfix: new HarmonyMethod(postfixEnsureRoleState));
                harmony.Patch(
                    tryRestorePositiveSourcesMethod,
                    postfix: new HarmonyMethod(postfixTryRestore));
                harmony.Patch(
                    tryGainPsylinkLevelMethod,
                    prefix: new HarmonyMethod(prefixTryGainPsylinkLevel),
                    postfix: new HarmonyMethod(postfixTryGainPsylinkLevel));
                harmony.Patch(
                    abilityShowGizmoOnPawnMethod,
                    transpiler: new HarmonyMethod(transpilerAbilityShowGizmoOnPawn));
                harmony.Patch(
                    abilityAutoCastGetter,
                    transpiler: new HarmonyMethod(transpilerAbilityAutoCast));
            }
            catch (Exception)
            {
                // 定向撤销六个本模块补丁入口：使用具体 original 与具体 patch MethodInfo，
                // 不调用 UnpatchAll，不删除其他 MOD 的补丁。
                // UnpatchQuietly 自身异常被吞掉，避免掩盖最初的安装异常。
                // 即使第六个入口自身安装失败，也必须先撤销此前已安装的五个入口，
                // 避免留下半兼容状态。
                UnpatchQuietly(harmony, ensureRoleStateMethod, postfixEnsureRoleState);
                UnpatchQuietly(harmony, tryRestorePositiveSourcesMethod, postfixTryRestore);
                UnpatchQuietly(harmony, tryGainPsylinkLevelMethod, prefixTryGainPsylinkLevel);
                UnpatchQuietly(harmony, tryGainPsylinkLevelMethod, postfixTryGainPsylinkLevel);
                UnpatchQuietly(harmony, abilityShowGizmoOnPawnMethod, transpilerAbilityShowGizmoOnPawn);
                UnpatchQuietly(harmony, abilityAutoCastGetter, transpilerAbilityAutoCast);
                throw;
            }
        }

        /// <summary>
        /// 定向撤销单个补丁的极小辅助方法。
        /// 只在 InstallPatches 的失败回滚路径中使用：具体 original 与具体 patch 一一对应，
        /// 绝不调用 UnpatchAll；回滚自身异常被吞掉，避免掩盖最初的安装异常。
        /// </summary>
        private static void UnpatchQuietly(
            Harmony harmony,
            MethodInfo original,
            MethodInfo patch)
        {
            try
            {
                harmony.Unpatch(original, patch);
            }
            catch
            {
                // 定向回滚失败不掩盖最初的安装异常。
            }
        }

        // ==================== 回滚追踪 ====================

        /// <summary>
        /// 记录本轮为各 ThingDef 新增的 CompProperties / 页签类型 / 页签实例。
        /// Rollback 只移除本轮新增内容，绝不删除原本已经存在的内容。
        /// </summary>
        private sealed class DefInjectionTracker
        {
            public int AddedCompCount;
            public int ExistingCompCount;
            public int SkippedShieldConflictCount;
            public int AddedTabCount;

            private readonly List<DefInjectionEntry> entries =
                new List<DefInjectionEntry>();

            public void RecordAddedComp(ThingDef def, CompProperties addedComp)
            {
                FindOrCreate(def).AddedComp = addedComp;
            }

            public void RecordAddedTab(
                ThingDef def,
                Type? addedTabType,
                InspectTabBase? addedTabResolved)
            {
                DefInjectionEntry entry = FindOrCreate(def);
                if (addedTabType != null)
                {
                    entry.AddedTabType = addedTabType;
                }

                if (addedTabResolved != null)
                {
                    entry.AddedTabResolved = addedTabResolved;
                }
            }

            private DefInjectionEntry FindOrCreate(ThingDef def)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    if (ReferenceEquals(entries[i].Def, def))
                    {
                        return entries[i];
                    }
                }

                DefInjectionEntry entry = new DefInjectionEntry { Def = def };
                entries.Add(entry);
                return entry;
            }

            public void Rollback()
            {
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    entries[i].Rollback();
                }

                entries.Clear();
            }

            private sealed class DefInjectionEntry
            {
                public ThingDef Def = null!;

                public CompProperties? AddedComp;

                public Type? AddedTabType;

                public InspectTabBase? AddedTabResolved;

                public void Rollback()
                {
                    TryRemove(
                        () =>
                        {
                            if (AddedComp != null
                                && Def.comps != null
                                && Def.comps.Contains(AddedComp))
                            {
                                Def.comps.Remove(AddedComp);
                            }
                        });

                    TryRemove(
                        () =>
                        {
                            if (AddedTabType != null
                                && Def.inspectorTabs != null
                                && Def.inspectorTabs.Contains(AddedTabType))
                            {
                                Def.inspectorTabs.Remove(AddedTabType);
                            }
                        });

                    TryRemove(
                        () =>
                        {
                            if (AddedTabResolved != null
                                && Def.inspectorTabsResolved != null
                                && Def.inspectorTabsResolved.Contains(AddedTabResolved))
                            {
                                Def.inspectorTabsResolved.Remove(AddedTabResolved);
                            }
                        });
                }

                private static void TryRemove(Action action)
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(
                            "[MAP-机械族机械师] 原版灵能拓展兼容：回滚新增 Def 内容时发生异常："
                            + ex);
                    }
                }
            }
        }

        // ==================== 解析结果缓存 ====================

        private sealed class ResolvedTargets
        {
            public ResolvedTargets(
                Type compAbilitiesType,
                Type shieldCompType,
                Type shieldPropertiesType,
                Type hediffPsycastAbilitiesType,
                Type itabPsycastsType,
                ConstructorInfo shieldPropsCtor,
                ConstructorInfo itabCtor,
                MethodInfo initializeFromPsylinkMethod,
                FieldInfo blockRangedAttackField,
                FieldInfo blockMeleeAttackField,
                FieldInfo showWhenDraftedField,
                FieldInfo showOnHostilesField,
                FieldInfo showOnNeutralInCombatField,
                FieldInfo shieldTexPathField,
                FieldInfo minShieldSizeField,
                FieldInfo maxShieldSizeField,
                FieldInfo shieldColorField,
                FieldInfo energyLossPerDamageField,
                Type vefAbilityType,
                MethodInfo abilityShowGizmoOnPawnMethod,
                MethodInfo abilityAutoCastGetter)
            {
                CompAbilitiesType = compAbilitiesType;
                ShieldCompType = shieldCompType;
                ShieldPropertiesType = shieldPropertiesType;
                HediffPsycastAbilitiesType = hediffPsycastAbilitiesType;
                ITabPsycastsType = itabPsycastsType;
                ShieldPropsCtor = shieldPropsCtor;
                ITabCtor = itabCtor;
                InitializeFromPsylinkMethod = initializeFromPsylinkMethod;
                BlockRangedAttackField = blockRangedAttackField;
                BlockMeleeAttackField = blockMeleeAttackField;
                ShowWhenDraftedField = showWhenDraftedField;
                ShowOnHostilesField = showOnHostilesField;
                ShowOnNeutralInCombatField = showOnNeutralInCombatField;
                ShieldTexPathField = shieldTexPathField;
                MinShieldSizeField = minShieldSizeField;
                MaxShieldSizeField = maxShieldSizeField;
                ShieldColorField = shieldColorField;
                EnergyLossPerDamageField = energyLossPerDamageField;
                VefAbilityType = vefAbilityType;
                AbilityShowGizmoOnPawnMethod = abilityShowGizmoOnPawnMethod;
                AbilityAutoCastGetter = abilityAutoCastGetter;
            }

            public Type CompAbilitiesType { get; }

            public Type ShieldCompType { get; }

            public Type ShieldPropertiesType { get; }

            public Type HediffPsycastAbilitiesType { get; }

            public Type ITabPsycastsType { get; }

            public ConstructorInfo ShieldPropsCtor { get; }

            public ConstructorInfo ITabCtor { get; }

            public MethodInfo InitializeFromPsylinkMethod { get; }

            public FieldInfo BlockRangedAttackField { get; }

            public FieldInfo BlockMeleeAttackField { get; }

            public FieldInfo ShowWhenDraftedField { get; }

            public FieldInfo ShowOnHostilesField { get; }

            public FieldInfo ShowOnNeutralInCombatField { get; }

            public FieldInfo ShieldTexPathField { get; }

            public FieldInfo MinShieldSizeField { get; }

            public FieldInfo MaxShieldSizeField { get; }

            public FieldInfo ShieldColorField { get; }

            public FieldInfo EnergyLossPerDamageField { get; }

            public Type VefAbilityType { get; }

            public MethodInfo AbilityShowGizmoOnPawnMethod { get; }

            /// <summary>
            /// VEF.Abilities.Ability.get_AutoCast 实例属性 getter 的精确解析结果。
            /// 由第六个兼容入口 Transpiler 原地替换其中唯一一次
            /// Pawn.get_IsColonistPlayerControlled 调用，使玩家阵营正式机械族机械师
            /// 也读取 autoCast 字段，从而获得玩家可切换的自动释放开关。
            /// </summary>
            public MethodInfo AbilityAutoCastGetter { get; }
        }
    }
}
