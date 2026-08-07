using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public class JobGiver_MechanoidMechanitorMeditate : ThinkNode_JobGiver
    {
        public override float GetPriority(Pawn pawn)
        {
            return MechanoidMechanitorPsycastUtility.ShouldAutoMeditate(
                pawn,
                out _)
                ? 7.1f
                : 0f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return MechanoidMechanitorPsycastUtility.TryMakeAssignedMeditationJob(
                pawn)!;
        }
    }

    [HarmonyPatch(
        typeof(CompAssignableToPawn_MeditationSpot),
        nameof(CompAssignableToPawn_MeditationSpot.AssigningCandidates),
        MethodType.Getter)]
    public static class Patch_CompAssignableToPawn_MeditationSpot_AssigningCandidates
    {
        [HarmonyPostfix]
        public static void Postfix(
            CompAssignableToPawn_MeditationSpot __instance,
            ref IEnumerable<Pawn> __result)
        {
            Map? map = __instance.parent?.Map;
            if (!ModsConfig.RoyaltyActive || map == null)
            {
                return;
            }

            List<Pawn> candidates = __result?.ToList() ?? new List<Pawn>();
            IReadOnlyList<Pawn> registered =
                GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors;
            for (int i = 0; i < registered.Count; i++)
            {
                Pawn pawn = registered[i];
                if (MechanoidMechanitorPsycastUtility.CanBeMeditationSpotCandidate(
                        pawn,
                        map)
                    && !candidates.Contains(pawn))
                {
                    candidates.Add(pawn);
                }
            }

            __result = candidates.OrderByDescending(
                pawn => __instance.CanAssignTo(pawn).Accepted);
        }
    }

    [HarmonyPatch(typeof(JobDriver_Meditate), "MakeNewToils")]
    public static class Patch_JobDriver_Meditate_MakeNewToils_MechanoidMechanitor
    {
        [HarmonyPostfix]
        public static IEnumerable<Toil> Postfix(
            IEnumerable<Toil> __result,
            JobDriver_Meditate __instance)
        {
            Pawn pawn = __instance.pawn;
            Job job = __instance.job;
            Building? jobSpot = job?.GetTarget(TargetIndex.A).Thing as Building;
            if (pawn != null
                && job?.def == JobDefOf.Meditate
                && job.ignoreJoyTimeAssignment
                && jobSpot?.def == ThingDefOf.MeditationSpot
                && MechanoidMechanitorPsycastUtility.HasPsycastingCapability(pawn))
            {
                __instance.AddEndCondition(
                    () => MechanoidMechanitorPsycastUtility
                        .GetAssignedMeditationEndCondition(__instance));
            }

            foreach (Toil toil in __result)
            {
                yield return toil;
            }
        }
    }
}
