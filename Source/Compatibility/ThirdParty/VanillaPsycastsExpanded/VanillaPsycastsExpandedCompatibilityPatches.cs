using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MAP_MechanoidMechanitor;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.VanillaPsycastsExpanded
{
    /// <summary>
    /// 原版灵能拓展（VPE）兼容的显式 Harmony 补丁。
    /// 本文件不添加任何 [HarmonyPatch] 特性，不使用 PatchAll；
    /// 全部补丁均由 VanillaPsycastsExpandedCompatibility.Apply
    /// 通过传入的 Harmony 实例显式 harmony.Patch() 安装。
    /// 当前共五个入口：
    /// - Postfix_EnsureRoleState：机械师身份初始化（EnsureRoleState Postfix）；
    /// - Postfix_TryRestorePositiveSources：旧档恢复（TryRestorePositiveSources Postfix）；
    /// - Prefix_TryGainPsylinkLevel：已有启灵神经的升级前修复（TryGainPsylinkLevel Prefix）；
    /// - Postfix_TryGainPsylinkLevel：首次新增启灵神经后的即时修复（TryGainPsylinkLevel Postfix）；
    /// - Transpiler_AbilityShowGizmoOnPawn：把 VEF 原有的“玩家控制殖民者”判定扩充为
    ///   “玩家阵营正式机械族机械师”，其余可见性条件（showUndrafted / Drafted、
    ///   AbilityModExtensions）仍由原方法负责。
    /// </summary>
    internal static class VanillaPsycastsExpandedCompatibilityPatches
    {
        /// <summary>
        /// 即时角色初始化入口：MechanoidMechanitorRoleUtility.EnsureRoleState(Pawn) 的 Postfix。
        /// 覆盖新生成机械师、后天转化机械师，以及其他主动执行 EnsureRoleState 的修复路径。
        /// </summary>
        public static void Postfix_EnsureRoleState(Pawn? pawn)
        {
            VanillaPsycastsExpandedCompatibilityRuntime.EnsurePawnState(pawn);
        }

        /// <summary>
        /// 旧档安全入口：MechanoidMechanitorPostLoadSafetyCoordinator.
        /// TryRestorePositiveSources() 的 Postfix。
        /// 仅当原方法返回 true（正面来源恢复成功）时执行旧档批量修复；
        /// 不改变原方法返回值，不干扰读档安全协调器的重试与死亡保护逻辑；
        /// 自身错误在 Runtime 内部被拦截，不会让 TryRestorePositiveSources 失败。
        /// </summary>
        public static void Postfix_TryRestorePositiveSources(bool __result)
        {
            if (__result)
            {
                VanillaPsycastsExpandedCompatibilityRuntime
                    .EnsureAllRegisteredMechanitors();
            }
        }

        /// <summary>
        /// 灵能中枢升级前修复：PsychicCoreUtility.TryGainPsylinkLevel(Pawn) 的 Prefix。
        /// 在原方法正式执行前调用 Runtime.EnsurePawnState：
        /// - 若 Pawn 已拥有启灵神经但缺少 VPE_PsycastAbilityImplant，先补齐灵拓状态，
        ///   保证随后原方法执行 psylink.ChangeLevel(1) 时，灵拓自己的 ChangeLevel 补丁
        ///   能够取得有效的灵拓状态并正常升级；
        /// - 若 Pawn 尚未拥有启灵神经，EnsurePawnState 会安全返回，不产生副作用。
        /// 所有前置条件判断与异常边界由 Runtime 统一负责，本方法不做任何自行判断。
        /// </summary>
        public static void Prefix_TryGainPsylinkLevel(Pawn? pawn)
        {
            VanillaPsycastsExpandedCompatibilityRuntime.EnsurePawnState(pawn);
        }

        /// <summary>
        /// 灵能中枢完成后的即时修复：PsychicCoreUtility.TryGainPsylinkLevel(Pawn) 的 Postfix。
        /// 在原方法正常完成后再次调用 Runtime.EnsurePawnState：
        /// - 原方法新增启灵神经后立即补齐 VPE_PsycastAbilityImplant，
        ///   让玩家阵营机械族机械师立即满足 VPE 灵能 ITab 的可见条件；
        /// - 若灵拓自己的 Hediff_Psylink.PostAdd 补丁已经成功添加该 Hediff，
        ///   本入口因状态已存在而幂等返回，不重复添加、不重复初始化、不重置灵拓数据；
        /// - 若本次没有成功获得启灵神经，EnsurePawnState 会安全返回。
        /// </summary>
        public static void Postfix_TryGainPsylinkLevel(Pawn? pawn)
        {
            VanillaPsycastsExpandedCompatibilityRuntime.EnsurePawnState(pawn);
        }

        /// <summary>
        /// 第五个兼容入口的 Transpiler 辅助判定。
        /// 把 VEF 原有的 Pawn.get_IsColonistPlayerControlled 调用替换为本静态辅助方法，
        /// 等价扩展为：玩家控制殖民者，或玩家阵营中的正式机械族机械师。
        /// 调用原版 pawn.IsColonistPlayerControlled 不会形成递归，因为本补丁目标是第三方
        /// VEF.Abilities.Ability.ShowGizmoOnPawn，而非 Pawn.IsColonistPlayerControlled getter 本身。
        /// </summary>
        public static bool IsColonistPlayerControlledOrPlayerMechanoidMechanitor(Pawn? pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            if (pawn.IsColonistPlayerControlled)
            {
                return true;
            }

            // 仅玩家阵营中的正式机械族机械师放行；不得只凭 RaceProps.IsMechanoid、
            // 只凭 Faction.IsPlayer 或只凭 CompAbilities 是否存在就放行。
            return pawn.RaceProps.IsMechanoid
                && pawn.Faction != null
                && pawn.Faction.IsPlayer
                && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn);
        }

        /// <summary>
        /// 第五个兼容入口：VEF.Abilities.Ability.ShowGizmoOnPawn 的 Transpiler。
        /// 仅把方法体内唯一一次 Pawn.get_IsColonistPlayerControlled getter 调用
        /// 原地替换为对 IsColonistPlayerControlledOrPlayerMechanoidMechanitor(Pawn) 的 call。
        /// 不插入 / 删除 / 复制任何 Pawn 栈值，栈平衡保持不变；
        /// 其余 IL、短路顺序与可见性判定全部保留。
        /// 找不到唯一 getter 调用时不静默返回原始 IL，而是抛出明确异常，
        /// 由 InstallPatches 统一定向回滚，避免留下半兼容状态。
        /// </summary>
        public static IEnumerable<CodeInstruction> Transpiler_AbilityShowGizmoOnPawn(
            IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo? colonistGetter = AccessTools.PropertyGetter(
                typeof(Pawn),
                nameof(Pawn.IsColonistPlayerControlled));
            if (colonistGetter == null)
            {
                throw new InvalidOperationException(
                    "原版灵能拓展兼容：无法解析 Pawn.get_IsColonistPlayerControlled，" +
                    "无法安全安装 VEF Ability.ShowGizmoOnPawn Transpiler。");
            }

            MethodInfo? helper = AccessTools.Method(
                typeof(VanillaPsycastsExpandedCompatibilityPatches),
                nameof(IsColonistPlayerControlledOrPlayerMechanoidMechanitor),
                new[] { typeof(Pawn) });
            if (helper == null)
            {
                throw new InvalidOperationException(
                    "原版灵能拓展兼容：无法解析辅助方法" +
                    " IsColonistPlayerControlledOrPlayerMechanoidMechanitor(Pawn)，" +
                    "无法安全安装 VEF Ability.ShowGizmoOnPawn Transpiler。");
            }

            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            int matchCount = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                CodeInstruction instruction = codes[i];
                if (instruction.operand is MethodInfo candidate
                    && candidate == colonistGetter)
                {
                    // 原地修改以保留该 CodeInstruction 上已有的 labels 与 exception blocks。
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = helper;
                    matchCount++;
                }
            }

            if (matchCount != 1)
            {
                throw new InvalidOperationException(
                    "目标方法 VEF.Abilities.Ability.ShowGizmoOnPawn 中预期唯一匹配" +
                    " Pawn.get_IsColonistPlayerControlled 调用 1 次，实际匹配 "
                    + matchCount + " 次，兼容不能安全应用。");
            }

            return codes;
        }
    }
}
