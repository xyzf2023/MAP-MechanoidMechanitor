using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    internal static class MechServiceHaulUtility
    {
        internal static bool NeedsHauling(Pawn mech) => mech.Downed || mech.IsSelfShutdown();

        internal static bool CanDeliver(Pawn carrier, Pawn mech, CompMechServiceStation station)
        {
            if (carrier == null || mech == null || carrier == mech || !carrier.Spawned
                || carrier.Dead || carrier.Downed || carrier.carryTracker == null
                || !MechServicePolicyUtility.IsEligiblePawn(mech)
                || !station.parent.Spawned || station.parent.Map != carrier.Map
                || mech.MapHeld != carrier.Map || !station.Powered || station.Owner != null
                || !MechServicePolicyUtility.MatchesUsagePolicy(station, mech)
                || !MechServicePolicyUtility.HasEntryNeed(station, mech, true)
                || station.parent.IsForbidden(carrier) || station.parent.IsForbidden(mech)
                || !station.ServiceCell.Standable(carrier.Map)) return false;

            bool carrying = carrier.carryTracker.CarriedThing == mech;
            if (!carrying && (!mech.Spawned || !NeedsHauling(mech) || mech.IsForbidden(carrier)
                || carrier.carryTracker.AvailableStackSpace(mech.def) < 1
                || !carrier.CanReserveAndReach(mech, PathEndMode.ClosestTouch, Danger.Deadly))) return false;
            return carrier.CanReserve(station.parent) && carrier.CanReserve(station.ServiceCell)
                && carrier.CanReach(station.ServiceCell, PathEndMode.OnCell, Danger.Deadly)
                && !carrier.Map.physicalInteractionReservationManager.IsReserved(station.parent)
                && !carrier.Map.physicalInteractionReservationManager.IsReserved(station.ServiceCell);
        }
    }

    // 仅由右键命令创建，不注册 WorkGiver 或自动派工节点。
    public sealed class FloatMenuOptionProvider_HaulMechToServiceStation : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool RequiresManipulation => true;
        protected override bool MechanoidCanDo => true;

        protected override FloatMenuOption? GetSingleOptionFor(Pawn clickedPawn, FloatMenuContext context)
        {
            Pawn carrier = context.FirstSelectedPawn;
            if (!ModsConfig.BiotechActive || carrier == null || !carrier.Spawned
                || clickedPawn == carrier || !MechServicePolicyUtility.IsEligiblePawn(clickedPawn)
                || !clickedPawn.Spawned || !MechServiceHaulUtility.NeedsHauling(clickedPawn)) return null;
            MapComponent_MechServiceStations registry = carrier.Map.GetComponent<MapComponent_MechServiceStations>();
            if (registry.FindDeliveryStation(carrier, clickedPawn) == null) return null;
            return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(
                "搬运至机体整备台：" + clickedPawn.LabelShortCap, () =>
                {
                    // 菜单打开后，目标、预约及策略均可能变化；点击时重新选择可用台。
                    CompMechServiceStation? station = registry.FindDeliveryStation(carrier, clickedPawn);
                    if (station == null) return;
                    Job haul = JobMaker.MakeJob(MechServiceStationDefOf.MAP_Job_HaulMechToServiceStation,
                        clickedPawn, station.parent, station.ServiceCell);
                    haul.count = 1;
                    carrier.jobs.TryTakeOrderedJob(haul, JobTag.Misc);
                }), carrier, new LocalTargetInfo(clickedPawn));
        }
    }

    public sealed class JobDriver_HaulMechToServiceStation : JobDriver
    {
        private bool deliveryCompleted;
        private Pawn? Mech => job.targetA.Thing as Pawn;
        private CompMechServiceStation? Station => job.targetB.Thing?.TryGetComp<CompMechServiceStation>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (Mech == null || Station == null || !MechServiceHaulUtility.CanDeliver(pawn, Mech, Station)) return false;
            if (pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed)
                && pawn.Reserve(job.targetB, job, 1, -1, null, errorOnFailed)
                && pawn.Reserve(job.targetC, job, 1, -1, null, errorOnFailed))
            {
                Station.ReleaseStandbyIfClaimed();
                return true;
            }
            pawn.ClearReservationsForJob(job);
            return false;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDespawnedNullOrForbidden(TargetIndex.B);
            AddFailCondition(() => !deliveryCompleted && (Mech == null || Station == null
                || !MechServiceHaulUtility.CanDeliver(pawn, Mech, Station)));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch)
                .FailOnSomeonePhysicallyInteracting(TargetIndex.A);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A);
            yield return Toils_Haul.CarryHauledThingToCell(TargetIndex.C, PathEndMode.OnCell);
            // 放下和转交在同一个步骤内完成，避免恢复意识后短暂接到其他工作。
            yield return Toils_General.Do(() =>
            {
                Pawn? mech = Mech;
                CompMechServiceStation? station = Station;
                if (mech == null || station == null || pawn.carryTracker.CarriedThing != mech
                    || !MechServiceHaulUtility.CanDeliver(pawn, mech, station)
                    || !pawn.carryTracker.TryDropCarriedThing(station.ServiceCell, ThingPlaceMode.Direct, out _)
                    || !mech.Spawned || mech.Position != station.ServiceCell)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                pawn.Map.reservationManager.Release(job.targetB, pawn, job);
                pawn.Map.reservationManager.Release(job.targetC, pawn, job);
                Job receive = JobMaker.MakeJob(MechServiceStationDefOf.MAP_Job_ReceiveMechService,
                    station.parent, station.ServiceCell);
                receive.playerForced = true;
                deliveryCompleted = true;
                mech.jobs.StartJob(receive, JobCondition.InterruptForced);
            });
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref deliveryCompleted, "deliveryCompleted");
        }
    }

    // 倒地分支之前保留已送达的整备任务；只保持任务，不自动创建搬运工作。
    public sealed class JobGiver_ContinueDeliveredMechService : ThinkNode_JobGiver
    {
        protected override Job? TryGiveJob(Pawn pawn) =>
            pawn.CurJobDef == MechServiceStationDefOf.MAP_Job_ReceiveMechService
            && pawn.jobs.curDriver is JobDriver_UseMechServiceStation driver
            && driver.Station is CompMechServiceStation station
            && MechServicePolicyUtility.IsAllowed(station, pawn) && pawn.Position == station.ServiceCell
                ? pawn.CurJob : null;
    }
}
