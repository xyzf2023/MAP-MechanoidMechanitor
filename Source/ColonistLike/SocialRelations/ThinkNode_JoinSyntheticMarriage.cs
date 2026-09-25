using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor
{
    /// <summary>仅加入自己的婚礼；后续工作仍由原版 LordDuty 节点执行。</summary>
    public sealed class ThinkNode_JoinSyntheticMarriage : ThinkNode
    {
        public static bool CanParticipate(Pawn pawn) =>
            SyntheticCompanionRelationshipUtility.HasModule(pawn)
            && SyntheticCompanionRelationshipUtility.CanAct(pawn)
            && pawn.Faction == Faction.OfPlayer && pawn.CurJob?.playerForced != true
            && pawn.needs?.energy?.IsLowEnergySelfShutdown != true;

        public override ThinkResult TryIssueJobPackage(Pawn pawn, JobIssueParams jobParams)
        {
            Lord? current = pawn.GetLord();
            if (current?.LordJob is LordJob_Joinable_MarriageCeremony currentWedding
                && (currentWedding.firstPawn == pawn || currentWedding.secondPawn == pawn))
            {
                if (!CanParticipate(pawn) || currentWedding.VoluntaryJoinPriorityFor(pawn) <= 0f)
                    current.Notify_PawnLost(pawn, PawnLostCondition.LeftVoluntarily);
                return ThinkResult.NoJob;
            }
            if (current != null || !CanParticipate(pawn)) return ThinkResult.NoJob;
            foreach (Lord lord in pawn.Map.lordManager.lords)
            {
                if (!(lord.LordJob is LordJob_Joinable_MarriageCeremony wedding)
                    || (wedding.firstPawn != pawn && wedding.secondPawn != pawn)) continue;
                Pawn other = wedding.firstPawn == pawn ? wedding.secondPawn : wedding.firstPawn;
                if (SyntheticCompanionRelationshipUtility.IsProtectedPair(pawn, other)
                    && wedding.VoluntaryJoinPriorityFor(pawn) > 0f)
                {
                    lord.AddPawn(pawn);
                    break;
                }
            }
            return ThinkResult.NoJob;
        }
    }

    public static class SyntheticMarriagePatches
    {
        [HarmonyPatch]
        public static class WeddingSpouseCounts
        {
            public static MethodBase TargetMethod() => AccessTools.Constructor(
                typeof(LordJob_Joinable_MarriageCeremony), new[] { typeof(Pawn), typeof(Pawn), typeof(IntVec3) });
            public static void Postfix(Pawn firstPawn, Pawn secondPawn, ref int ___secondPawnSpouseCount)
            {
                // 原版构造器对第二人误读了第一人的配偶数；只修正本类婚礼的宾客文化判定。
                if (SyntheticCompanionRelationshipUtility.IsProtectedPair(firstPawn, secondPawn)
                    || SyntheticRelationshipContext.Current?.Operation == SyntheticRelationshipOperation.CeremonyStart)
                    ___secondPawnSpouseCount = secondPawn.GetSpouseCount(includeDead: false);
            }
        }

        [HarmonyPatch(typeof(VoluntarilyJoinableLordsStarter), nameof(VoluntarilyJoinableLordsStarter.TryStartMarriageCeremony))]
        public static class StartCeremony
        {
            internal static void Prefix(Pawn firstFiance, Pawn secondFiance, out SyntheticRelationshipContext? __state)
            {
                // 人类可能同时有机械体与第三者未婚夫妻；其第三者婚礼也必须使用这次传入的对象。
                __state = SyntheticCompanionRelationshipUtility.HasProtectedPartner(firstFiance)
                    || SyntheticCompanionRelationshipUtility.HasProtectedPartner(secondFiance)
                    ? SyntheticRelationshipContext.Enter(SyntheticRelationshipOperation.CeremonyStart, firstFiance, secondFiance) : null;
            }
            internal static Exception? Finalizer(Exception? __exception, SyntheticRelationshipContext? __state)
            {
                __state?.Dispose();
                return __exception;
            }
        }

        [HarmonyPatch(typeof(GatheringWorker_MarriageCeremony), "FindFiancees")]
        public static class ExactFiancePair
        {
            public static bool Prefix(Pawn organizer, ref Pawn firstFiance, ref Pawn secondFiance)
            {
                var context = SyntheticRelationshipContext.Current;
                if (context?.Operation != SyntheticRelationshipOperation.CeremonyStart || context.First != organizer) return true;
                firstFiance = context.First;
                secondFiance = context.Second!;
                return false;
            }
        }

        [HarmonyPatch(typeof(MarriageCeremonyUtility), nameof(MarriageCeremonyUtility.FianceCanContinueCeremony))]
        public static class CeremonyEligibility
        {
            public static void Postfix(Pawn pawn, ref bool __result)
            {
                if (SyntheticCompanionRelationshipUtility.HasModule(pawn)
                    && !ThinkNode_JoinSyntheticMarriage.CanParticipate(pawn)) __result = false;
            }
        }

        [HarmonyPatch(typeof(JobGiver_MarryAdjacentPawn), "TryGiveJob")]
        public static class MechanicalMarriageJob
        {
            public static bool Prefix(Pawn pawn, ref Job __result)
            {
                if (!SyntheticCompanionRelationshipUtility.HasModule(pawn)) return true;
                __result = null!;
                if (!(pawn.GetLord()?.LordJob is LordJob_Joinable_MarriageCeremony wedding)
                    || !ThinkNode_JoinSyntheticMarriage.CanParticipate(pawn)) return false;
                Pawn? other = wedding.firstPawn == pawn ? wedding.secondPawn : wedding.secondPawn == pawn ? wedding.firstPawn : null;
                if (other != null && other.Spawned && other.Map == pawn.Map && !other.Drafted
                    && pawn.Position.AdjacentToCardinal(other.Position)
                    && pawn.relations.DirectRelationExists(PawnRelationDefOf.Fiance, other))
                    __result = JobMaker.MakeJob(JobDefOf.MarryAdjacentPawn, other);
                return false;
            }
        }
    }
}
