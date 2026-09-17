using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompMechServiceStation : ThingComp
    {
        private MechServiceStationMode mode;
        private bool mechanitorPriority;
        private bool standbyAfterService;
        private bool repairEnabled = true;
        private bool chargingEnabled = true;
        private Pawn? assignedPawn;
        private Pawn? owner;
        private Pawn? standbyPawn;
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
        public bool StandbyAfterService => standbyAfterService;
        public bool RepairEnabled => repairEnabled;
        public bool ChargingEnabled => chargingEnabled;
        public bool HasServiceFunctions => repairEnabled || chargingEnabled;
        public bool Enabled => HasServiceFunctions || standbyAfterService;
        public Pawn? AssignedPawn => assignedPawn;
        public Pawn? Owner => owner;
        public Pawn? StandbyPawn => standbyPawn != null && standbyPawn.Spawned
            && standbyPawn.Map == parent.Map && standbyPawn.Position == ServiceCell
            && MechServiceStandbyUtility.IsWaiting(standbyPawn)
            && standbyPawn.CurJob.targetA.Thing == parent ? standbyPawn : null;
        public bool IsOccupied => owner != null || StandbyPawn != null;
        public IntVec3 ServiceCell => parent.Position;
        public bool Powered => power?.PowerOn == true && FlickUtility.WantsToBeOn(parent)
            && !parent.IsBrokenDown() && parent.TryGetComp<CompStunnable>()?.StunHandler.Stunned != true;
        public CompProperties_MechServiceStation Props => (CompProperties_MechServiceStation)props;
        public MechServiceStationVisuals Visuals => visuals ??= new MechServiceStationVisuals(this);

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            power = parent.TryGetComp<CompPowerTrader>();
            parent.Map.GetComponent<MapComponent_MechServiceStations>().Register(this);
            UpdateRequestedPower();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref mode, "serviceMode", MechServiceStationMode.AsNeeded);
            Scribe_Values.Look(ref mechanitorPriority, "mechanitorPriority");
            Scribe_Values.Look(ref standbyAfterService, "standbyAfterService", false);
            Scribe_Values.Look(ref repairEnabled, "repairEnabled", true);
            Scribe_Values.Look(ref chargingEnabled, "chargingEnabled", true);
            Scribe_References.Look(ref assignedPawn, "assignedMech");
            Scribe_References.Look(ref owner, "serviceOwner");
            Scribe_References.Look(ref standbyPawn, "serviceStandbyPawn");
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

        public bool NeedsCharge(Pawn pawn) => chargingEnabled && MechServiceNeedUtility.NeedsCharge(pawn);
        public bool NeedsRepair(Pawn pawn) => repairEnabled && MechServiceNeedUtility.NeedsRepair(pawn);
        public bool HasEnabledNeed(Pawn pawn) => NeedsCharge(pawn) || NeedsRepair(pawn);

        public bool Complete(Pawn pawn)
        {
            if (!standbyAfterService && !forcedSession)
            {
                if (sessionMode == MechServiceStationMode.ChargeFirst && chargingEnabled) return !NeedsCharge(pawn);
                if (sessionMode == MechServiceStationMode.RepairFirst && repairEnabled) return !NeedsRepair(pawn);
            }
            return !HasEnabledNeed(pawn);
        }

        private void GetRequestedWork(out bool charging, out bool repairing)
        {
            charging = repairing = false;
            if (owner?.jobs?.curDriver is not JobDriver_UseMechServiceStation driver
                || !driver.IsServicing || Complete(owner)) return;
            charging = NeedsCharge(owner);
            repairing = NeedsRepair(owner);
        }

        // 请求功率不依赖 PowerOn；避免断电后回落到待机功率引发供电反复切换。
        public void UpdateRequestedPower()
        {
            if (power == null || !parent.Spawned) return;
            GetRequestedWork(out bool charging, out bool repairing);
            float watts = charging ? (repairing ? 1800f : 1000f) : repairing ? 800f : 50f;
            if (power.powerOutputInt != -watts) power.PowerOutput = -watts;
        }

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

        private bool HasStationJob(Pawn pawn) => MechServicePolicyUtility.IsServiceJob(pawn)
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
            && (pawn.Position == ServiceCell
                || pawn.CanReach(ServiceCell, PathEndMode.OnCell, forced ? Danger.Deadly : Danger.Some));

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
                ReleaseStandbyIfClaimed();
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

        public void RegisterStandby(Pawn pawn) => standbyPawn = pawn;

        public void UnregisterStandby(Pawn pawn)
        {
            if (standbyPawn == pawn) standbyPawn = null;
        }

        private void TryResumeStandbyService()
        {
            if (transferring || owner != null || !HasServiceFunctions || !Powered) return;
            Pawn? waiting = StandbyPawn;
            if (waiting == null || !MechServiceStandbyUtility.CanWait(waiting)
                || !HasEnabledNeed(waiting)) return;

            // 待命者继续使用本台，不抢占已有使用者、第三方预约或排队的手动命令。
            PruneRequests();
            if (manualQueue.Count > 0 || !CanRequest(waiting, true)) return;

            // 延续本台整备，按完整需求处理，不受自动寻台的低电阈值限制。
            // StartJob 会清理旧待命任务并注销占用，再由现有整备驱动申请预约。
            Job service = JobMaker.MakeJob(MechServiceStationDefOf.MAP_Job_UseMechServiceStation,
                parent, ServiceCell);
            service.playerForced = true;
            service.overrideFacing = Rot4.South;
            waiting.jobs.StartJob(service, JobCondition.Succeeded, tag: JobTag.SatisfyingNeeds);
        }

        // 待命是可让出的占用，不持有排他预约；只在确有新使用者/预约时让位。
        public void ReleaseStandbyIfClaimed()
        {
            Pawn? waiting = StandbyPawn;
            if (waiting == null) { standbyPawn = null; return; }
            bool claimed = owner != null && owner != waiting;
            if (!claimed)
                foreach (ReservationManager.Reservation reservation in parent.Map.reservationManager.ReservationsReadOnly)
                    if (reservation.Claimant != waiting
                        && (reservation.Target == (LocalTargetInfo)parent || reservation.Target == (LocalTargetInfo)ServiceCell))
                    { claimed = true; break; }
            if (!claimed)
            {
                var physical = parent.Map.physicalInteractionReservationManager;
                claimed = (physical.IsReserved(parent) && !physical.IsReservedBy(waiting, parent))
                    || (physical.IsReserved(ServiceCell) && !physical.IsReservedBy(waiting, ServiceCell));
            }
            if (!claimed) return;
            standbyPawn = null;
            waiting.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            parent.Map.GetComponent<MapComponent_MechServiceStations>().DelayAutomaticRetry(waiting);
            VacateServiceCell(waiting);
            if (waiting.CurJob == null) waiting.jobs.CheckForJobOverride();
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
            UpdateRequestedPower();
        }

        public override void CompTick()
        {
            base.CompTick();
            ReleaseStandbyIfClaimed();
            if (owner != null && (!HasStationJob(owner) || owner.CurJob.loadID != ownerJobId
                || !MechServicePolicyUtility.IsAllowed(this, owner))) ClearOwner();
            if (parent.IsHashIntervalTick(120)) PruneRequests();
            // 在建筑 Tick 中交接，避免策略窗口绘制或待命 Toil 初始化期间重入换任务。
            TryResumeStandbyService();
            UpdateRequestedPower();
            if (owner?.jobs?.curDriver is not JobDriver_UseMechServiceStation driver || !driver.IsServicing)
                visuals?.Stop();
            else driver.ServiceTick();
            UpdateRequestedPower();
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
            UpdateRequestedPower();
        }

        public void SetMode(MechServiceStationMode value)
        {
            if (mode == value) return;
            mode = value;
            ChangePolicy();
        }

        public void SetMechanitorPriority(bool value)
        {
            if (mechanitorPriority == value) return;
            mechanitorPriority = value;
            ChangePolicy();
        }

        // 完成时读取当前设置；不解除已经独立运行的待命任务。
        public void SetStandbyAfterService(bool value)
        {
            if (standbyAfterService == value) return;
            standbyAfterService = value;
            UpdateRequestedPower();
        }

        public void SetServiceFunctions(bool repair, bool charge)
        {
            if (repairEnabled == repair && chargingEnabled == charge) return;
            repairEnabled = repair;
            chargingEnabled = charge;
            UpdateRequestedPower();
        }

        public void OpenAssignmentMenu()
        {
            if (!parent.Spawned) return;
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

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction != Faction.OfPlayer) yield break;
            yield return new Command_Action
            {
                defaultLabel = "整备策略",
                defaultDesc = "设置使用模式、指定机械族、机械族机械师优先、整备功能和完成后待命。\n当前：" + MechServicePolicyUtility.Label(mode),
                icon = ContentFinder<Texture2D>.Get("UI/MaintenancePolicy"),
                action = () => Find.WindowStack.Add(new Dialog_MechServiceStationPolicy(this))
            };
        }

        public override string CompInspectStringExtra()
        {
            Pawn? waiting = StandbyPawn;
            GetRequestedWork(out bool charging, out bool repairing);
            string state = waiting != null && owner == null ? "占用：机械族待命中..."
                : !Powered && owner != null ? "断电暂停"
                : charging && repairing ? "正在维修并充电"
                : charging ? "正在充电" : repairing ? "正在维修" : "待命中...";
            return state + "\n整备策略：" + MechServicePolicyUtility.Label(mode)
                + (assignedPawn == null || (mode != MechServiceStationMode.AssignedOnly && mode != MechServiceStationMode.AssignedPriority)
                    ? "" : "\n指定机械族：" + assignedPawn.LabelShortCap)
                + (owner == null ? "" : "\n使用者：" + owner.LabelShortCap)
                + (waiting == null ? "" : "\n待命机械族：" + waiting.LabelShortCap)
                + (manualQueue.Count == 0 ? "" : "\n等候：" + manualQueue.Count);
        }
    }
}
