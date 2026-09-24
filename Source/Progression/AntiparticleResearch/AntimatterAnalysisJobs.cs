using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    internal static class AntimatterAnalysisUtility
    {
        // 资格只看当前智识等级，不检查科研速度、工作禁用、兴趣、身份或种族。
        internal static bool Qualified(Pawn pawn) => pawn.skills?.GetSkill(SkillDefOf.Intellectual)?.Level >= 12;

        internal static bool BenchUsable(Building_ResearchBench? bench, Pawn pawn) =>
            bench != null && bench.Spawned && bench.Map == pawn.Map && bench.Faction == Faction.OfPlayer
            && !bench.IsForbidden(pawn) && !bench.IsBurning()
            && (bench.TryGetComp<CompPowerTrader>()?.PowerOn ?? true);

        internal static Building_ResearchBench? FindBench(Pawn pawn) =>
            GenClosest.ClosestThingReachable(pawn.Position, pawn.Map,
                ThingRequest.ForGroup(ThingRequestGroup.ResearchBench), PathEndMode.InteractionCell,
                TraverseParms.For(pawn, Danger.Some), 9999f,
                thing => thing is Building_ResearchBench bench && BenchUsable(bench, pawn)
                    && pawn.CanReserve(bench) && pawn.CanReserve(bench.Position)
                    && pawn.CanReserve(bench.InteractionCell)) as Building_ResearchBench;

        internal static Job? MakeJob(Pawn pawn, Thing device, bool forced) =>
            MakeJob(pawn, device, forced, out _);

        // 菜单说明与实际派工共用校验，避免显示的禁用原因与执行条件不一致。
        internal static Job? MakeJob(Pawn pawn, Thing device, bool forced, out string disabledReasonKey)
        {
            disabledReasonKey = "MAP_Antiparticle.Unavailable";
            if (!pawn.Spawned || !Qualified(pawn) || GameComponent_AntiparticleResearch.Discovered
                || !device.Spawned || device.Map != pawn.Map || device.TryGetComp<CompAntimatterAnalysis>() == null
                || (!forced && device.IsForbidden(pawn)))
                return null;
            if (!pawn.CanReach(device, PathEndMode.ClosestTouch, Danger.Some))
            {
                disabledReasonKey = "MAP_Antiparticle.ItemUnreachable";
                return null;
            }
            if (!pawn.CanReserve(device))
            {
                disabledReasonKey = "MAP_Antiparticle.AnalysisInProgress";
                return null;
            }
            Building_ResearchBench? bench = FindBench(pawn);
            if (bench == null)
            {
                disabledReasonKey = "MAP_Antiparticle.NoResearchBench";
                return null;
            }
            Job job = JobMaker.MakeJob(AntiparticleResearchDefOf.MAP_AnalyzeAntimatterContainmentDevice,
                device, bench, bench.Position);
            job.count = 1;
            return job;
        }
    }

    public sealed class FloatMenuOptionProvider_AntimatterAnalysis : FloatMenuOptionProvider
    {
        protected override bool Drafted => false;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool MechanoidCanDo => true;

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing clickedThing, FloatMenuContext context)
        {
            CompAntimatterAnalysis? comp = clickedThing.TryGetComp<CompAntimatterAnalysis>();
            if (comp == null) yield break;
            if (GameComponent_AntiparticleResearch.Discovered)
            {
                yield return new FloatMenuOption("MAP_Antiparticle.AnalyzeCompleted".Translate(), null);
                yield break;
            }
            Pawn pawn = context.FirstSelectedPawn;
            string label = "MAP_Antiparticle.Analyze".Translate();
            if (!AntimatterAnalysisUtility.Qualified(pawn))
            {
                yield return new FloatMenuOption(label + ": " + "MAP_Antiparticle.SkillRequired".Translate(), null);
                yield break;
            }
            if (AntimatterAnalysisUtility.MakeJob(pawn, clickedThing, true, out string disabledReasonKey) == null)
            {
                yield return new FloatMenuOption(label + ": " + disabledReasonKey.Translate(), null);
                yield break;
            }
            yield return new FloatMenuOption(label, () =>
            {
                Job? job = AntimatterAnalysisUtility.MakeJob(pawn, clickedThing, true);
                if (job == null) return;
                clickedThing.SetForbidden(false, false);
                comp.Request();
                pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            });
        }
    }

    /// <summary>只自动继续玩家已下令分析的物品，沿用科研工作调度。</summary>
    public sealed class WorkGiver_AntimatterAnalysis : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest =>
            ThingRequest.ForDef(AntiparticleResearchDefOf.MAP_AntimatterContainmentDevice);

        public override bool ShouldSkip(Pawn pawn, bool forced = false) =>
            GameComponent_AntiparticleResearch.Discovered || !AntimatterAnalysisUtility.Qualified(pawn);

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            t.TryGetComp<CompAntimatterAnalysis>()?.Requested == true
            && AntimatterAnalysisUtility.MakeJob(pawn, t, forced) != null;

        public override Job? JobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            AntimatterAnalysisUtility.MakeJob(pawn, t, forced);
    }

    public sealed class JobDriver_AntimatterAnalysis : JobDriver
    {
        private CompAntimatterAnalysis? Analysis => TargetThingA?.TryGetComp<CompAntimatterAnalysis>();
        private Building_ResearchBench? Bench => TargetThingB as Building_ResearchBench;

        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(TargetA, job, 1, -1, null, errorOnFailed)
            && pawn.Reserve(TargetB, job, 1, -1, null, errorOnFailed)
            && pawn.Reserve(TargetC, job, 1, -1, null, errorOnFailed)
            && Bench != null && pawn.Reserve(Bench.InteractionCell, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddEndCondition(() => GameComponent_AntiparticleResearch.Discovered
                ? JobCondition.Succeeded : JobCondition.Ongoing);
            this.FailOnDestroyedNullOrForbidden(TargetIndex.A);
            this.FailOnDespawnedNullOrForbidden(TargetIndex.B);
            this.FailOn(() => Analysis?.Requested != true || !AntimatterAnalysisUtility.Qualified(pawn)
                || !AntimatterAnalysisUtility.BenchUsable(Bench, pawn));

            // 已在研究台上的物品无需每次工作调度都重新搬起。
            Toil arrive = Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.InteractionCell);
            yield return Toils_Jump.JumpIf(arrive, () => TargetThingA.Spawned && TargetThingA.Position == TargetC.Cell);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(TargetIndex.A).FailOnSomeonePhysicallyInteracting(TargetIndex.A);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A);
            yield return arrive;
            // 自定义放置以明确处理失败，不丢失物品，也不在仍搬运时累计分析进度。
            yield return Toils_General.DoAtomic(() =>
            {
                if (pawn.carryTracker.CarriedThing != null
                    && !pawn.carryTracker.TryDropCarriedThing(TargetC.Cell, ThingPlaceMode.Direct, out _))
                    EndJobWith(JobCondition.Incompletable);
            });

            Toil analyze = ToilMaker.MakeToil("AnalyzeAntimatterContainmentDevice");
            analyze.defaultCompleteMode = ToilCompleteMode.Delay;
            // 自动工作分段交还需求/作息调度；右键强制工作可持续完成，
            // 不要求 Pawn 开启科研工作，也不因科研工作禁用而每小时停下。
            // Delay 到期的 tick 不调用 tickAction，因此额外留一个结束 tick。
            analyze.defaultDuration = (job.playerForced ? CompAntimatterAnalysis.RequiredTicks : 2500) + 1;
            analyze.activeSkill = () => SkillDefOf.Intellectual;
            analyze.handlingFacing = true;
            analyze.FailOn(() => !TargetThingA.Spawned || TargetThingA.Position != TargetC.Cell
                || pawn.Position != Bench!.InteractionCell);
            analyze.WithProgressBar(TargetIndex.A, () => Analysis?.Progress ?? 0f);
            analyze.tickAction = () =>
            {
                pawn.rotationTracker.FaceTarget(TargetA);
                if (Analysis!.Work(1)) ReadyForNextToil();
            };
            yield return analyze;
        }
    }
}
