using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class MechWorkSettingsUtility
    {
        public static bool TryEnsureWorkSettingsInitialized(Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return false;
            }

            pawn.workSettings ??= new Pawn_WorkSettings(pawn);

            if (pawn.kindDef == null || pawn.def?.race == null)
            {
                return false;
            }

            pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
            return pawn.workSettings.Initialized;
        }

        public static void RestrictToMechEnabledWorkTypes(Pawn pawn)
        {
            if (pawn?.workSettings == null || !pawn.workSettings.EverWork)
            {
                return;
            }

            if (pawn.kindDef == null)
            {
                return;
            }

            if (!ModsConfig.BiotechActive || !pawn.RaceProps.IsMechanoid)
            {
                return;
            }

            HashSet<WorkTypeDef> allowed = BuildAllowedWorkTypes(pawn);
            if (allowed.Count == 0)
            {
                return;
            }

            List<WorkTypeDef> allWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;
            for (int i = 0; i < allWorkTypes.Count; i++)
            {
                WorkTypeDef workType = allWorkTypes[i];
                if (!allowed.Contains(workType))
                {
                    pawn.workSettings.Disable(workType);
                }
            }

            pawn.Notify_DisabledWorkTypesChanged();
        }

        private static HashSet<WorkTypeDef> BuildAllowedWorkTypes(Pawn pawn)
        {
            HashSet<WorkTypeDef> allowed = new HashSet<WorkTypeDef>();

            if (MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                List<WorkTypeDef> roleWorkTypes =
                    MechanoidMechanitorRoleUtility.GetRoleWorkTypes();
                for (int i = 0; i < roleWorkTypes.Count; i++)
                {
                    allowed.Add(roleWorkTypes[i]);
                }
            }
            else if (pawn.RaceProps.mechEnabledWorkTypes != null)
            {
                List<WorkTypeDef> mechEnabledWorkTypes = pawn.RaceProps.mechEnabledWorkTypes;
                for (int i = 0; i < mechEnabledWorkTypes.Count; i++)
                {
                    allowed.Add(mechEnabledWorkTypes[i]);
                }
            }

            if (WardenWorkUtility.IsAuthorized(pawn))
            {
                WorkTypeDef? warden = WardenWorkUtility.WardenWorkType;
                if (warden != null)
                {
                    allowed.Add(warden);
                }
            }

            if (AnimalHandlingWorkUtility.IsAuthorized(pawn))
            {
                WorkTypeDef? handling = AnimalHandlingWorkUtility.HandlingWorkType;
                if (handling != null)
                {
                    allowed.Add(handling);
                }
            }

            return allowed;
        }
    }
}
