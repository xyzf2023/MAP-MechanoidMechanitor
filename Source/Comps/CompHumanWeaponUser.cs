using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompProperties_HumanWeaponUser : CompProperties
    {
        public bool allowEquipFloatMenu = true;
        public bool allowDropEquipmentFloatMenu = true;
        public bool ensureEquipmentTracker = true;

        public CompProperties_HumanWeaponUser()
        {
            compClass = typeof(CompHumanWeaponUser);
        }
    }

    public class CompHumanWeaponUser : ThingComp
    {
        public CompProperties_HumanWeaponUser Props => (CompProperties_HumanWeaponUser)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            EnsureTrackersIfNeeded();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureTrackersIfNeeded();
            }
        }

        private void EnsureTrackersIfNeeded()
        {
            if (parent is not Pawn pawn || !Props.ensureEquipmentTracker)
            {
                return;
            }

            if (pawn.equipment == null)
            {
                pawn.equipment = new Pawn_EquipmentTracker(pawn);
            }
        }

        public static bool PawnCanUseHumanWeapons(Pawn? pawn)
        {
            if (pawn == null || pawn.Dead)
            {
                return false;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                return false;
            }

            if (!MechanoidMechanitorCapabilityUtility.HasCapability(
                    pawn,
                    MechanoidMechanitorCapability.HumanWeapons))
            {
                return false;
            }

            if (GameComponent_MechanoidMechanitorRegistry.TryGetAcquiredMechanitorRecord(
                    pawn,
                    out _))
            {
                pawn.equipment ??= new Pawn_EquipmentTracker(pawn);
                return true;
            }

            CompHumanWeaponUser? comp = pawn.GetComp<CompHumanWeaponUser>();
            return comp != null
                && (pawn.equipment != null || comp.Props.ensureEquipmentTracker);
        }

        public static bool PawnAllowsEquipFloatMenu(Pawn? pawn)
        {
            if (!MechanoidMechanitorCapabilityUtility.HasCapability(
                    pawn,
                    MechanoidMechanitorCapability.HumanWeapons))
            {
                return false;
            }

            if (GameComponent_MechanoidMechanitorRegistry.TryGetAcquiredMechanitorRecord(
                    pawn,
                    out _))
            {
                return true;
            }

            CompHumanWeaponUser? comp = pawn?.GetComp<CompHumanWeaponUser>();
            return comp != null && comp.Props.allowEquipFloatMenu;
        }

        public static bool PawnAllowsDropEquipmentFloatMenu(Pawn? pawn)
        {
            if (!MechanoidMechanitorCapabilityUtility.HasCapability(
                    pawn,
                    MechanoidMechanitorCapability.HumanWeapons))
            {
                return false;
            }

            if (GameComponent_MechanoidMechanitorRegistry.TryGetAcquiredMechanitorRecord(
                    pawn,
                    out _))
            {
                return true;
            }

            CompHumanWeaponUser? comp = pawn?.GetComp<CompHumanWeaponUser>();
            return comp != null && comp.Props.allowDropEquipmentFloatMenu;
        }
    }
}
