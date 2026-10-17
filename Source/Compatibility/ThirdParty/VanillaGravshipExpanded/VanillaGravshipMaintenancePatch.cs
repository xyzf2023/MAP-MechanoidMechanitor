using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.VanillaGravshipExpanded
{
    internal static class VanillaGravshipMaintenancePatch
    {
        internal static bool Enabled;
        private static WorkGiverDef? maintenanceWorkDef;
        private static FieldInfo? astronautField;

        internal static void Configure(WorkGiverDef workDef, FieldInfo field)
        {
            maintenanceWorkDef = workDef;
            astronautField = field;
        }

        internal static bool IsColonyMechWithoutMaintenanceAuthorization(Pawn pawn, WorkGiver giver)
        {
            bool original = pawn.IsColonyMech;
            if (!original) return false;

            return !(Enabled
                && Scribe.mode == LoadSaveMode.Inactive
                && !MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferPositiveRestore
                && GameComponent_MechanoidMechanitorRegistry.IsPawnAliveAndInitialized(pawn)
                && pawn.health?.capacities != null
                && pawn.IsColonyMechPlayerControlled && !pawn.Downed
                && pawn.HostFaction == null && !pawn.IsPrisoner && !pawn.IsSlave
                && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn)
                && maintenanceWorkDef != null && ReferenceEquals(giver.def, maintenanceWorkDef)
                && MechanoidMechanitorCapabilityUtility.AllowsWorkGiver(pawn, giver.def)
                && !pawn.WorkTypeIsDisabled(maintenanceWorkDef.workType)
                && !pawn.WorkTagIsDisabled(maintenanceWorkDef.workTags)
                && giver.MissingRequiredCapacity(pawn) == null);
        }

        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            MethodInfo? getter = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.IsColonyMech));
            FieldInfo? pawnDef = AccessTools.Field(typeof(Thing), nameof(Thing.def));
            MethodInfo? helper = AccessTools.DeclaredMethod(typeof(VanillaGravshipMaintenancePatch),
                nameof(IsColonyMechWithoutMaintenanceAuthorization), new[] { typeof(Pawn), typeof(WorkGiver) });
            if (getter == null || pawnDef == null || astronautField == null || helper == null
                || !helper.IsStatic || helper.ReturnType != typeof(bool))
                throw new InvalidOperationException("VGE 维护：无法解析机械体身份门槛及其兼容 helper。");

            // 已核对 1.6 上游：pawn.IsColonyMech && pawn.def != VGE_Astronaut 时返回 false。
            // 只接受唯一、直接读取 pawn 的短路分支，两个成功分支必须汇合到同一位置。
            List<int> significant = new List<int>();
            int getterCount = 0, astronautCount = 0, match = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode != OpCodes.Nop) significant.Add(i);
                if (codes[i].Calls(getter)) getterCount++;
                if (codes[i].opcode == OpCodes.Ldsfld && Equals(codes[i].operand, astronautField)) astronautCount++;
            }
            for (int n = 1; n + 7 < significant.Count; n++)
            {
                int index = significant[n];
                if (!codes[index].Calls(getter)) continue;
                CodeInstruction branch = codes[significant[n + 1]];
                CodeInstruction equal = codes[significant[n + 5]];
                if (codes[significant[n - 1]].opcode != OpCodes.Ldarg_1
                    || (branch.opcode != OpCodes.Brfalse && branch.opcode != OpCodes.Brfalse_S)
                    || codes[significant[n + 2]].opcode != OpCodes.Ldarg_1
                    || codes[significant[n + 3]].opcode != OpCodes.Ldfld
                    || !Equals(codes[significant[n + 3]].operand, pawnDef)
                    || codes[significant[n + 4]].opcode != OpCodes.Ldsfld
                    || !Equals(codes[significant[n + 4]].operand, astronautField)
                    || (equal.opcode != OpCodes.Beq && equal.opcode != OpCodes.Beq_S)
                    || branch.operand is not Label destination || !Equals(equal.operand, destination)
                    || codes[significant[n + 6]].opcode != OpCodes.Ldc_I4_0
                    || codes[significant[n + 7]].opcode != OpCodes.Ret)
                    continue;

                int destinationIndex = codes.FindIndex(c => c.labels.Contains(destination));
                if (destinationIndex <= significant[n + 7]) continue;
                // 不允许跳转直接进入条件内部或跨越异常块；链首元数据完整保留。
                for (int i = significant[n - 1]; i <= significant[n + 7]; i++)
                {
                    if (codes[i].blocks.Count != 0 || (i > significant[n - 1] && codes[i].labels.Count != 0))
                        throw new InvalidOperationException("VGE 维护：身份门槛内部的分支或异常块已变化。");
                }
                match = index;
            }
            if (getterCount != 1 || astronautCount != 1 || match < 0)
                throw new InvalidOperationException(
                    $"VGE 维护：非宇航机械体门槛不再唯一或短路结构已变化（机械体判断 {getterCount}，宇航机械体引用 {astronautCount}）。");

            // 原有 Pawn 留在栈上，补入当前 WorkGiver；其余分支、目标检查和派工指令不变。
            codes[match].opcode = OpCodes.Call;
            codes[match].operand = helper;
            codes.Insert(match, new CodeInstruction(OpCodes.Ldarg_0));
            return codes;
        }
    }
}
