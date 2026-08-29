using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MAP_MechanoidMechanitor;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.DeadManSwitch
{
    /// <summary>
    /// DeadManSwitch 兼容补丁：仅在失能机关本体与 Fortified 均已加载、且本 MOD 的兼容模块
    /// 动态确认全部精确目标后才安装。本文件只“扩展使用者身份门槛”与“机械族机械师给自己安装
    /// 失能机关插件时改用普通 Wait”，不复制或重写第三方 JobDriver，不直接 AddHediff，
    /// 不给普通玩家机械族放权，也不放行其他使用 Fortified 改造框架的 MOD 物品。
    /// </summary>
    internal static class DeadManSwitchModificationInstallationPatch
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] 第三方兼容（DeadManSwitch）：";

        // 失能机关本体真实 packageId；插件来源必须精确等于它才放行。
        private const string DmsPackageId = "Aoba.DeadManSwitch.Core";

        // 稳定的 ErrorOnce 键，避免与其他模块冲突（DM = DeadMan，区别于 Glitterworld 的 0x4744）。
        private const int ErrorKeyDoEffectResolve = unchecked((int)0x444D_0001);
        private const int ErrorKeyDoEffectMatch = unchecked((int)0x444D_0002);
        private const int ErrorKeyDoEffectBlock = unchecked((int)0x444D_0003);
        private const int ErrorKeyWaitResolve = unchecked((int)0x444D_0004);
        private const int ErrorKeyWaitMatch = unchecked((int)0x444D_0005);
        private const int ErrorKeyWaitBlock = unchecked((int)0x444D_0006);

        // 运行期精确目标，由兼容模块在确认全部解析完成后再配置，避免反复模糊反射。
        private static FieldInfo? s_captureField;
        private static MethodInfo? s_waitWithMethod;

        internal static void Configure(
            FieldInfo captureField,
            MethodInfo waitWithMethod)
        {
            s_captureField = captureField;
            s_waitWithMethod = waitWithMethod;
        }

        // 目标一：扩展 Fortified.CompTargetable_AddHediffOnTarget.DoEffect 的使用者身份门控。
        // 仅替换方法内唯一一处 Pawn.get_IsColonistPlayerControlled 调用，保留原指令的 labels。
        // 在 getter 调用之前插入 ldarg.0（当前 CompTargetable 实例，作为 ThingComp 传入），
        // 使辅助方法的栈参数顺序为 (Pawn, ThingComp)。
        internal static IEnumerable<CodeInstruction> TranspilerDoEffect(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            MethodInfo? getter = AccessTools.PropertyGetter(
                typeof(Pawn),
                nameof(Pawn.IsColonistPlayerControlled));
            MethodInfo? helper = AccessTools.Method(
                typeof(DeadManSwitchModificationInstallationPatch),
                nameof(IsColonistPlayerControlledOrAuthorizedDeadManSwitchMechanitor));

            if (getter == null || helper == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}无法解析 Pawn.IsColonistPlayerControlled getter 或本 MOD 辅助方法，" +
                    $"补丁未应用。",
                    ErrorKeyDoEffectResolve);
                return codes;
            }

            int matchCount = 0;
            int getterIndex = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].Calls(getter))
                {
                    matchCount++;
                    getterIndex = i;
                }
            }

            if (matchCount != 1)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}CompTargetable_AddHediffOnTarget.DoEffect 中 " +
                    $"Pawn.IsColonistPlayerControlled 调用预期仅 1 处，实际找到 {matchCount} 处，" +
                    $"结构已变化，兼容安全跳过。",
                    ErrorKeyDoEffectMatch);
                return codes;
            }

            if (codes[getterIndex].blocks.Count > 0)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}DoEffect 中 IsColonistPlayerControlled 调用位于 exception block，" +
                    $"无法安全扩展，兼容安全跳过。",
                    ErrorKeyDoEffectBlock);
                return codes;
            }

            // 在 getter 调用之前插入 ldarg.0（当前 CompTargetable_AddHediffOnTarget 实例，作为 ThingComp）。
            // 原 getter 调用上的分支标签必须转移到新增的 ldarg.0 指令，避免分支跳过参数加载导致栈失衡。
            // 随后把原 getter 调用替换为对辅助方法的 Call；调用前的求值栈从底到顶依次为 Pawn（原 usedBy 加载）、ThingComp（this）。
            CodeInstruction getterCall = codes[getterIndex];
            CodeInstruction loadThis = new CodeInstruction(OpCodes.Ldarg_0);
            loadThis.labels.AddRange(getterCall.labels);
            getterCall.labels.Clear();

            getterCall.opcode = OpCodes.Call;
            getterCall.operand = helper;

            codes.Insert(getterIndex, loadThis);

            return codes;
        }

        // 目标二：在 Fortified.JobDriver_ApplyModification.MakeNewToils 状态机 MoveNext 中，
        // 将唯一一处 Toils_General.WaitWith 调用改写为本 MOD 辅助方法；
        // 辅助方法在“机械族机械师给自己安装失能机关插件”时改用普通 Wait，
        // 消除“otherPawn is the same as toil.actor”警告。
        internal static IEnumerable<CodeInstruction> TranspilerMakeNewToils(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            if (s_captureField == null || s_waitWithMethod == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}Wait 兼容所需的精确目标未配置（状态机捕获字段或 WaitWith 方法缺失），" +
                    $"补丁未应用。",
                    ErrorKeyWaitResolve);
                return codes;
            }

            MethodInfo? helper = AccessTools.Method(
                typeof(DeadManSwitchModificationInstallationPatch),
                nameof(CreateDeadManSwitchInstallationWait));
            if (helper == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}无法解析本 MOD 的 CreateDeadManSwitchInstallationWait 辅助方法，补丁未应用。",
                    ErrorKeyWaitResolve);
                return codes;
            }

            int matchCount = 0;
            int callIndex = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].Calls(s_waitWithMethod))
                {
                    matchCount++;
                    callIndex = i;
                }
            }

            if (matchCount != 1)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}JobDriver_ApplyModification.MakeNewToils 状态机 MoveNext 中 " +
                    $"Toils_General.WaitWith 调用预期仅 1 处，实际找到 {matchCount} 处，" +
                    $"结构已变化，兼容安全跳过。",
                    ErrorKeyWaitMatch);
                return codes;
            }

            if (codes[callIndex].blocks.Count > 0)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}MakeNewToils 状态机中 WaitWith 调用位于 exception block，" +
                    $"无法安全改写，兼容安全跳过。",
                    ErrorKeyWaitBlock);
                return codes;
            }

            CodeInstruction callInstruction = codes[callIndex];
            CodeInstruction loadThis = new CodeInstruction(OpCodes.Ldarg_0);
            CodeInstruction loadField = new CodeInstruction(OpCodes.Ldfld, s_captureField);

            // 原 WaitWith 调用上的分支标签必须转移到新增的首条加载指令，
            // 否则分支会跳过 JobDriver 入栈，导致栈失衡。
            loadThis.labels.AddRange(callInstruction.labels);
            callInstruction.labels.Clear();

            codes.Insert(callIndex, loadThis);
            codes.Insert(callIndex + 1, loadField);

            // callInstruction 对象现已位于 callIndex + 2；opcode 仍为 Call，
            // 仅把目标替换为辅助方法。栈顺序：先压入 7 个 WaitWith 实参，再压入 JobDriver。
            callInstruction.opcode = OpCodes.Call;
            callInstruction.operand = helper;

            return codes;
        }

        // 目标一辅助：原版人类殖民者行为完全保留，仅额外放行“持失能机关本体插件的合法且可操作的机械族机械师”。
        // 不依赖 IsColonyMech / RaceProps.IsMechanoid / Faction 等宽泛条件，避免错误放行普通玩家机械族或其他 Fortified 插件。
        internal static bool IsColonistPlayerControlledOrAuthorizedDeadManSwitchMechanitor(
            Pawn? pawn,
            ThingComp? effectComp)
        {
            if (pawn == null)
            {
                return false;
            }

            // 完整保留原版人类殖民者行为（以及 Fortified 原有 HumanlikeMech 行为）。
            if (pawn.IsColonistPlayerControlled)
            {
                return true;
            }

            // 必须是正式机械族机械师，且通过统一可操作状态门控。
            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                return false;
            }

            if (!CompColonistLikeFloatMenuUser.PawnCanUseColonistLikeFloatMenu(pawn))
            {
                return false;
            }

            // 插件来源必须严格属于失能机关本体；effectComp / parent / def / modContentPack 任一无效则拒绝。
            if (effectComp == null
                || effectComp.parent == null
                || effectComp.parent.def == null
                || effectComp.parent.def.modContentPack == null)
            {
                return false;
            }

            if (!string.Equals(
                    effectComp.parent.def.modContentPack.PackageId,
                    DmsPackageId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        // 目标二辅助：仅在“机械族机械师给自己安装失能机关插件”这一精确组合时改用普通 Wait，
        // 避免原版 WaitWith 的“otherPawn is the same as toil.actor”警告；
        // 其他所有情况（给其他机械族安装、普通殖民者安装、普通机械族自装、其他 Fortified 插件）均返回原始 WaitWith。
        // 不改变等待时长、不删除后续 FailOnDespawnedOrNull / FailOnCannotTouch / WithEffect / 最终安装逻辑。
        internal static Toil CreateDeadManSwitchInstallationWait(
            TargetIndex targetInd,
            int ticks,
            bool useProgressBar,
            bool maintainPosture,
            bool maintainSleep,
            TargetIndex face,
            PathEndMode pathEndMode,
            JobDriver driver)
        {
            // 保守回退：无法确认执行者 / 任务 / 目标时，直接走原版 WaitWith。
            if (driver == null || driver.pawn == null || driver.job == null)
            {
                return Toils_General.WaitWith(
                    targetInd, ticks, useProgressBar, maintainPosture,
                    maintainSleep, face, pathEndMode);
            }

            // TargetIndex.A 是接受插件的机械族；若不可用或并非本人，则不是自我安装。
            Thing? targetThing = driver.job.GetTarget(TargetIndex.A).Thing;
            if (targetThing == null || targetThing != driver.pawn)
            {
                return Toils_General.WaitWith(
                    targetInd, ticks, useProgressBar, maintainPosture,
                    maintainSleep, face, pathEndMode);
            }

            // 执行者必须是正式机械族机械师且通过可操作状态门控。
            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(driver.pawn)
                || !CompColonistLikeFloatMenuUser.PawnCanUseColonistLikeFloatMenu(driver.pawn))
            {
                return Toils_General.WaitWith(
                    targetInd, ticks, useProgressBar, maintainPosture,
                    maintainSleep, face, pathEndMode);
            }

            // TargetIndex.B 是插件物品；JobDriver 在 TryMakePreToilReservations 阶段已将其解析为实际 Thing，
            // MakeNewToils 亦直接引用 TargetIndex.B，因此此处 Thing 必然有效，无需额外 DefName 兜底反射。
            // 插件来源必须属于失能机关本体，否则回退原版 WaitWith（不影响其他 Fortified 插件）。
            Thing? itemThing = driver.job.GetTarget(TargetIndex.B).Thing;
            if (itemThing == null
                || itemThing.def == null
                || itemThing.def.modContentPack == null
                || !string.Equals(
                    itemThing.def.modContentPack.PackageId,
                    DmsPackageId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Toils_General.WaitWith(
                    targetInd, ticks, useProgressBar, maintainPosture,
                    maintainSleep, face, pathEndMode);
            }

            // 满足全部条件：自我安装改用普通 Wait，执行者 StopDead 并等待完整时长，无同体等待警告。
            return Toils_General.Wait(ticks, face);
        }
    }
}
