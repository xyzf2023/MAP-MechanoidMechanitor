using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class MechanicalChildcareUtility
    {
        private static WorkTypeDef? cachedChildcareWorkType;
        private static HashSet<WorkGiverDef>? allowedWorkGivers;
        private static bool allowedWorkGiversInitialized;

        private static readonly string[] AllowedWorkGiverDefNames =
        {
            "BringBabyToSafety",
            "BottleFeedBaby",
            "PlayWithBaby"
        };

        public static WorkTypeDef ChildcareWorkType =>
            cachedChildcareWorkType ??=
                DefDatabase<WorkTypeDef>.GetNamedSilentFail("Childcare");

        public static bool IsAuthorized(Pawn? pawn)
        {
            return GameComponent_MechanicalChildcareRegistry.IsAuthorized(pawn);
        }

        public static bool AllowsWorkGiver(Pawn? pawn, WorkGiverDef? workGiver)
        {
            if (pawn == null || workGiver == null || !IsAuthorized(pawn))
            {
                return false;
            }

            return IsAllowedWorkGiver(workGiver);
        }

        /// <summary>
        /// 纯白名单检查。调用方须已确认保育授权，本方法不再查询注册表。
        /// </summary>
        internal static bool IsAllowedWorkGiver(WorkGiverDef? workGiver)
        {
            if (workGiver == null)
            {
                return false;
            }

            EnsureAllowedWorkGiversInitialized();
            return allowedWorkGivers!.Contains(workGiver);
        }

        public static void GrantAndEnsureInfrastructure(Pawn? pawn, int defaultPriority)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return;
            }

            if (pawn.GetComp<CompMechanicalChildcareUser>() == null)
            {
                GameComponent_MechanicalChildcareRegistry.Grant(pawn, defaultPriority);
            }

            EnsureInfrastructure(pawn);
        }

        public static void EnsureInfrastructure(Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed || !IsAuthorized(pawn))
            {
                return;
            }

            EnsureTrackerContainers(pawn);

            MechanoidBackstoryUtility.EnsureGenericBackstories(pawn);

            if (!MechWorkSettingsUtility.TryEnsureWorkSettingsInitialized(pawn))
            {
                return;
            }

            pawn.Notify_DisabledWorkTypesChanged();
            TryInitializeDefaultChildcarePriority(pawn);
            MechWorkSettingsUtility.RestrictToMechEnabledWorkTypes(pawn);
            pawn.Notify_DisabledWorkTypesChanged();
        }

        internal static int ClampDefaultPriority(int priority)
        {
            if (priority < 1)
            {
                return 1;
            }

            if (priority > 4)
            {
                return 4;
            }

            return priority;
        }

        internal static void TryGiveFedBabyThought(Pawn? caregiver, Pawn? baby)
        {
            if (caregiver?.needs?.mood?.thoughts?.memories == null || baby == null)
            {
                return;
            }

            caregiver.needs.mood.thoughts.memories.TryGainMemory(ThoughtDefOf.FedBaby, baby);
        }

        private static void EnsureAllowedWorkGiversInitialized()
        {
            if (allowedWorkGiversInitialized)
            {
                return;
            }

            allowedWorkGiversInitialized = true;
            allowedWorkGivers = new HashSet<WorkGiverDef>();

            for (int i = 0; i < AllowedWorkGiverDefNames.Length; i++)
            {
                WorkGiverDef? def = DefDatabase<WorkGiverDef>.GetNamedSilentFail(
                    AllowedWorkGiverDefNames[i]);
                if (def != null)
                {
                    allowedWorkGivers.Add(def);
                }
            }
        }

        private static void EnsureTrackerContainers(Pawn pawn)
        {
            pawn.story ??= new Pawn_StoryTracker(pawn);
            pawn.story.traits ??= new TraitSet(pawn);
            pawn.skills ??= new Pawn_SkillTracker(pawn);
            pawn.interactions ??= new Pawn_InteractionsTracker(pawn);
            pawn.relations ??= new Pawn_RelationsTracker(pawn);
            pawn.workSettings ??= new Pawn_WorkSettings(pawn);
        }

        private static void TryInitializeDefaultChildcarePriority(Pawn pawn)
        {
            WorkTypeDef? childcare = ChildcareWorkType;
            if (childcare == null || pawn.workSettings == null)
            {
                return;
            }

            if (GameComponent_MechanicalChildcareRegistry.IsDefaultPriorityInitialized(pawn))
            {
                return;
            }

            if (pawn.workSettings.GetPriority(childcare) > 0)
            {
                GameComponent_MechanicalChildcareRegistry.MarkDefaultPriorityInitialized(pawn);
                return;
            }

            if (pawn.WorkTypeIsDisabled(childcare))
            {
                return;
            }

            pawn.workSettings.SetPriority(
                childcare,
                GameComponent_MechanicalChildcareRegistry.GetDefaultPriority(pawn));

            if (pawn.workSettings.GetPriority(childcare) > 0)
            {
                GameComponent_MechanicalChildcareRegistry.MarkDefaultPriorityInitialized(pawn);
            }
        }
    }
}
