using System;
using System.Collections.Generic;
using GD3;
using RimWorld;
using Verse;
using Verse.Sound;

namespace MAP_MechanoidMechanitor.GD5
{
    /// <summary>
    /// 禁卫机械蜈蚣武器切换运行时兼容。
    /// 对普通受监管机械体不做任何处理；仅接管合法的 MAP 无监管者节点。
    /// </summary>
    internal static class CataphractCentipedeWeaponCompatibilityPatch
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] 闪耀世界毁灭者5禁卫机械蜈蚣武器切换兼容：";

        private static ThingDef? srWeaponDef;
        private static ThingDef? fyWeaponDef;
        private static SoundDef? switchSoundDef;

        internal static void Configure(
            ThingDef resolvedSrWeaponDef,
            ThingDef resolvedFyWeaponDef,
            SoundDef resolvedSwitchSoundDef)
        {
            srWeaponDef = resolvedSrWeaponDef;
            fyWeaponDef = resolvedFyWeaponDef;
            switchSoundDef = resolvedSwitchSoundDef;
            MechWeaponUtility.RegisterBuiltInWeapon(resolvedSrWeaponDef);
            MechWeaponUtility.RegisterBuiltInWeapon(resolvedFyWeaponDef);
        }

        internal static void Postfix(
            CompChangeWeaponB __instance,
            ref IEnumerable<Gizmo> __result)
        {
            if (__result == null
                || __instance?.parent is not Pawn pawn
                || !MAPOverseerlessNodeUtility.IsOverseerlessNodeSubject(pawn))
            {
                return;
            }

            __result = WrapGizmos(pawn, __result);
        }

        private static IEnumerable<Gizmo> WrapGizmos(
            Pawn pawn,
            IEnumerable<Gizmo> source)
        {
            foreach (Gizmo gizmo in source)
            {
                if (gizmo is Command_Action command)
                {
                    ConfigureCommand(pawn, command);
                }

                yield return gizmo;
            }
        }

        private static void ConfigureCommand(Pawn pawn, Command_Action command)
        {
            ThingWithComps? primary = pawn.equipment?.Primary;
            command.Disabled =
                !CanUseWeaponToggleNow(pawn)
                || IsForeignPrimaryWeapon(primary);
            if (MechWeaponUtility.IsManaged(pawn) && MechWeaponUtility.Missing(pawn))
                command.Disable("MAP_MechWeapon.Missing".Translate());

            // 原 GD5 action 会再次要求 GetOverseer()!=null，且会把任何非 SR 主武器
            // 直接 Remove。无监管者机械族机械师必须改走安全状态机。
            command.action = () => TrySwitchWeapon(pawn);
        }

        private static bool CanUseWeaponToggleNow(Pawn pawn)
        {
            return MAPOverseerlessNodeUtility.IsOverseerlessNodeSubject(pawn)
                && !pawn.Dead
                && !pawn.Downed
                && !pawn.mindState.mentalStateHandler.InMentalState
                && pawn.Faction == Faction.OfPlayer;
        }

        private static bool IsForeignPrimaryWeapon(ThingWithComps? primary)
        {
            if (primary == null)
            {
                return false;
            }

            if (srWeaponDef == null || fyWeaponDef == null)
            {
                return true;
            }

            return primary.def != srWeaponDef && primary.def != fyWeaponDef;
        }

        private static void TrySwitchWeapon(Pawn pawn)
        {
            if (!CanUseWeaponToggleNow(pawn)
                || srWeaponDef == null
                || fyWeaponDef == null)
            {
                return;
            }

            Pawn_EquipmentTracker equipment =
                pawn.equipment ??= new Pawn_EquipmentTracker(pawn);
            ThingWithComps? current = equipment.Primary;

            ThingDef targetDef;
            if (current == null)
            {
                // 自由装备系统允许主动丢弃主武器；无武器时以 SR 作为默认专武恢复入口。
                targetDef = srWeaponDef;
            }
            else if (current.def == srWeaponDef)
            {
                targetDef = fyWeaponDef;
            }
            else if (current.def == fyWeaponDef)
            {
                targetDef = srWeaponDef;
            }
            else
            {
                // 执行层再次保护普通武器，避免其他 MOD 或状态变化绕过 Gizmo Disabled。
                return;
            }

            if (MechWeaponUtility.IsManaged(pawn))
            {
                if (MechWeaponUtility.TrySwitchBuiltInVariant(pawn, targetDef)
                    && switchSoundDef != null && pawn.MapHeld != null)
                    switchSoundDef.PlayOneShot(new TargetInfo(pawn.PositionHeld, pawn.MapHeld, false));
                return;
            }

            Thing created = ThingMaker.MakeThing(targetDef);
            if (created is not ThingWithComps newEquipment)
            {
                if (!created.Destroyed)
                {
                    created.Destroy(DestroyMode.Vanish);
                }

                Log.Error(
                    LogPrefix
                    + $"武器Def {targetDef.defName} 未生成 ThingWithComps，已取消切换。");
                return;
            }

            if (current != null)
            {
                // GD5 原实现直接 Remove 会留下无持有者且未生成的 Thing。
                // 专武切换本质是替换模式，因此在兼容分支中显式销毁旧专武。
                equipment.DestroyEquipment(current);
            }

            equipment.AddEquipment(newEquipment);

            if (switchSoundDef != null && pawn.MapHeld != null)
            {
                switchSoundDef.PlayOneShot(
                    new TargetInfo(pawn.PositionHeld, pawn.MapHeld, false));
            }
        }
    }
}
