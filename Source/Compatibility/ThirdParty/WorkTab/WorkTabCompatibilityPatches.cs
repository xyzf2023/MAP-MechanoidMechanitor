using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.WorkTab
{
    internal static class WorkTabWorkSettingsPatch
    {
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            MethodInfo humanlike = WorkTabCompatibilityUtility.HumanlikeMethod
                ?? throw new InvalidOperationException("WorkTab：Humanlike 目标尚未配置。");
            MethodInfo helper = AccessTools.DeclaredMethod(typeof(WorkTabCompatibilityUtility),
                nameof(WorkTabCompatibilityUtility.HumanlikeOrMechanitor), new[] { typeof(Pawn) })
                ?? throw new InvalidOperationException("WorkTab：无法解析工作资格 helper。");

            int match = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(humanlike))
                    continue;
                if (match >= 0 || codes[i].opcode != OpCodes.Call)
                    throw new InvalidOperationException("WorkTab：Prefix 的 Humanlike 判断不唯一或调用形式变化。");
                match = i;
            }
            if (match < 0)
                throw new InvalidOperationException("WorkTab：Prefix 的 Humanlike 判断已变化。");

            // 保留原调用的参数栈、标签和异常块，只替换这五个入口中的资格判断。
            codes[match].operand = helper;

            if (__originalMethod.DeclaringType?.FullName == "WorkTab.Pawn_WorkSettings_SetPriority")
            {
                MethodInfo selectedHours = WorkTabCompatibilityUtility.SelectedHoursGetter
                    ?? throw new InvalidOperationException("WorkTab：SelectedHours 目标尚未配置。");
                MethodInfo hoursHelper = AccessTools.DeclaredMethod(typeof(WorkTabDefaultPriorityWritePatch),
                    nameof(WorkTabDefaultPriorityWritePatch.HoursForWrite),
                    new[] { typeof(List<int>), typeof(Pawn_WorkSettings) })
                    ?? throw new InvalidOperationException("WorkTab：无法解析默认优先级时段 helper。");
                int hoursMatch = -1;
                for (int i = 0; i < codes.Count; i++)
                {
                    if (!codes[i].Calls(selectedHours))
                        continue;
                    if (hoursMatch >= 0 || codes[i].opcode != OpCodes.Call || codes[i].blocks.Count != 0)
                        throw new InvalidOperationException("WorkTab：SetPriority 的 SelectedHours 读取结构变化。");
                    hoursMatch = i;
                }
                if (hoursMatch < 0)
                    throw new InvalidOperationException("WorkTab：SetPriority 缺少 SelectedHours 读取。");

                // 此 Prefix 的第一个参数已按签名确认是 Pawn_WorkSettings。
                // 玩家编辑仍使用上游 SelectedHours；系统默认写入使用上游的 null=全天约定。
                codes.Insert(hoursMatch + 1, new CodeInstruction(OpCodes.Ldarg_0));
                codes.Insert(hoursMatch + 2, new CodeInstruction(OpCodes.Call, hoursHelper));
            }
            return codes;
        }
    }

    internal static class WorkTabHourlyRefreshPatch
    {
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            MethodInfo? getter = AccessTools.PropertyGetter(typeof(MapPawns), nameof(MapPawns.FreeColonistsSpawned));
            MethodInfo? helper = AccessTools.DeclaredMethod(typeof(WorkTabCompatibilityUtility),
                nameof(WorkTabCompatibilityUtility.GetHourlyRefreshPawns), new[] { typeof(MapPawns) });
            if (getter == null || getter.ReturnType != typeof(List<Pawn>) || helper == null)
                throw new InvalidOperationException("WorkTab：无法解析小时刷新名单。");

            int match = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(getter))
                    continue;
                if (match >= 0)
                    throw new InvalidOperationException("WorkTab：小时刷新名单读取不唯一。");
                match = i;
            }
            if (match < 0)
                throw new InvalidOperationException("WorkTab：小时刷新名单读取已变化。");

            FieldInfo? hourField = AccessTools.DeclaredField(__originalMethod.DeclaringType, "currentHour");
            MethodInfo? hourGetter = AccessTools.DeclaredMethod(typeof(GenLocalDate),
                nameof(GenLocalDate.HourOfDay), new[] { typeof(Map) });
            if (hourField == null || hourField.IsStatic || hourField.FieldType != typeof(int) || hourGetter == null)
                throw new InvalidOperationException("WorkTab：无法确认小时变化检测字段或方法。");
            int hourWrites = 0;
            int hourWriteIndex = -1;
            int hourReads = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Stfld && Equals(codes[i].operand, hourField))
                {
                    hourWrites++;
                    hourWriteIndex = i;
                }
                if (codes[i].Calls(hourGetter))
                    hourReads++;
            }
            if (hourWrites != 1 || hourWriteIndex >= match || hourReads != 2)
                throw new InvalidOperationException("WorkTab：名单刷新不再位于预期的小时更新路径。");

            codes[match].opcode = OpCodes.Call;
            codes[match].operand = helper;
            return codes;
        }
    }

    internal static class WorkTabDefaultPriorityWritePatch
    {
        // 仅同步调用期间的运行态；嵌套调用和异常均由 Finalizer 恢复，不保留 Pawn。
        [ThreadStatic]
        private static int wholeDayWriteDepth;

        public static void Prefix(Pawn pawn, out int __state)
        {
            __state = wholeDayWriteDepth;
            if (WorkTabCompatibilityUtility.IsPlayerMechanitor(pawn))
                wholeDayWriteDepth++;
        }

        public static void InitializationPrefix(Pawn ___pawn, out int __state) =>
            Prefix(___pawn, out __state);

        public static Exception? Finalizer(Exception? __exception, int __state)
        {
            wholeDayWriteDepth = __state;
            return __exception;
        }

        internal static List<int>? HoursForWrite(List<int> selectedHours, Pawn_WorkSettings settings) =>
            wholeDayWriteDepth > 0
                && WorkTabCompatibilityUtility.CanUseDetailedPriorities(WorkTabCompatibilityUtility.GetPawn(settings))
                ? null
                : selectedHours;
    }
}
