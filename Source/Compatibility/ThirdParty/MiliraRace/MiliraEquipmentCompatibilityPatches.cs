using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.MiliraRace
{
    internal static class MiliraEquipmentCompatibilityPatches
    {
        internal static bool Enabled;
        private static FieldInfo? dressDriverCapture;
        private static MethodInfo? waitWithMethod;

        private static bool CanApply => Enabled
            && Scribe.mode == LoadSaveMode.Inactive
            && !MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferPositiveRestore;

        internal static void Configure(FieldInfo capture, MethodInfo waitWith)
        {
            dressDriverCapture = capture;
            waitWithMethod = waitWith;
        }

        internal static IEnumerable<CodeInstruction> TranspilerDoEffectOn(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            MethodInfo? getter = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.IsColonistPlayerControlled));
            MethodInfo? helper = AccessTools.DeclaredMethod(typeof(MiliraEquipmentCompatibilityPatches),
                nameof(IsColonistOrAuthorizedMechanitor), new[] { typeof(Pawn) });
            if (getter == null || helper == null || !helper.IsStatic || helper.ReturnType != typeof(bool))
                throw new InvalidOperationException("Milira 换装：无法解析操作者身份 getter 或兼容 helper。");

            int index = RequireUniqueCall(codes, getter, "DoEffectOn 的操作者身份判断");
            int pawnLoad = index - 1;
            while (pawnLoad >= 0 && codes[pawnLoad].opcode == OpCodes.Nop) pawnLoad--;
            // DoEffectOn 实例方法的 arg1 必须仍是 user，避免上游改为检查其他 Pawn 后误扩展。
            if (pawnLoad < 0 || codes[pawnLoad].opcode != OpCodes.Ldarg_1)
                throw new InvalidOperationException("Milira 换装：身份 getter 不再直接读取 DoEffectOn 的 user 参数。");

            // 同栈输入 Pawn、输出 bool，保留原指令标签及全部后续目标、寻路和预留判断。
            codes[index].opcode = OpCodes.Call;
            codes[index].operand = helper;
            return codes;
        }

        internal static IEnumerable<CodeInstruction> TranspilerDressWait(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            if (dressDriverCapture == null || waitWithMethod == null)
                throw new InvalidOperationException("Milira 换装：穿戴状态机捕获字段或 WaitWith 尚未配置。");
            MethodInfo? helper = AccessTools.DeclaredMethod(typeof(MiliraEquipmentCompatibilityPatches),
                nameof(CreateDressWait), new[] { typeof(TargetIndex), typeof(int), typeof(bool), typeof(bool),
                    typeof(bool), typeof(TargetIndex), typeof(PathEndMode), typeof(JobDriver) });
            if (helper == null || !helper.IsStatic || helper.ReturnType != typeof(Toil))
                throw new InvalidOperationException("Milira 换装：无法解析自我穿戴等待 helper。");

            int index = RequireUniqueCall(codes, waitWithMethod, "穿戴迭代器的 WaitWith");
            CodeInstruction call = codes[index];
            CodeInstruction loadThis = new CodeInstruction(OpCodes.Ldarg_0);
            // 分支入口也必须执行新增的 JobDriver 入栈，否则调用 helper 时会缺少实参。
            loadThis.labels.AddRange(call.labels);
            call.labels.Clear();
            codes.Insert(index, loadThis);
            codes.Insert(index + 1, new CodeInstruction(OpCodes.Ldfld, dressDriverCapture));
            call.opcode = OpCodes.Call;
            call.operand = helper;
            return codes;
        }

        private static int RequireUniqueCall(List<CodeInstruction> codes, MethodInfo target, string description)
        {
            int index = -1;
            int count = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(target)) continue;
                index = i;
                count++;
            }
            if (count != 1)
                throw new InvalidOperationException($"Milira 换装：{description} 调用预期 1 处，实际 {count} 处。");
            if (codes[index].blocks.Count > 0)
                throw new InvalidOperationException($"Milira 换装：{description} 位于异常块边界，无法安全改写。");
            return index;
        }

        internal static bool IsColonistOrAuthorizedMechanitor(Pawn? pawn)
        {
            if (pawn == null) return false;
            if (pawn.IsColonistPlayerControlled) return true;

            // 只扩展正式机械师；能力组件或普通自律机械体不单独获得 Milira 换装资格。
            return CanApply
                && pawn.Spawned
                && !pawn.Discarded
                && !pawn.InMentalState
                && !pawn.IsPrisoner
                && !pawn.IsSlave
                && pawn.HostFaction == null
                && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn)
                && CompColonistLikeFloatMenuUser.PawnCanUseColonistLikeFloatMenu(pawn);
        }

        internal static Toil CreateDressWait(TargetIndex targetInd, int ticks, bool useProgressBar,
            bool maintainPosture, bool maintainSleep, TargetIndex face, PathEndMode pathEndMode, JobDriver driver)
        {
            if (CanApply && driver?.pawn is Pawn pawn && driver.job != null
                && driver.job.GetTarget(targetInd).Thing == pawn
                && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                // 等待自身不调用 ForceWait；原时长、面向、进度条及失败条件均保留。
                Toil toil = Toils_General.Wait(ticks, face);
                toil.FailOnDespawnedOrNull(targetInd);
                toil.FailOnCannotTouch(targetInd, pathEndMode);
                if (useProgressBar) toil.WithProgressBarToilDelay(targetInd);
                return toil;
            }

            return Toils_General.WaitWith(targetInd, ticks, useProgressBar, maintainPosture,
                maintainSleep, face, pathEndMode);
        }
    }
}
