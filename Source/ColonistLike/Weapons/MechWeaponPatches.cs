using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    internal static class MechWeaponGizmoPatch
    {
        private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (Gizmo gizmo in __result) yield return gizmo;
            foreach (Gizmo gizmo in MechWeaponUtility.GetGizmos(__instance)) yield return gizmo;
        }
    }

    [HarmonyPatch(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.Notify_EquipmentRemoved))]
    internal static class MechWeaponRemovedPatch
    {
        private static void Prefix(Pawn_EquipmentTracker __instance, ThingWithComps eq) =>
            MechWeaponUtility.EquipmentRemoved(__instance.pawn, eq);
    }

    [HarmonyPatch(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.Notify_EquipmentAdded))]
    internal static class MechWeaponAddedPatch
    {
        private static void Postfix(Pawn_EquipmentTracker __instance, ThingWithComps eq) =>
            MechWeaponUtility.EquipmentAdded(__instance.pawn, eq);
    }

    [HarmonyPatch(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.MakeRoomFor),
        new[] { typeof(ThingWithComps), typeof(ThingWithComps) }, new[] { ArgumentType.Normal, ArgumentType.Out })]
    internal static class MechWeaponMakeRoomPatch
    {
        private static bool Prefix(Pawn_EquipmentTracker __instance, ThingWithComps eq, out ThingWithComps dropped)
        {
            dropped = null!;
            Pawn pawn = __instance.pawn;
            GameComponent_MechWeaponRegistry.Ensure(pawn);
            // 不提前销毁旧专武。紧接着的 AddEquipment 负责完整替换及失败回滚。
            return !(MechWeaponUtility.IsManaged(pawn) && __instance.Primary != null
                && GameComponent_MechWeaponRegistry.Get(pawn) != null
                && MechWeaponUtility.IsBuiltInFor(pawn, __instance.Primary.def)
                && MechWeaponUtility.CanUseOrdinary(pawn, eq));
        }
    }

    [HarmonyPatch(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.AddEquipment))]
    internal static class MechWeaponAddPatch
    {
        private static bool Prefix(Pawn_EquipmentTracker __instance, ThingWithComps newEq)
        {
            Pawn pawn = __instance.pawn;
            if (!MechWeaponUtility.IsManaged(pawn)) return true;
            GameComponent_MechWeaponRegistry.Ensure(pawn);
            if (__instance.Contains(newEq)) return false;
            MechWeaponRecord? record = GameComponent_MechWeaponRegistry.Get(pawn);
            if (record == null || record.ControlledChanges > 0 || __instance.Primary == null
                || !MechWeaponUtility.IsBuiltInFor(pawn, __instance.Primary.def)
                || !MechWeaponUtility.CanUseOrdinary(pawn, newEq)) return true;
            MechWeaponUtility.EquipOrdinary(pawn, newEq);
            return false;
        }
    }

    [HarmonyPatch(typeof(MechRepairUtility), nameof(MechRepairUtility.IsMissingWeapon))]
    internal static class MechWeaponMissingPatch
    {
        private static bool Prefix(Pawn mech, ref bool __result)
        {
            if (!MechWeaponUtility.IsManaged(mech) || Scribe.mode != LoadSaveMode.Inactive) return true;
            __result = MechWeaponUtility.Missing(mech);
            return false;
        }
    }

    [HarmonyPatch(typeof(MechRepairUtility), nameof(MechRepairUtility.GenerateWeapon))]
    internal static class MechWeaponRepairPatch
    {
        private static bool Prefix(Pawn mech)
        {
            if (!MechWeaponUtility.IsManaged(mech)) return true;
            MechWeaponUtility.RepairWeapon(mech);
            return false;
        }
    }

    /// <summary>只替换持有者级的销毁标志读取，保留各原版掉落路径的其余判断。</summary>
    [HarmonyPatch]
    internal static class MechWeaponGearDestructionPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Pawn), nameof(Pawn.DropAndForbidEverything));
            yield return AccessTools.Method(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.Notify_PawnSpawned));
            yield return AccessTools.Method(typeof(Pawn_HealthTracker), "CheckForStateChange");
            yield return AccessTools.Method(typeof(ITab_Pawn_Gear), "DrawThingRow");
        }

        internal static bool DestroyGear(Pawn pawn) => pawn.kindDef.destroyGearOnDrop && !MechWeaponUtility.IsManaged(pawn);
        internal static bool LockGear(Pawn pawn, Thing thing) => MechWeaponUtility.IsManaged(pawn)
            ? MechWeaponUtility.IsBuiltIn(thing.def) : pawn.kindDef.destroyGearOnDrop;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var codes = new List<CodeInstruction>(instructions);
            FieldInfo kind = AccessTools.Field(typeof(Pawn), nameof(Pawn.kindDef));
            FieldInfo destroy = AccessTools.Field(typeof(PawnKindDef), nameof(PawnKindDef.destroyGearOnDrop));
            bool gearTab = __originalMethod.DeclaringType == typeof(ITab_Pawn_Gear);
            int count = 0;
            for (int i = 0; i + 1 < codes.Count; i++)
                if (codes[i].LoadsField(kind) && codes[i + 1].LoadsField(destroy)) count++;
            if (count != 1)
            {
                Log.Error($"[MAP-机械族机械师] 专武适配：{__originalMethod} 的 destroyGearOnDrop 读取预期 1 处，实际 {count}，保留原方法。");
                return codes;
            }
            for (int i = 0; i + 1 < codes.Count; i++)
            {
                if (!codes[i].LoadsField(kind) || !codes[i + 1].LoadsField(destroy)) continue;
                // 原栈上保留 Pawn；装备栏额外读取当前行的 Thing（实例参数 3）。
                codes[i].opcode = gearTab ? OpCodes.Ldarg_3 : OpCodes.Nop;
                codes[i].operand = null;
                codes[i + 1].opcode = OpCodes.Call;
                codes[i + 1].operand = AccessTools.Method(typeof(MechWeaponGearDestructionPatch), gearTab ? nameof(LockGear) : nameof(DestroyGear));
            }
            return codes;
        }
    }

    [HarmonyPatch(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.TryDropEquipment))]
    internal static class MechWeaponDropPatch
    {
        private static bool Prefix(Pawn_EquipmentTracker __instance, ThingWithComps eq,
            ref ThingWithComps resultingEq, ref bool __result)
        {
            if (!MechWeaponUtility.IsBuiltIn(eq.def)) return true;
            // 合体借出的专武同样不能成为可拾取物；强制掉落视为部件丢失。
            if (!MechWeaponUtility.IsManaged(__instance.pawn)
                && GameComponent_MechWeaponRegistry.OwnerOf(eq) == null) return true;
            resultingEq = null!;
            __instance.DestroyEquipment(eq);
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(FloatMenuOptionProvider_DropEquipment), "GetSingleOptionFor")]
    internal static class MechWeaponDropMenuPatch
    {
        private static void Postfix(Pawn clickedPawn, FloatMenuContext context, ref FloatMenuOption __result)
        {
            if (__result == null || clickedPawn != context.FirstSelectedPawn
                || clickedPawn.equipment?.Primary is not ThingWithComps primary
                || !MechWeaponUtility.IsBuiltIn(primary.def)
                || !MechWeaponUtility.IsManaged(clickedPawn)) return;
            __result = new FloatMenuOption("MAP_MechWeapon.BuiltInLocked".Translate(), null);
        }
    }

    [HarmonyPatch(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.TryTransferEquipmentToContainer))]
    internal static class MechWeaponTransferPatch
    {
        private static bool Prefix(Pawn_EquipmentTracker __instance, ThingWithComps eq, ref bool __result)
        {
            if (!MechWeaponUtility.IsBuiltIn(eq.def)) return true;
            MechWeaponRecord? record = GameComponent_MechWeaponRegistry.OwnerOf(eq)
                ?? GameComponent_MechWeaponRegistry.Get(__instance.pawn);
            if (record == null || record.ControlledChanges > 0) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(JobDriver_DropEquipment), nameof(JobDriver_DropEquipment.TryMakePreToilReservations))]
    internal static class MechWeaponDropJobPatch
    {
        private static bool Prefix(JobDriver_DropEquipment __instance, ref bool __result)
        {
            if (!MechWeaponUtility.IsManaged(__instance.pawn)
                || __instance.job.targetA.Thing is not Thing target || !MechWeaponUtility.IsBuiltIn(target.def)) return true;
            __result = false;
            return false;
        }
    }
}
