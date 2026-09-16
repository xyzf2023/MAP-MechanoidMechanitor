using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompMechServiceStation : ThingComp
    {
        private MechServiceStationMode mode;
        private bool mechanitorPriority;
        private Pawn? assignedPawn;
        private Pawn? owner;
        private int ownerJobId = -1;
        private int sessionId;
        private bool forcedSession;
        private MechServiceStationMode sessionMode;
        private List<Pawn> manualQueue = new List<Pawn>();
        private bool transferring;
        private CompPowerTrader? power;
        private MechServiceStationVisuals? visuals;

        public MechServiceStationMode Mode => mode;
        public bool MechanitorPriority => mechanitorPriority;
        public Pawn? AssignedPawn => assignedPawn;
        public Pawn? Owner => owner;
        public IntVec3 ServiceCell => parent.Position;
        public bool Powered => power?.PowerOn == true;
        public CompProperties_MechServiceStation Props => (CompProperties_MechServiceStation)props;
        public MechServiceStationVisuals Visuals => visuals ??= new MechServiceStationVisuals(this);

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            power = parent.TryGetComp<CompPowerTrader>();
            parent.Map.GetComponent<MapComponent_MechServiceStations>().Register(this);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref mode, "serviceMode", MechServiceStationMode.AsNeeded);
            Scribe_Values.Look(ref mechanitorPriority, "mechanitorPriority");
            Scribe_References.Look(ref assignedPawn, "assignedMech");
            Scribe_References.Look(ref owner, "serviceOwner");
            Scribe_Values.Look(ref ownerJobId, "serviceOwnerJobId", -1);
            Scribe_Values.Look(ref sessionId, "serviceSessionId");
            Scribe_Values.Look(ref forcedSession, "serviceForced");
            Scribe_Values.Look(ref sessionMode, "serviceSessionMode", MechServiceStationMode.AsNeeded);
            Scribe_Collections.Look(ref manualQueue, "serviceManualQueue", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                manualQueue ??= new List<Pawn>();
                if (!Enum.IsDefined(typeof(MechServiceStationMode), mode)) mode = MechServiceStationMode.AsNeeded;
                if (!Enum.IsDefined(typeof(MechServiceStationMode), sessionMode)) sessionMode = mode;
            }
        }

        public bool Owns(Pawn pawn, Job job, int token) =>
            owner == pawn && ownerJobId == job.loadID && sessionId == token;

        public bool Complete(Pawn pawn) => MechServicePolicyUtility.IsComplete(pawn, sessionMode, forcedSession);

        public void PromoteToManual(Pawn pawn, Job job)
        {
            job.playerForced = true;
            if (owner == pawn && ownerJobId == job.loadID)
            {
                forcedSession = true;
                sessionMode = MechServiceStationMode.AsNeeded;
            }
            else RegisterManual(pawn);
        }

        private bool HasStationJob(Pawn pawn) => pawn?.CurJobDef == MechServiceStationDefOf.MAP_Job_UseMechServiceStation
            && pawn.CurJob.targetA.Thing == parent;

        public void RegisterManual(Pawn pawn)
        {
            if (!manualQueue.Contains(pawn)) manualQueue.Add(pawn);
        }

        private void PruneRequests()
        {
            manualQueue.RemoveAll(p => p == null || !MechServicePolicyUtility.IsAllowed(this, p)
                || !HasStationJob(p) || !p.CurJob.playerForced);
        }

        public bool CanRequest(Pawn pawn, bool forced)
        {
            if (transferring || !Powered || !MechServicePolicyUtility.IsAllowed(this, pawn)
                || !MechServicePolicyUtility.HasEntryNeed(this, pawn, forced)) return false;
            if (!forced && manualQueue.Count > 0) return false;
            if (owner != null && owner != pawn
                && MechServicePolicyUtility.Priority(this, pawn, forced)
                    <= MechServicePolicyUtility.Priority(this, owner, forcedSession)) return false;
            return CanReach(pawn, forced) && ReservationsPermit(pawn);
        }

        public bool CanReach(Pawn pawn, bool forced) => parent.Spawned && pawn.Map == parent.Map
            && !parent.IsForbidden(pawn) && ServiceCell.InBounds(pawn.Map)
            && (forced || !ServiceCell.IsForbidden(pawn))
            && pawn.CanReach(ServiceCell, PathEndMode.OnCell, forced ? Danger.Deadly : Danger.Some);

        // 只忽略即将交接的本会话预约，第三方预约仍然有效。
        private bool ReservationsPermit(Pawn pawn)
        {
            Map map = parent.Map;
            foreach (ReservationManager.Reservation reservation in map.reservationManager.ReservationsReadOnly)
            {
                if (reservation.Claimant == pawn) continue;
                if (reservation.Target != (LocalTargetInfo)parent && reservation.Target != (LocalTargetInfo)ServiceCell) continue;
                if (reservation.Claimant == owner && reservation.Job?.loadID == ownerJobId) continue;
                return false;
            }
            return (!map.physicalInteractionReservationManager.IsReserved(parent)
                    || map.physicalInteractionReservationManager.IsReservedBy(pawn, parent))
                && (!map.physicalInteractionReservationManager.IsReserved(ServiceCell)
                    || map.physicalInteractionReservationManager.IsReservedBy(pawn, ServiceCell));
        }

        public bool TryAcquire(Pawn pawn, Job job, out int token)
        {
            token = sessionId;
            if (owner == pawn && ownerJobId == job.loadID) return true;
            PruneRequests();
            if (job.playerForced && manualQueue.Count > 0 && manualQueue[0] != pawn) return false;
            if (!CanRequest(pawn, job.playerForced)) return false;
            transferring = true;
            try
            {
                Pawn? previous = owner;
                int previousJobId = ownerJobId;
                if (previous != null && HasStationJob(previous) && previous.CurJob.loadID == previousJobId)
                    previous.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                ClearOwner();
                if (!pawn.Reserve(parent, job, 1, -1, null, false)) return false;
                if (!pawn.Reserve(ServiceCell, job, 1, -1, null, false))
                {
                    parent.Map.reservationManager.Release(parent, pawn, job);
                    return false;
                }
                owner = pawn;
                ownerJobId = job.loadID;
                forcedSession = job.playerForced;
                sessionMode = mode;
                sessionId = sessionId == int.MaxValue ? 1 : sessionId + 1;
                token = sessionId;
                manualQueue.Remove(pawn);
                VacateServiceCell(previous);
                return true;
            }
            finally { transferring = false; }
        }

        private void VacateServiceCell(Pawn? previous)
        {
            if (previous == null || !MechServicePolicyUtility.IsLegalPawn(previous)
                || previous.Map != parent.Map || previous.Position != ServiceCell || previous.CurJob != null) return;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(ServiceCell, 5f, false))
            {
                if (!cell.InBounds(parent.Map) || parent.OccupiedRect().Contains(cell)
                    || !cell.Standable(parent.Map) || cell.IsForbidden(previous)
                    || cell.GetFirstPawn(parent.Map) != null
                    || !previous.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.Some)) continue;
                previous.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto, cell));
                break;
            }
        }

        public void Release(Pawn pawn, Job job, int token)
        {
            manualQueue.Remove(pawn);
            if (Owns(pawn, job, token)) ClearOwner();
        }

        private void ClearOwner()
        {
            // 异常换任务/离图时也只释放本会话的预约。
            if (owner != null && parent.Map != null)
            {
                Job? reservedJob = null;
                foreach (ReservationManager.Reservation reservation in parent.Map.reservationManager.ReservationsReadOnly)
                    if (reservation.Claimant == owner && reservation.Job?.loadID == ownerJobId)
                    { reservedJob = reservation.Job; break; }
                if (reservedJob != null) parent.Map.reservationManager.ReleaseClaimedBy(owner, reservedJob);
            }
            owner = null;
            ownerJobId = -1;
            forcedSession = false;
            visuals?.Stop();
        }

        public override void CompTick()
        {
            base.CompTick();
            if (owner != null && (!HasStationJob(owner) || owner.CurJob.loadID != ownerJobId
                || !MechServicePolicyUtility.IsAllowed(this, owner))) ClearOwner();
            if (parent.IsHashIntervalTick(120)) PruneRequests();
            if (owner?.jobs?.curDriver is not JobDriver_UseMechServiceStation driver || !driver.IsServicing)
                visuals?.Stop();
            else driver.ServiceTick();
        }

        public override void PostDraw()
        {
            base.PostDraw();
            Visuals.Draw();
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            map.GetComponent<MapComponent_MechServiceStations>().Unregister(this);
            ClearOwner();
            manualQueue.Clear();
            base.PostDeSpawn(map, mode);
        }

        private void ChangePolicy()
        {
            PruneRequests();
            if (owner != null && !MechServicePolicyUtility.IsAllowed(this, owner))
            {
                Pawn previous = owner;
                ClearOwner();
                if (HasStationJob(previous)) previous.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            }
            // 手动会话固定按需结束；自动会话随玩家修改的模式更新。
            if (!forcedSession) sessionMode = mode;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction != Faction.OfPlayer) yield break;
            yield return new Command_Action
            {
                defaultLabel = MechServicePolicyUtility.Label(mode),
                defaultDesc = "设置使用规则。",
                action = () =>
                {
                    var options = new List<FloatMenuOption>();
                    foreach (MechServiceStationMode value in Enum.GetValues(typeof(MechServiceStationMode)))
                    {
                        MechServiceStationMode choice = value;
                        options.Add(new FloatMenuOption(MechServicePolicyUtility.Label(choice), () =>
                        { mode = choice; ChangePolicy(); }));
                    }
                    Find.WindowStack.Add(new FloatMenu(options));
                }
            };
            if (mode != MechServiceStationMode.AssignedOnly && mode != MechServiceStationMode.MechanitorOnly)
                yield return new Command_Toggle
                {
                    defaultLabel = "机械族机械师优先",
                    defaultDesc = "允许按需抢占普通自动使用者。",
                    isActive = () => mechanitorPriority,
                    toggleAction = () => { mechanitorPriority = !mechanitorPriority; ChangePolicy(); }
                };
            if (mode == MechServiceStationMode.AssignedOnly || mode == MechServiceStationMode.AssignedPriority)
                yield return new Command_Action
                {
                    defaultLabel = assignedPawn?.LabelShortCap ?? "指定机械族",
                    defaultDesc = "选择机械族。",
                    action = () =>
                    {
                        var options = new List<FloatMenuOption>
                        { new FloatMenuOption("清除指定", () => { assignedPawn = null; ChangePolicy(); }) };
                        foreach (Pawn candidate in parent.Map.mapPawns.AllPawnsSpawned)
                        {
                            if (candidate.Dead || candidate.Faction != Faction.OfPlayer || !candidate.RaceProps.IsMechanoid) continue;
                            Pawn selected = candidate;
                            options.Add(new FloatMenuOption(selected.LabelShortCap, () =>
                            { assignedPawn = selected; ChangePolicy(); }));
                        }
                        Find.WindowStack.Add(new FloatMenu(options));
                    }
                };
        }

        public override string CompInspectStringExtra()
        {
            string state = !Powered ? "断电暂停" : owner == null ? "空闲"
                : owner.jobs?.curDriver is JobDriver_UseMechServiceStation driver && driver.IsServicing
                    ? MechServiceNeedUtility.NeedsRepair(owner)
                        ? MechServiceNeedUtility.NeedsCharge(owner) ? "充电并维修" : "维修中"
                        : "充电中"
                    : "等待到站";
            return MechServicePolicyUtility.Label(mode) + "：" + state
                + (owner == null ? "" : "\n使用者：" + owner.LabelShortCap)
                + (manualQueue.Count == 0 ? "" : "\n等候：" + manualQueue.Count);
        }
    }
}
