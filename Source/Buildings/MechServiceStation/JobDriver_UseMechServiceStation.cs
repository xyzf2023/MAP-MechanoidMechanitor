using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public sealed class JobDriver_UseMechServiceStation : JobDriver
    {
        private int token = -1;
        private int repairTicks;
        private int waitTicks;
        private bool servicing;
        private bool undraftingForService;
        public override bool PlayerInterruptable => !undraftingForService && base.PlayerInterruptable;
        public CompMechServiceStation? Station => job.targetA.Thing?.TryGetComp<CompMechServiceStation>();
        public bool IsServicing => servicing && Station is CompMechServiceStation station
            && station.Owns(pawn, job, token) && MechServicePolicyUtility.IsAllowed(station, pawn)
            && pawn.Position == station.ServiceCell && pawn.pather?.Moving != true
            && pawn.flight?.Flying != true && (pawn.flight == null || pawn.flight.PositionOffsetFactor <= 0f);
        public bool IsPoweredService => IsServicing && Station!.Powered;

        public override Vector3 ForcedBodyOffset
        {
            get
            {
                if (!IsServicing) return Vector3.zero;
                Vector3 offset = Station!.parent.DrawPos - pawn.Position.ToVector3Shifted();
                offset.y = 0f;
                return offset;
            }
        }

        // 抢占和预约在首个 Toil 一并提交，查询候选时不修改占用。
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            AddFailCondition(() => Station == null || !MechServicePolicyUtility.IsAllowed(Station, pawn)
                || Station.parent.IsForbidden(pawn)
                || (job.def == MechServiceStationDefOf.MAP_Job_ReceiveMechService
                    && pawn.Position != Station.ServiceCell));
            AddFinishAction(condition =>
            {
                servicing = false;
                Station?.Release(pawn, job, token);
                if (condition != JobCondition.Succeeded && pawn.Map != null)
                    pawn.Map.GetComponent<MapComponent_MechServiceStations>().DelayAutomaticRetry(pawn);
            });

            Toil acquire = ToilMaker.MakeToil("AcquireMechServiceStation");
            acquire.defaultCompleteMode = ToilCompleteMode.Never;
            acquire.initAction = () =>
            {
                if (job.playerForced) Station?.RegisterManual(pawn);
                TryAcquire();
            };
            acquire.tickIntervalAction = delta =>
            {
                waitTicks += delta;
                if (!job.playerForced && waitTicks >= 180)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                if (pawn.IsHashIntervalTick(30, delta)) TryAcquire();
            };
            yield return acquire;

            Toil move = Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            move.AddFailCondition(() => Station == null || !Station.Owns(pawn, job, token));
            yield return move;

            Toil service = ToilMaker.MakeToil("UseMechServiceStation");
            service.defaultCompleteMode = ToilCompleteMode.Never;
            service.handlingFacing = true;
            service.initAction = () =>
            {
                // 原版取消征召会中断可打断任务；只在设置征召状态的瞬间保护本任务。
                if (pawn.Drafted && pawn.drafter != null)
                {
                    undraftingForService = true;
                    try { pawn.drafter.Drafted = false; }
                    finally { undraftingForService = false; }
                }
                servicing = true;
                pawn.jobs.posture = pawn.Downed ? PawnPosture.LayingOnGroundNormal : PawnPosture.Standing;
                job.overrideFacing = Rot4.South;
                pawn.Rotation = Rot4.South;
                repairTicks = Mathf.Max(1, Station!.Props.repairIntervalTicks);
                Station.UpdateRequestedPower();
            };
            service.tickIntervalAction = _ =>
            {
                if (!IsServicing) EndJobWith(JobCondition.Incompletable);
                else
                {
                    pawn.Rotation = Rot4.South;
                    pawn.jobs.posture = pawn.Downed ? PawnPosture.LayingOnGroundNormal : PawnPosture.Standing;
                }
            };
            yield return service;
        }

        private void TryAcquire()
        {
            CompMechServiceStation? station = Station;
            if (station == null || !station.CanReach(pawn, job.playerForced))
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }
            if (!MechServicePolicyUtility.HasEntryNeed(station, pawn, job.playerForced))
            {
                EndJobWith(JobCondition.Succeeded);
                return;
            }
            if (station.TryAcquire(pawn, job, out token))
            {
                job.targetB = station.ServiceCell;
                ReadyForNextToil();
            }
        }

        // 由建筑 Normal Tick 调用，能量与维修只有此处写入，不随 Pawn 的 Tick 间隔变化。
        internal void ServiceTick()
        {
            CompMechServiceStation? station = Station;
            if (station == null || !IsServicing) return;
            pawn.Rotation = Rot4.South;
            if (station.Complete(pawn) && (!station.StandbyAfterService || station.Powered))
            {
                CompleteService(station);
                return;
            }
            if (!station.Powered)
            {
                station.Visuals.Stop();
                return;
            }
            bool charging = station.NeedsCharge(pawn);
            bool repairing = station.NeedsRepair(pawn);
            if (charging)
            {
                Need_MechEnergy energy = pawn.needs.energy;
                energy.CurLevel = Mathf.Min(energy.MaxLevel, energy.CurLevel + station.Props.energyPerTick);
            }
            if (repairing && --repairTicks <= 0)
            {
                repairTicks = Mathf.Max(1, station.Props.repairIntervalTicks);
                for (int i = 0; i < station.Props.repairAmount && station.NeedsRepair(pawn); i++)
                    MechRepairUtility.RepairTick(pawn);
            }
            station.Visuals.Tick(pawn, station.NeedsCharge(pawn), station.NeedsRepair(pawn));
            if (station.Complete(pawn)) CompleteService(station);
        }

        private void CompleteService(CompMechServiceStation station)
        {
            if (!station.StandbyAfterService)
            {
                EndJobWith(JobCondition.Succeeded);
                return;
            }
            // 先通过原版清理释放会话、预约和特效，禁止在交接间隙寻找自动工作。
            Pawn user = pawn;
            user.jobs.EndCurrentJob(JobCondition.Succeeded, startNewJob: false);
            if (user.CurJob == null && MechServiceStandbyUtility.CanWait(user))
            {
                Job standby = JobMaker.MakeJob(MechServiceStationDefOf.MAP_Job_MechServiceStandby, station.parent);
                standby.expiryInterval = -1;
                user.jobs.StartJob(standby);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref token, "serviceToken", -1);
            Scribe_Values.Look(ref repairTicks, "serviceRepairTicks");
            Scribe_Values.Look(ref waitTicks, "serviceWaitTicks");
            Scribe_Values.Look(ref servicing, "servicing");
        }
    }
}
