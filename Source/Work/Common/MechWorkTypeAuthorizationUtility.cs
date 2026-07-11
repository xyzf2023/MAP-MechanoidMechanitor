using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class MechWorkTypeAuthorizationUtility
    {
        public static bool IsDisabledOutsideMechRaceProfile(
            Pawn pawn,
            WorkTypeDef workType,
            bool permanentOnly)
        {
            if (pawn.IsMutant && pawn.mutant.HasTurned)
            {
                List<WorkTypeDef>? disabledWorkTypes =
                    pawn.mutant.Def.DisabledWorkTypes;

                return disabledWorkTypes != null
                    && disabledWorkTypes.Contains(workType);
            }

            if (pawn.story != null && !pawn.IsSlave)
            {
                List<BackstoryDef>? backstories = pawn.story.AllBackstories;
                if (backstories != null)
                {
                    for (int i = 0; i < backstories.Count; i++)
                    {
                        BackstoryDef backstory = backstories[i];
                        if (backstory?.DisabledWorkTypes != null
                            && backstory.DisabledWorkTypes.Contains(workType))
                        {
                            return true;
                        }
                    }
                }

                if (pawn.story.traits != null)
                {
                    List<Trait> traits = pawn.story.traits.allTraits;
                    for (int i = 0; i < traits.Count; i++)
                    {
                        Trait trait = traits[i];
                        if (trait == null || trait.Suppressed)
                        {
                            continue;
                        }

                        IEnumerable<WorkTypeDef>? disabledWorkTypes =
                            trait.GetDisabledWorkTypes();
                        if (disabledWorkTypes != null
                            && disabledWorkTypes.Contains(workType))
                        {
                            return true;
                        }
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

            List<LifeStageWorkSettings>? lifeStageWorkSettings =
                pawn.RaceProps?.lifeStageWorkSettings;
            if (lifeStageWorkSettings != null)
            {
                for (int i = 0; i < lifeStageWorkSettings.Count; i++)
                {
                    LifeStageWorkSettings settings = lifeStageWorkSettings[i];
                    if (settings.workType == workType && settings.IsDisabled(pawn))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
