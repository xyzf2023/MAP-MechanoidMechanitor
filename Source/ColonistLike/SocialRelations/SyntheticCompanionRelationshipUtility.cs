using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>关系阶段只读取原版 DirectRelations；持久授权与当前能否行动分别判断。</summary>
    public static class SyntheticCompanionRelationshipUtility
    {
        public const string Key = "MAP_MechanoidMechanitor.Companion.";

        public static bool IsLoveRelation(PawnRelationDef def) => def == PawnRelationDefOf.Lover
            || def == PawnRelationDefOf.Fiance || def == PawnRelationDefOf.Spouse;

        public static bool HasModule(Pawn? pawn) =>
            GameComponent_SyntheticCompanionRegistry.HasAuthorizationRecord(pawn);

        public static bool HasLoveRelation(Pawn? first, Pawn? second)
        {
            if (first?.relations == null || second?.relations == null || first == second) return false;
            foreach (DirectPawnRelation relation in first.relations.DirectRelations)
                if (relation.otherPawn == second && IsLoveRelation(relation.def)
                    && second.relations.DirectRelationExists(relation.def, first)) return true;
            return false;
        }

        public static bool IsProtectedPair(Pawn? first, Pawn? second) =>
            (HasModule(first) || HasModule(second)) && HasLoveRelation(first, second);

        internal static bool IsAutomaticEndBlocked(Pawn first, Pawn second)
        {
            if (!IsProtectedPair(first, second)) return false;
            var context = SyntheticRelationshipContext.Current;
            return context?.Operation != SyntheticRelationshipOperation.Breakup
                || !context.Contains(first) || !context.Contains(second);
        }

        public static List<Pawn> GetPartners(Pawn pawn)
        {
            var result = new List<Pawn>();
            if (pawn.relations == null) return result;
            foreach (DirectPawnRelation relation in pawn.relations.DirectRelations)
                if (IsLoveRelation(relation.def) && relation.otherPawn != null
                    && !result.Contains(relation.otherPawn)) result.Add(relation.otherPawn);
            return result;
        }

        public static bool HasPartner(Pawn pawn)
        {
            if (pawn.relations == null) return false;
            foreach (DirectPawnRelation relation in pawn.relations.DirectRelations)
                if (IsLoveRelation(relation.def) && relation.otherPawn != null) return true;
            return false;
        }

        internal static bool HasProtectedPartner(Pawn pawn)
        {
            if (pawn.relations == null) return false;
            foreach (DirectPawnRelation relation in pawn.relations.DirectRelations)
                if (IsLoveRelation(relation.def) && IsProtectedPair(pawn, relation.otherPawn)) return true;
            return false;
        }

        public static bool CanAct(Pawn? pawn) => pawn != null && pawn.Spawned && !pawn.Dead
            && !pawn.Destroyed && !pawn.Downed && !pawn.Drafted && !pawn.InMentalState
            && !pawn.IsBurning() && pawn.Awake() && pawn.jobs != null
            && pawn.health?.capacities?.CanBeAwake == true;

        public static SyntheticRelationshipFailure? PursuitReason(Pawn mech, Pawn? target = null)
        {
            if (!HasModule(mech) || mech.Faction != Faction.OfPlayer || mech.relations == null)
                return SyntheticRelationshipFailure.InvalidMech;
            // UI 已隐藏追求入口；仍须拦截旧菜单、排队工作及关系变化后的再次结算。
            if (HasPartner(mech)) return SyntheticRelationshipFailure.AlreadyPartnered;
            if (mech.Downed) return SyntheticRelationshipFailure.Downed;
            if (mech.Drafted) return SyntheticRelationshipFailure.Drafted;
            if (mech.InMentalState) return SyntheticRelationshipFailure.MentalState;
            if (!CanAct(mech)) return SyntheticRelationshipFailure.CannotAct;
            if (target == null) return null;
            if (target.ageTracker != null && target.ageTracker.AgeBiologicalYearsFloat < 16f)
                return SyntheticRelationshipFailure.TargetUnder16;
            string? invalidTargetReason = InvalidPursuitTargetReason(mech, target);
            if (invalidTargetReason != null)
            {
                // 菜单每帧及工作执行期间都会检查，同一对象对、同一原因只警告一次。
                int warningKey = unchecked(((879346610 * 397 ^ mech.thingIDNumber) * 397
                    ^ target.thingIDNumber) * 397 ^ invalidTargetReason.GetHashCode());
                Log.WarningOnce($"[MAP-机械族机械师] 仿生伴侣追求目标不合法：发起者={mech.LabelShort}({mech.ThingID})，"
                    + $"目标={target.LabelShort}({target.ThingID})，原因={invalidTargetReason}", warningKey);
                return SyntheticRelationshipFailure.InvalidTarget;
            }
            // 与原版手动追求相同的近亲标准；直接求值，不能受社交面板隐藏间接关系影响。
            foreach (PawnRelationDef relation in DefDatabase<PawnRelationDef>.AllDefsListForReading)
                if (relation.romanceChanceFactor != 1f
                    && (relation.Worker.InRelation(mech, target) || relation.Worker.InRelation(target, mech)))
                    return SyntheticRelationshipFailure.Related;
            if (!CanAct(target)) return SyntheticRelationshipFailure.TargetCannotAct;
            if (mech.HostileTo(target) || !mech.CanReach(target, PathEndMode.Touch, Danger.Some))
                return SyntheticRelationshipFailure.Unreachable;
            return null;
        }

        private static string? InvalidPursuitTargetReason(Pawn mech, Pawn target)
        {
            if (target == mech) return "目标是发起者自身。";
            if (target.Dead) return "目标已死亡。";
            if (!target.Spawned) return "目标未生成在地图上。";
            if (target.Map != mech.Map) return "目标与发起者不在同一地图。";
            if (target.Faction != Faction.OfPlayer) return "目标不属于玩家阵营。";
            if (!target.IsFreeColonist) return "目标不是自由殖民者。";
            if (!target.RaceProps.Humanlike) return "目标不是类人种族。";
            if (target.RaceProps.IsMechanoid) return "目标是机械体。";
            if (HasModule(target)) return "目标自身拥有仿生伴侣模块授权。";
            if (target.IsQuestLodger()) return "目标是任务寄居者。";
            if (target.relations == null) return "目标缺少关系 Tracker。";
            if (target.ageTracker == null) return "目标缺少年龄 Tracker。";
            if (target.DevelopmentalStage != DevelopmentalStage.Adult) return "目标未达到物种成年阶段。";
            return null;
        }

        public static bool IsDirectedPursuit(Pawn mech, Pawn target) =>
            mech.CurJobDef == MAPMechanitor_JobDefOf.MAP_SyntheticRomance
            && mech.CurJob?.targetA.Pawn == target && PursuitReason(mech, target) == null;

        public static float ProposalWeight(Pawn first, Pawn second)
        {
            if (!IsProtectedPair(first, second)) return 0f;
            DirectPawnRelation? relation = first.relations.GetDirectRelation(PawnRelationDefOf.Lover, second);
            if (relation == null) return 0f;
            float days = (Find.TickManager.TicksGame - relation.startTicks) / 60000f;
            return 0.4f * Mathf.InverseLerp(0f, 60f, days);
        }

        public static SyntheticRelationshipFailure? ProposalReason(Pawn first, Pawn second)
        {
            if (!IsProtectedPair(first, second)
                || !first.relations.DirectRelationExists(PawnRelationDefOf.Lover, second))
                return SyntheticRelationshipFailure.NotLovers;
            if (!CanAct(first) || !CanAct(second) || first.Map != second.Map
                || first.HostileTo(second) || first.Faction != Faction.OfPlayer
                || second.Faction != Faction.OfPlayer || first.Map.dangerWatcher.DangerRating != StoryDanger.None)
                return SyntheticRelationshipFailure.ProposalUnavailable;
            if (first.jobs.curDriver?.DesiredSocialMode() == RandomSocialMode.Off
                || second.jobs.curDriver?.DesiredSocialMode() == RandomSocialMode.Off
                || first.mindState?.duty?.SocialModeMax == RandomSocialMode.Off
                || second.mindState?.duty?.SocialModeMax == RandomSocialMode.Off)
                return SyntheticRelationshipFailure.InteractionUnavailable;
            if (first.interactions == null || !first.interactions.CanInteractNowWith(second, InteractionDefOf.MarriageProposal))
                return SyntheticRelationshipFailure.InteractionUnavailable;
            return null;
        }

        public static SyntheticRelationshipFailure? IntimacyReason(Pawn first, Pawn second)
        {
            Pawn? mech = HasModule(first) ? first : HasModule(second) ? second : null;
            Pawn human = mech == first ? second : first;
            if (mech == null || HasModule(human) || !human.RaceProps.Humanlike
                || !SyntheticCompanionStateUtility.IsSyntheticCompanion(mech)
                || !HasLoveRelation(mech, human)) return SyntheticRelationshipFailure.NotPartners;
            if (!SyntheticCompanionStateUtility.IsLovinWithSpouseEnabled(mech))
                return SyntheticRelationshipFailure.IntimacyOff;
            if (mech.Dead || human.Dead || !mech.Spawned || !human.Spawned || mech.Map != human.Map)
                return SyntheticRelationshipFailure.TargetCannotAct;
            if (ModsConfig.IdeologyActive && !BedUtility.WillingToShareBed(mech, human))
                return SyntheticRelationshipFailure.IdeologyForbids;
            var traits = human.story?.traits;
            if (traits != null && (traits.HasTrait(TraitDefOf.Asexual)
                || (!traits.HasTrait(TraitDefOf.Bisexual)
                    && (traits.HasTrait(TraitDefOf.Gay) ? human.gender != mech.gender : human.gender == mech.gender))))
                return SyntheticRelationshipFailure.IntimacyUnwilling;
            return null;
        }

        public static void ResetForNewRelationship(Pawn mech)
        {
            SyntheticCompanionStateUtility.DisableLovinWithSpouse(mech);
            SyntheticCompanionStateUtility.ResetPregnancyApproachToAvoid(mech);
            SocialCardUtility.ClearCaches();
        }

        public static bool TryEndRelationship(Pawn mech, Pawn other)
        {
            if (!HasModule(mech) || mech.Faction != Faction.OfPlayer || !HasLoveRelation(mech, other)) return false;
            using (SyntheticRelationshipContext.Enter(SyntheticRelationshipOperation.Breakup, mech, other))
            {
                bool married = mech.relations.DirectRelationExists(PawnRelationDefOf.Spouse, other);
                mech.relations.TryRemoveDirectRelation(PawnRelationDefOf.Lover, other);
                mech.relations.TryRemoveDirectRelation(PawnRelationDefOf.Fiance, other);
                mech.relations.TryRemoveDirectRelation(PawnRelationDefOf.Spouse, other);
                PawnRelationDef previous = married ? PawnRelationDefOf.ExSpouse : PawnRelationDefOf.ExLover;
                if (!mech.relations.DirectRelationExists(previous, other)) mech.relations.AddDirectRelation(previous, other);
                RemoveMarriageMemories(mech, other);
                RemoveMarriageMemories(other, mech);
                if (!other.Dead)
                    other.needs?.mood?.thoughts.memories.TryGainMemory(
                        married ? ThoughtDefOf.DivorcedMe : ThoughtDefOf.BrokeUpWithMe, mech);
                if (mech.ownership?.OwnedBed != null && mech.ownership.OwnedBed == other.ownership?.OwnedBed)
                    mech.ownership.UnclaimBed();
                StopPairJobs(mech, other);
                TaleRecorder.RecordTale(TaleDefOf.Breakup, mech, other);
                Find.LetterStack.ReceiveLetter("LetterLabelBreakup".Translate(),
                    "LetterNoLongerLovers".Translate(mech.LabelShort, other.LabelShort,
                        mech.Named("PAWN1"), other.Named("PAWN2")),
                    LetterDefOf.NegativeEvent, new LookTargets(mech, other));
                SocialCardUtility.ClearCaches();
                return true;
            }
        }

        private static void RemoveMarriageMemories(Pawn pawn, Pawn other)
        {
            var memories = pawn.needs?.mood?.thoughts.memories;
            memories?.RemoveMemoriesOfDefWhereOtherPawnIs(ThoughtDefOf.GotMarried, other);
            memories?.RemoveMemoriesOfDefWhereOtherPawnIs(ThoughtDefOf.HoneymoonPhase, other);
        }

        internal static void StopPairJobs(Pawn first, Pawn second)
        {
            EndPairJob(first, second);
            EndPairJob(second, first);
        }

        private static void EndPairJob(Pawn pawn, Pawn other)
        {
            pawn.jobs?.jobQueue?.RemoveAll(pawn, queued => queued.targetA.Pawn == other
                && (queued.def == MAPMechanitor_JobDefOf.MAP_SyntheticRomance
                    || queued.def == JobDefOf.TryRomance || queued.def == JobDefOf.Lovin
                    || queued.def == JobDefOf.MarryAdjacentPawn));
            if (!pawn.Dead && pawn.Spawned && pawn.CurJob?.targetA.Pawn == other
                && (pawn.CurJobDef == MAPMechanitor_JobDefOf.MAP_SyntheticRomance
                    || pawn.CurJobDef == JobDefOf.TryRomance || pawn.CurJobDef == JobDefOf.Lovin
                    || pawn.CurJobDef == JobDefOf.MarryAdjacentPawn))
                pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced);
        }
    }

    internal enum SyntheticRelationshipOperation { Romance, Marriage, Breakup, IdeologyCleanup, CeremonyStart }

    /// <summary>只在同步结算期间存活；Finalizer/using 恢复上一层，绝不跨帧或存档。</summary>
    internal sealed class SyntheticRelationshipContext : IDisposable
    {
        [ThreadStatic] internal static SyntheticRelationshipContext? Current;
        internal readonly SyntheticRelationshipOperation Operation;
        internal readonly Pawn First;
        internal readonly Pawn? Second;
        private readonly SyntheticRelationshipContext? previous;
        private bool disposed;

        private SyntheticRelationshipContext(SyntheticRelationshipOperation operation, Pawn first, Pawn? second)
        {
            Operation = operation; First = first; Second = second;
            previous = Current; Current = this;
        }
        internal static SyntheticRelationshipContext Enter(SyntheticRelationshipOperation operation, Pawn first, Pawn? second)
            => new SyntheticRelationshipContext(operation, first, second);
        internal bool Contains(Pawn pawn) => pawn == First || pawn == Second;
        public void Dispose()
        {
            if (disposed) return;
            Current = previous;
            disposed = true;
        }
    }
}
