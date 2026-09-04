using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MAP_MechanoidMechanitor;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.GlitterworldDestroyer5Expansion
{
    /// <summary>
    /// GlitterworldDestroyer5Expansion 兼容补丁：仅在扩展 MOD 已加载、且本 MOD 的兼容模块
    /// 动态确认全部精确目标后才安装。本文件只“扩展使用者身份门槛”与“自我安装时改用普通 Wait”，
    /// 不复制或重写第三方 JobDriver，不直接 AddHediff，不给普通玩家机械族放权。
    /// </summary>
    internal static class GlitterworldDestroyer5ExpansionInstallationPatch
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] 第三方兼容（GlitterworldDestroyer5Expansion）：";

        // 稳定的 ErrorOnce 键，避免与其他模块冲突。
        private const int ErrorKeyDoEffectResolve = unchecked((int)0x4744_0001);
        private const int ErrorKeyDoEffectMatch = unchecked((int)0x4744_0002);
        private const int ErrorKeyDoEffectBlock = unchecked((int)0x4744_0003);
        private const int ErrorKeyWaitResolve = unchecked((int)0x4744_0004);
        private const int ErrorKeyWaitMatch = unchecked((int)0x4744_0005);
        private const int ErrorKeyWaitBlock = unchecked((int)0x4744_0006);

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

        // 目标一：扩展 CompTargetEffect_GiveHediffToPlayMech.DoEffectOn 的使用者身份门控。
        // 仅替换方法内唯一一处 Pawn.get_IsColonistPlayerControlled 调用，保留原指令的 labels。
        internal static IEnumerable<CodeInstruction> TranspilerDoEffectOn(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            MethodInfo? getter = AccessTools.PropertyGetter(
                typeof(Pawn),
                nameof(Pawn.IsColonistPlayerControlled));
            MethodInfo? helper = AccessTools.Method(
                typeof(GlitterworldDestroyer5ExpansionInstallationPatch),
                nameof(IsColonistPlayerControlledOrAuthorizedMechanoidMechanitor));

            if (getter == null || helper == null)
            {
                throw new InvalidOperationException(
                    $"{LogPrefix}无法解析 Pawn.IsColonistPlayerControlled getter 或本 MOD 辅助方法，" +
                    $"补丁未应用。");
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
                throw new InvalidOperationException(
                    $"{LogPrefix}CompTargetEffect_GiveHediffToPlayMech.DoEffectOn 中 " +
                    $"Pawn.IsColonistPlayerControlled 调用预期仅 1 处，实际找到 {matchCount} 处，" +
                    $"结构已变化，兼容安全跳过。");
            }

            if (codes[getterIndex].blocks.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{LogPrefix}DoEffectOn 中 IsColonistPlayerControlled 调用位于 exception block，" +
                    $"无法安全扩展，兼容安全跳过。");
            }

            // 同栈输入（Pawn）、同栈输出（bool），仅替换调用目标，原指令的 labels 自然保留。
            // 显式使用 Call，避免原 getter 调用若以 callvirt 发出时指向静态方法产生语义偏差。
            codes[getterIndex].opcode = OpCodes.Call;
            codes[getterIndex].operand = helper;

            return codes;
        }

        // 目标二：在 JobDriver_GiveHediffToMech.MakeNewToils 状态机 MoveNext 中，
        // 将唯一一处 Toils_General.WaitWith 调用改写为本 MOD 辅助方法；
        // 辅助方法在“自我安装”时改用普通 Wait，消除“otherPawn is the same as toil.actor”警告。
        internal static IEnumerable<CodeInstruction> TranspilerMakeNewToils(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            if (s_captureField == null || s_waitWithMethod == null)
            {
                throw new InvalidOperationException(
                    $"{LogPrefix}Wait With 兼容所需的精确目标未配置（状态机捕获字段或 WaitWith 方法缺失），" +
                    $"补丁未应用。");
            }

            MethodInfo? helper = AccessTools.Method(
                typeof(GlitterworldDestroyer5ExpansionInstallationPatch),
                nameof(CreateExpansionInstallationWait));
            if (helper == null)
            {
                throw new InvalidOperationException(
                    $"{LogPrefix}无法解析本 MOD 的 CreateExpansionInstallationWait 辅助方法，补丁未应用。");
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
                throw new InvalidOperationException(
                    $"{LogPrefix}JobDriver_GiveHediffToMech.MakeNewToils 状态机 MoveNext 中 " +
                    $"Toils_General.WaitWith 调用预期仅 1 处，实际找到 {matchCount} 处，" +
                    $"结构已变化，兼容安全跳过。");
            }

            if (codes[callIndex].blocks.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{LogPrefix}MakeNewToils 状态机中 WaitWith 调用位于 exception block，" +
                    $"无法安全改写，兼容安全跳过。");
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

        // 目标一辅助：原版人类殖民者行为完全保留，仅额外放行合法且可操作的机械族机械师。
        internal static bool IsColonistPlayerControlledOrAuthorizedMechanoidMechanitor(
            Pawn? pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            // 完整保留原版人类殖民者行为。
            if (pawn.IsColonistPlayerControlled)
            {
                return true;
            }

            // 机械族分支：必须是正式机械族机械师，且通过可操作状态门控。
            // 不依赖 IsColonyMech，避免错误放行所有普通玩家机械族。
            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                return false;
            }

            return CompColonistLikeFloatMenuUser.PawnCanUseColonistLikeFloatMenu(pawn);
        }

        // 目标二辅助：自我安装时改用普通 Wait，避免原版 WaitWith 的
        // “otherPawn is the same as toil.actor” 警告；给其他机械族安装仍完整走 WaitWith。
        // 不改变等待时长、不删除后续 WithProgressBarToilDelay / FailOn / tickAction / warmupMote。
        internal static Toil CreateExpansionInstallationWait(
            TargetIndex targetInd,
            int ticks,
            bool useProgressBar,
            bool maintainPosture,
            bool maintainSleep,
            TargetIndex face,
            PathEndMode pathEndMode,
            JobDriver driver)
        {
            if (driver == null || driver.pawn == null || driver.job == null)
            {
                return Toils_General.WaitWith(
                    targetInd, ticks, useProgressBar, maintainPosture,
                    maintainSleep, face, pathEndMode);
            }

            Thing? targetThing = driver.job.GetTarget(targetInd).Thing;
            if (targetThing != null && targetThing == driver.pawn)
            {
                // 自我安装：普通 Wait，执行者 StopDead 并等待完整时长，无“otherPawn is the same as toil.actor”警告。
                return Toils_General.Wait(ticks, face);
            }

            return Toils_General.WaitWith(
                targetInd, ticks, useProgressBar, maintainPosture,
                maintainSleep, face, pathEndMode);
        }
    }
}
