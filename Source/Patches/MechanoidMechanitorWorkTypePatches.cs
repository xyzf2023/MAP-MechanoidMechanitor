using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetDisabledWorkTypes))]
    public static class Patch_Pawn_GetDisabledWorkTypes_AcquiredMechanitor
    {
        [HarmonyPostfix]
        public static void Postfix(
            Pawn __instance,
            bool permanentOnly,
            ref List<WorkTypeDef> __result)
        {
            if (__result == null
                || !MechanoidMechanitorRoleUtility
                    .IsAcquiredMechanoidMechanitor(__instance))
            {
                return;
            }

            for (int i = __result.Count - 1; i >= 0; i--)
            {
                WorkTypeDef workType = __result[i];
                if (!MechanoidMechanitorRoleUtility.IsRoleWorkType(workType))
                {
                    continue;
                }

                if (!IsDisabledOutsideMechRaceProfile(
                        __instance,
                        workType,
                        permanentOnly))
                {
                    __result.RemoveAt(i);
                }
            }
        }

        private static bool IsDisabledOutsideMechRaceProfile(
            Pawn pawn,
            WorkTypeDef workType,
            bool permanentOnly)
        {
            if (pawn.IsMutant && pawn.mutant.HasTurned)
            {
                return pawn.mutant.Def.DisabledWorkTypes.Contains(workType);
            }

            if (pawn.story != null && !pawn.IsSlave)
            {
                foreach (BackstoryDef backstory in pawn.story.AllBackstories)
                {
                    if (backstory.DisabledWorkTypes.Contains(workType))
                    {
                        return true;
                    }
                }

                List<Trait> traits = pawn.story.traits.allTraits;
                for (int i = 0; i < traits.Count; i++)
                {
                    Trait trait = traits[i];
                    if (!trait.Suppressed
                        && trait.GetDisabledWorkTypes().Contains(workType))
                    {
                        return true;
                    }
                }
            }

            if (permanentOnly)
            {
                return false;
            }

            if (pawn.health != null
                && pawn.health.DisabledWorkTypes.Contains(workType))
            {
                return true;
            }

            if (pawn.royalty != null && !pawn.IsSlave)
            {
                foreach (RoyalTitle title in pawn.royalty.AllTitlesForReading)
                {
                    if (title.conceited
                        && title.def.DisabledWorkTypes.Contains(workType))
                    {
                        return true;
                    }
                }
            }

            if (ModsConfig.IdeologyActive && pawn.Ideo != null)
            {
                Precept_Role role = pawn.Ideo.GetRole(pawn);
                if (role != null && role.DisabledWorkTypes.Contains(workType))
                {
                    return true;
                }
            }

            if (ModsConfig.BiotechActive && pawn.genes != null)
            {
                foreach (Gene gene in pawn.genes.GenesListForReading)
                {
                    if (gene.DisabledWorkTypes.Contains(workType))
                    {
                        return true;
                    }
                }
            }

            foreach (QuestPart_WorkDisabled questPart in
                     QuestUtility.GetWorkDisabledQuestPart(pawn))
            {
                if (questPart.DisabledWorkTypes.Contains(workType))
                {
                    return true;
                }
            }

            if (pawn.guest != null
                && pawn.guest.GetDisabledWorkTypes().Contains(workType))
            {
                return true;
            }

            for (int i = 0; i < pawn.RaceProps.lifeStageWorkSettings.Count; i++)
            {
                LifeStageWorkSettings settings =
                    pawn.RaceProps.lifeStageWorkSettings[i];
                if (settings.workType == workType && settings.IsDisabled(pawn))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
