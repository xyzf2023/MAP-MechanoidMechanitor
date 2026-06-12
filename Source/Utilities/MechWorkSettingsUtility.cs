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

            if (!ModsConfig.BiotechActive)
            {
                return;
            }

            if (!pawn.RaceProps.IsMechanoid)
            {
                return;
            }

            if (pawn.RaceProps.mechEnabledWorkTypes.NullOrEmpty())
            {
                return;
            }

            List<WorkTypeDef> allowed = pawn.RaceProps.mechEnabledWorkTypes;
            List<WorkTypeDef> allWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;

            for (int i = 0; i < allWorkTypes.Count; i++)
            {
                WorkTypeDef w = allWorkTypes[i];
                if (!allowed.Contains(w))
                {
                    pawn.workSettings.Disable(w);
                }
            }

            pawn.Notify_DisabledWorkTypesChanged();
        }
    }
}
