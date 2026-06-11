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

            if (pawn.equipment != null)
            {
                return;
            }

            pawn.equipment = new Pawn_EquipmentTracker(pawn);

            if (Prefs.DevMode)
            {
                Log.Message($"[MAP] Created equipment tracker for human weapon user: pawn={SafePawnDebugName(pawn)}");
            }
        }

        private static string SafePawnDebugName(Pawn? pawn)
        {
            if (pawn == null)
            {
                return "null";
            }

            string defName = pawn.def?.defName ?? "nullDef";
            string kindDefName = pawn.kindDef?.defName ?? "nullKind";
            return $"{defName}/{kindDefName}";
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

            CompHumanWeaponUser? comp = pawn.GetComp<CompHumanWeaponUser>();
            if (comp == null)
            {
                return false;
            }

            if (pawn.equipment != null)
            {
                return true;
            }

            return comp.Props.ensureEquipmentTracker;
        }

        public static bool PawnAllowsEquipFloatMenu(Pawn? pawn)
        {
            CompHumanWeaponUser? comp = pawn?.GetComp<CompHumanWeaponUser>();
            return comp != null && comp.Props.allowEquipFloatMenu;
        }

        public static bool PawnAllowsDropEquipmentFloatMenu(Pawn? pawn)
        {
            CompHumanWeaponUser? comp = pawn?.GetComp<CompHumanWeaponUser>();
            return comp != null && comp.Props.allowDropEquipmentFloatMenu;
        }
    }
}
