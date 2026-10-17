using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.WvcWorkModes
{
    // 1.6 依据：WVCSergkart/MoreMechanoidsWorkModes 43d10d2fd89bbe21b2bf41a7607aa3a5acf765cb。
    // 该版本 csproj 输出至 1.6/Assemblies；当前 main 的共享 Source 已输出至 1.7，不作为版本依据。
    // 两个模式分别注册，安全门槛解析失败时仍可保留普通工作兼充电模式的兼容。
    internal sealed class WvcWorkModesServiceCompatibility : IThirdPartyCompatibilityModule
    {
        private const string WorkModeName = "WVC_WorkAndRecharge";
        private const string SafeModeName = "WVC_SafeWorkAndRecharge";
        private const string EnemyNodeName = "WVC_WorkModes.ThinkNode_ConditionalEnemyOnMap";
        private static MechWorkModeDef? workMode;
        private static MechWorkModeDef? safeMode;
        private static Type? enemyNodeType;
        private static MethodInfo? enemySatisfied;
        // 只缓存 Def 思维树上的节点委托，不保存 Pawn，也不自行创建带有另一份敌人缓存的节点。
        private static readonly Dictionary<ThinkNode, Func<Pawn, bool>?> safetyGates =
            new Dictionary<ThinkNode, Func<Pawn, bool>?>();
        private readonly bool safe;

        public WvcWorkModesServiceCompatibility(bool safe) => this.safe = safe;
        public string ModuleId => safe ? "WvcWorkModes.SafeMechService" : "WvcWorkModes.MechService";
        public string DisplayName => safe ? "WVC：安全工作兼充电整备寻站" : "WVC：工作兼充电整备寻站";
        public string PackageId => "wvc.sergkart.biotech.MoreMechanoidsWorkModes";

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            if (safe)
            {
                safeMode = null;
                enemyNodeType = null;
                enemySatisfied = null;
                safetyGates.Clear();
            }
            else workMode = null;

            ModContentPack? mod = ThirdPartyCompatibilityTargetResolver.FindRunningMod(PackageId);
            if (mod == null)
                return ThirdPartyCompatibilityResult.CreateInactive(ModuleId, DisplayName, PackageId);

            string name = safe ? SafeModeName : WorkModeName;
            MechWorkModeDef? mode = DefDatabase<MechWorkModeDef>.GetNamedSilentFail(name);
            // WVC 可通过设置删除单个模式；不恢复已禁用的 Def，也不接受其他 MOD 的同名 Def。
            if (mode == null)
                return ThirdPartyCompatibilityResult.CreateInactive(ModuleId, DisplayName, PackageId);
            if (mode.defName != name || !ReferenceEquals(mode.modContentPack, mod))
                return Changed("工作模式 Def 的来源与 WVC 不符。");

            if (!safe)
            {
                workMode = mode;
                return Applied("仅为 WVC_WorkAndRecharge 增加现有整备身份资格内的自动寻站权限。");
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueInstanceMethod(mod,
                    EnemyNodeName, "Satisfied", typeof(bool), new[] { typeof(Pawn) },
                    out MethodInfo? method, out string failure)
                || method == null)
                return Changed(failure);
            Type? type = method.DeclaringType;
            if (type == null || !typeof(ThinkNode_Conditional).IsAssignableFrom(type)
                || !method.IsFamily || !method.IsVirtual || method.IsAbstract
                || method.ContainsGenericParameters || method.GetMethodBody() == null)
                return Changed("WVC 敌人条件节点或 protected Boolean Satisfied(Pawn) 签名已变化。");

            // 预先验证标准机械族思维树；实际使用时仍以 Pawn 当前的主思维树为准。
            ThinkNode? root = DefDatabase<ThinkTreeDef>.GetNamedSilentFail("Mechanoid")?.thinkRoot;
            Func<Pawn, bool>? gate = root == null ? null : BindSafetyGate(root, mode, type, method);
            if (gate == null)
                return Changed("未找到唯一的 WVC 安全工作分支及其首个未反转敌人条件节点。");

            enemyNodeType = type;
            enemySatisfied = method;
            safetyGates[root!] = gate;
            safeMode = mode;
            return Applied("WVC_SafeWorkAndRecharge 复用当前主思维树的敌人门槛；危险时拒绝寻站并退出自动整备。");
        }

        // 只能在 CanAutoSeek 已检查机械体身份及全部其他资格后使用，不能授予新身份资格。
        internal static bool AllowsAutoSeek(Pawn pawn, MechWorkModeDef? mode) =>
            mode != null && ((workMode != null && mode == workMode)
                || (safeMode != null && mode == safeMode && IsSafe(pawn)));

        internal static bool ShouldYieldToSafety(Pawn pawn) =>
            safeMode != null && pawn.GetMechControlGroup()?.WorkMode == safeMode
            // MAP 本体模式仍优先使用现有映射，不能被控制组的 WVC 模式覆盖。
            && !MechanoidMechanitorSelfWorkModeUtility.TryGetCurrentMode(pawn, out _)
            && !IsSafe(pawn);

        private static bool IsSafe(Pawn pawn)
        {
            ThinkNode? root = null;
            try
            {
                root = pawn.thinker?.MainThinkNodeRoot;
                if (root == null || safeMode == null || enemyNodeType == null || enemySatisfied == null)
                    return false;
                if (!safetyGates.TryGetValue(root, out Func<Pawn, bool>? gate))
                {
                    gate = BindSafetyGate(root, safeMode, enemyNodeType, enemySatisfied);
                    safetyGates[root] = gate;
                }
                // 直接调用上游的条件，不执行 TryIssueJobPackage，不派发避战/关机任务。
                // 同一节点保留 enableEnemySearching、可达性/威胁判断及 enemyCheckDelay 缓存。
                return gate != null && !gate(pawn);
            }
            catch (Exception ex)
            {
                if (root != null) safetyGates[root] = null;
                Log.ErrorOnce("[MAP-机械族机械师] WVC 安全整备门槛查询失败，已停止该思维树的自动整备：" + ex,
                    879347241);
                return false;
            }
        }

        private static Func<Pawn, bool>? BindSafetyGate(ThinkNode root, MechWorkModeDef mode,
            Type type, MethodInfo method)
        {
            ThinkNode_ConditionalWorkMode? branch = null;
            foreach (ThinkNode node in root.ThisAndChildrenRecursive)
            {
                if (node is not ThinkNode_ConditionalWorkMode candidate || candidate.workMode != mode)
                    continue;
                if (branch != null || candidate.GetType() != typeof(ThinkNode_ConditionalWorkMode)
                    || candidate.invert)
                    return null;
                branch = candidate;
            }
            if (branch?.subNodes == null || branch.subNodes.Count == 0
                || branch.subNodes[0] is not ThinkNode_Conditional enemy
                || enemy.GetType() != type || enemy.invert)
                return null;
            var predicate = (Func<Pawn, bool>)Delegate.CreateDelegate(typeof(Func<Pawn, bool>), enemy, method);
            // 其他 MOD 若替换首节点或反转分支，安全降级，不继续使用旧委托放行。
            return pawn => branch.workMode != mode || branch.invert || branch.subNodes.Count == 0
                || !ReferenceEquals(branch.subNodes[0], enemy) || enemy.invert || predicate(pawn);
        }

        private ThirdPartyCompatibilityResult Applied(string detail) =>
            ThirdPartyCompatibilityResult.CreateApplied(ModuleId, DisplayName, PackageId, detail);

        private ThirdPartyCompatibilityResult Changed(string detail) =>
            ThirdPartyCompatibilityResult.CreateTargetChanged(ModuleId, DisplayName, PackageId, detail);
    }
}
