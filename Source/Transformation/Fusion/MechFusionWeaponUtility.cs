using System;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体武器所有权转移的统一入口。始终移动真实 Thing 实例；若物品已经被
    /// 玩家转移到其他持有者，则不强夺、不复制。
    /// </summary>
    internal static class MechFusionWeaponUtility
    {
        internal static void TransferForFusion(
            MechFusionSession session,
            Pawn source,
            Pawn wearer)
        {
            using IDisposable weaponChange = MechWeaponUtility.ControlledChange(source);
            ThingWithComps? sourceWeapon = source.equipment?.Primary;
            if (sourceWeapon == null)
            {
                session.CaptureWeapons(null, null);
                return;
            }

            if (source.equipment == null
                || wearer.equipment == null
                || wearer.inventory == null)
            {
                throw new InvalidOperationException(
                    "合体双方缺少武器或货物追踪器。");
            }

            ThingWithComps? wearerWeapon = wearer.equipment.Primary;
            session.CaptureWeapons(wearerWeapon, sourceWeapon);

            if (wearerWeapon != null)
            {
                wearer.equipment.Remove(wearerWeapon);
                if (!wearer.inventory.innerContainer.TryAdd(wearerWeapon))
                {
                    PlaceNearPawn(wearerWeapon, wearer);
                }
            }

            source.equipment.Remove(sourceWeapon);
            wearer.equipment.AddEquipment(sourceWeapon);
            if (!ReferenceEquals(wearer.equipment.Primary, sourceWeapon))
            {
                throw new InvalidOperationException(
                    "机械体武器未能装备给合体目标。");
            }
        }

        internal static void RestoreAfterTeardown(
            MechFusionSession session,
            Pawn source,
            Pawn? wearer)
        {
            using IDisposable weaponChange = MechWeaponUtility.ControlledChange(source);
            ThingWithComps? sourceWeapon = session.SourceWeapon;
            if (sourceWeapon != null
                && !sourceWeapon.Destroyed
                && !sourceWeapon.Discarded
                && wearer != null
                && TryRemoveFromPawn(sourceWeapon, wearer))
            {
                EquipOrStow(sourceWeapon, source);
            }

            ThingWithComps? wearerWeapon = session.OriginalWearerWeapon;
            if (wearerWeapon == null
                || wearerWeapon.Destroyed
                || wearerWeapon.Discarded
                || wearer?.inventory == null
                || wearer.equipment == null
                || !wearer.inventory.innerContainer.Contains(wearerWeapon))
            {
                return;
            }

            wearer.inventory.innerContainer.Remove(wearerWeapon);
            StowOrDropPrimary(wearer);
            wearer.equipment.AddEquipment(wearerWeapon);
        }

        internal static void RollbackStart(
            MechFusionSession session,
            Pawn source,
            Pawn wearer)
        {
            using IDisposable weaponChange = MechWeaponUtility.ControlledChange(source);
            ThingWithComps? sourceWeapon = session.SourceWeapon;
            if (sourceWeapon != null
                && !sourceWeapon.Destroyed
                && !sourceWeapon.Discarded
                && TryRemoveFromPawn(sourceWeapon, wearer))
            {
                EquipOrStow(sourceWeapon, source);
            }

            ThingWithComps? wearerWeapon = session.OriginalWearerWeapon;
            if (wearerWeapon == null
                || wearerWeapon.Destroyed
                || wearerWeapon.Discarded
                || wearer.equipment == null)
            {
                return;
            }

            bool recovered = TryRemoveFromPawn(wearerWeapon, wearer);
            if (!recovered
                && wearerWeapon.Spawned
                && wearerWeapon.Map == wearer.Map)
            {
                wearerWeapon.DeSpawn(DestroyMode.Vanish);
                recovered = true;
            }

            if (!recovered)
            {
                return;
            }

            StowOrDropPrimary(wearer);
            wearer.equipment.AddEquipment(wearerWeapon);
        }

        private static bool TryRemoveFromPawn(
            ThingWithComps weapon,
            Pawn pawn)
        {
            if (pawn.equipment != null
                && ReferenceEquals(pawn.equipment.Primary, weapon))
            {
                pawn.equipment.Remove(weapon);
                return true;
            }

            if (pawn.inventory?.innerContainer.Contains(weapon) == true)
            {
                pawn.inventory.innerContainer.Remove(weapon);
                return true;
            }

            return false;
        }

        private static void EquipOrStow(ThingWithComps weapon, Pawn pawn)
        {
            if (pawn.equipment != null && pawn.equipment.Primary == null)
            {
                pawn.equipment.AddEquipment(weapon);
                return;
            }

            if (pawn.inventory?.innerContainer.TryAdd(weapon) == true)
            {
                return;
            }

            PlaceNearPawn(weapon, pawn);
        }

        private static void StowOrDropPrimary(Pawn pawn)
        {
            ThingWithComps? current = pawn.equipment?.Primary;
            if (current == null)
            {
                return;
            }

            pawn.equipment!.Remove(current);
            if (pawn.inventory?.innerContainer.TryAdd(current) != true)
            {
                PlaceNearPawn(current, pawn);
            }
        }

        private static void PlaceNearPawn(Thing thing, Pawn pawn)
        {
            // GenPlace 不执行 destroyOnDrop，专武不能经合体失败回退流入地图。
            if (MechWeaponUtility.IsBuiltIn(thing.def))
            {
                thing.Destroy();
                return;
            }
            try
            {
                if (pawn.Spawned
                    && pawn.Map != null
                    && GenPlace.TryPlaceThing(
                        thing,
                        pawn.Position,
                        pawn.Map,
                        ThingPlaceMode.Near))
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 放置合体交换武器时发生异常，" +
                    "不阻断合体事务：" +
                    $"thing={thing.ThingID}，pawn={pawn.ThingID}：{ex}");
            }

            Log.Error(
                "[MAP-机械族机械师] 无法将合体交换武器放入货物或放置在地图上；" +
                "物品实例仍被保留，但合体流程继续：" +
                $"thing={thing.ThingID}，pawn={pawn.ThingID}。");
        }
    }
}
