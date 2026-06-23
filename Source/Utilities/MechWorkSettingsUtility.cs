using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class MechWorkSettingsUtility
    {
        public static void RestrictToMechEnabledWorkTypes(Pawn pawn)
        {
            if (pawn?.workSettings == null || !pawn.workSettings.EverWork)
            {
                return;
            }

            if (!ModsConfig.BiotechActive || !pawn.RaceProps.IsMechanoid)
            {
                return;
            }

            List<WorkTypeDef> allowed =
                MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn)
                    ? MechanoidMechanitorRoleUtility.GetRoleWorkTypes()
                    : pawn.RaceProps.mechEnabledWorkTypes;

            if (allowed.NullOrEmpty())
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
    }
}
