using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>装备页的只读物品权限；服装、普通武器与内建专武分别检查。</summary>
    internal static class GearInteractionUtility
    {
        internal static bool HasGearCapability(Pawn? pawn) => pawn != null
            && (MechanoidMechanitorRoleUtility.AllowsHumanWeapons(pawn)
                || HumanApparelUtility.TryGetApparelComp(pawn, out _));

        internal static bool IsProtectedBuiltIn(Pawn pawn, Thing thing) =>
            MechWeaponUtility.IsBuiltIn(thing.def)
            && (MechWeaponUtility.IsManaged(pawn)
                || GameComponent_MechWeaponRegistry.OwnerOf(thing) != null);

        internal static bool IsGearLocked(Pawn pawn, Thing thing) =>
            IsProtectedBuiltIn(pawn, thing)
            || (!MechWeaponUtility.IsManaged(pawn) && pawn.kindDef.destroyGearOnDrop);

        internal static bool CanOperate(Pawn? pawn) => pawn != null
            && Scribe.mode == LoadSaveMode.Inactive
            && !MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferPositiveRestore
            && pawn.Spawned && !pawn.Destroyed && !pawn.Discarded && !pawn.Dead
            && !pawn.Downed && !pawn.InMentalState && pawn.CarriedBy == null
            && !pawn.Deathresting && !pawn.IsSelfShutdown()
            && pawn.Faction == Faction.OfPlayer && pawn.HostFaction == null
            && !pawn.IsPrisoner && !pawn.IsSlave && pawn.CanTakeOrder && pawn.jobs != null;

        internal static bool Holds(Pawn pawn, Thing thing) =>
            (thing is Apparel apparel && pawn.apparel?.WornApparel.Contains(apparel) == true)
            || (thing is ThingWithComps equipment
                && pawn.equipment?.AllEquipmentListForReading.Contains(equipment) == true)
            || pawn.inventory?.innerContainer.Contains(thing) == true;

        internal static bool CanDrop(Pawn pawn, Thing thing, bool respectKindLock = true)
        {
            if (thing == null || thing.Destroyed || !Holds(pawn, thing)
                || IsProtectedBuiltIn(pawn, thing))
                return false;

            bool inventory = pawn.inventory?.innerContainer.Contains(thing) == true;
            if (pawn.IsQuestLodger()
                && (inventory || !EquipmentUtility.QuestLodgerCanUnequip(thing, pawn)))
                return false;
            if (inventory)
                return !thing.def.destroyOnDrop;
            if (respectKindLock && IsGearLocked(pawn, thing))
                return false;

            if (thing is Apparel apparel && pawn.apparel?.WornApparel.Contains(apparel) == true)
                return !pawn.apparel.IsLocked(apparel)
                    && (!HasGearCapability(pawn) || HumanApparelUtility.CanRemoveApparel(pawn));
            return !HasGearCapability(pawn)
                || (CompHumanWeaponUser.PawnCanUseHumanWeapons(pawn)
                    && CompHumanWeaponUser.PawnAllowsDropEquipmentFloatMenu(pawn));
        }

        internal static string DropBlockedReason(Pawn pawn, Thing thing) =>
            (IsProtectedBuiltIn(pawn, thing) ? "MAP_MechWeapon.BuiltInLocked" : "DropThingLocked").Translate();
    }
}
