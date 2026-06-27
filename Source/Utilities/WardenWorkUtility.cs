using System.Collections.Generic;
using System.Linq;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class WardenWorkUtility
    {
        private static WorkTypeDef? cachedWardenWorkType;

        public static WorkTypeDef WardenWorkType =>
            cachedWardenWorkType ??=
                DefDatabase<WorkTypeDef>.GetNamedSilentFail("Warden");

        public static bool IsAuthorized(Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return false;
            }

            if (pawn.GetComp<CompWardenWorkUser>() != null)
            {
                return true;
            }

            return GameComponent_WardenWorkRegistry.IsAuthorized(pawn);
        }

        public static void GrantAndEnsureInfrastructure(Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return;
            }

            GameComponent_WardenWorkRegistry.Grant(pawn);
            EnsureInfrastructure(pawn);
        }

        public static void EnsureInfrastructure(Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed || !IsAuthorized(pawn))
            {
                return;
            }

            pawn.story ??= new Pawn_StoryTracker(pawn);
            pawn.story.traits ??= new TraitSet(pawn);
            pawn.skills ??= new Pawn_SkillTracker(pawn);
            pawn.interactions ??= new Pawn_InteractionsTracker(pawn);

            pawn.workSettings ??= new Pawn_WorkSettings(pawn);
            if (!pawn.workSettings.Initialized)
            {
                pawn.workSettings.EnableAndInitialize();
            }

            TryInitializeDefaultWardenPriority(pawn);
            pawn.Notify_DisabledWorkTypesChanged();
        }

        public static bool IsDisabledOutsideMechRaceProfile(
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

        private static void TryInitializeDefaultWardenPriority(Pawn pawn)
        {
            WorkTypeDef? warden = WardenWorkType;
            if (warden == null || pawn.workSettings == null)
            {
                return;
            }

            if (IsDefaultPriorityInitialized(pawn))
            {
                return;
            }

            if (pawn.workSettings.GetPriority(warden) <= 0)
            {
                pawn.workSettings.SetPriority(warden, GetDefaultPriority(pawn));
            }

            MarkDefaultPriorityInitialized(pawn);
        }

        private static int GetDefaultPriority(Pawn pawn)
        {
            CompWardenWorkUser? comp = pawn.GetComp<CompWardenWorkUser>();
            return comp?.DefaultPriority ?? 3;
        }

        private static bool IsDefaultPriorityInitialized(Pawn pawn)
        {
            CompWardenWorkUser? comp = pawn.GetComp<CompWardenWorkUser>();
            if (comp != null)
            {
                return comp.DefaultPriorityInitialized;
            }

            return GameComponent_WardenWorkRegistry.IsDefaultPriorityInitialized(pawn);
        }

        private static void MarkDefaultPriorityInitialized(Pawn pawn)
        {
            CompWardenWorkUser? comp = pawn.GetComp<CompWardenWorkUser>();
            if (comp != null)
            {
                comp.MarkDefaultPriorityInitialized();
                return;
            }

            GameComponent_WardenWorkRegistry.MarkDefaultPriorityInitialized(pawn);
        }
    }
}
