using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public sealed class JobGiver_ContinueMechServiceStation : ThinkNode_JobGiver
    {
        protected override Job? TryGiveJob(Pawn pawn) =>
            MechServicePolicyUtility.IsServiceJob(pawn)
            && pawn.jobs.curDriver is JobDriver_UseMechServiceStation driver
            && driver.Station is CompMechServiceStation station
            && MechServicePolicyUtility.IsAllowed(station, pawn) ? pawn.CurJob : null;
    }

    public sealed class JobGiver_MechServiceStation : ThinkNode_JobGiver
    {
        protected override Job? TryGiveJob(Pawn pawn)
        {
            // 返回当前实例，原版不会重新开始同一任务或丢失手动请求来源。
            if (MechServicePolicyUtility.IsServiceJob(pawn)
                && pawn.jobs.curDriver is JobDriver_UseMechServiceStation driver
                && driver.Station is CompMechServiceStation current
                && MechServicePolicyUtility.IsAllowed(current, pawn)) return pawn.CurJob;
            if (!MechServicePolicyUtility.CanAutoSeek(pawn)) return null;
            CompMechServiceStation? station = pawn.Map.GetComponent<MapComponent_MechServiceStations>().FindStation(pawn);
            return station == null ? null
                : JobMaker.MakeJob(MechServiceStationDefOf.MAP_Job_UseMechServiceStation, station.parent, station.ServiceCell);
        }
    }

    public sealed class MapComponent_MechServiceStations : MapComponent
    {
        private readonly List<CompMechServiceStation> stations = new List<CompMechServiceStation>();
        private readonly List<Pawn> candidates = new List<Pawn>();
        private readonly Dictionary<Pawn, int> retryAfter = new Dictionary<Pawn, int>();
        private readonly List<Pawn> expiredRetries = new List<Pawn>();

        public MapComponent_MechServiceStations(Map map) : base(map) { }
        public void Register(CompMechServiceStation station)
        {
            if (!stations.Contains(station)) stations.Add(station);
        }
        public void Unregister(CompMechServiceStation station) => stations.Remove(station);
        public void DelayAutomaticRetry(Pawn pawn) => retryAfter[pawn] = Find.TickManager.TicksGame + 600;

        public CompMechServiceStation? FindDeliveryStation(Pawn carrier, Pawn mech)
        {
            CompMechServiceStation? best = null;
            int bestOccupied = int.MaxValue;
            int bestDistance = int.MaxValue;
            foreach (CompMechServiceStation station in stations)
            {
                if (!MechServiceHaulUtility.CanDeliver(carrier, mech, station)) continue;
                int distance = mech.Position.DistanceToSquared(station.ServiceCell);
                int occupied = station.IsOccupied ? 1 : 0;
                if (occupied < bestOccupied || (occupied == bestOccupied && distance < bestDistance))
                { best = station; bestDistance = distance; bestOccupied = occupied; }
            }
            return best;
        }

        public CompMechServiceStation? FindStation(Pawn pawn)
        {
            if (retryAfter.TryGetValue(pawn, out int tick) && Find.TickManager.TicksGame < tick) return null;
            CompMechServiceStation? best = null;
            int bestOccupied = int.MaxValue;
            int bestDistance = int.MaxValue;
            foreach (CompMechServiceStation station in stations)
            {
                if (!station.CanRequest(pawn, false)) continue;
                int occupied = station.IsOccupied ? 1 : 0;
                int distance = pawn.Position.DistanceToSquared(station.ServiceCell);
                if (occupied < bestOccupied || occupied == bestOccupied && distance < bestDistance)
                {
                    best = station;
                    bestOccupied = occupied;
                    bestDistance = distance;
                }
            }
            return best;
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % 120 != 0) return;
            expiredRetries.Clear();
            foreach (KeyValuePair<Pawn, int> entry in retryAfter)
                if (!entry.Key.Spawned || entry.Key.Map != map || Find.TickManager.TicksGame >= entry.Value)
                    expiredRetries.Add(entry.Key);
            foreach (Pawn pawn in expiredRetries) retryAfter.Remove(pawn);
            expiredRetries.Clear();
            if (stations.Count == 0) return;
            // 只请求原版重评估 AI，不直接派发玩家命令，也不中断其他手动任务。
            candidates.Clear();
            candidates.AddRange(map.mapPawns.AllPawnsSpawned);
            foreach (Pawn pawn in candidates)
            {
                if (MechServicePolicyUtility.IsServiceJob(pawn)
                    || !MechServicePolicyUtility.CanAutoSeek(pawn)
                    || pawn.CurJob?.def.casualInterruptible == false) continue;
                if (FindStation(pawn) != null) pawn.jobs.CheckForJobOverride();
            }
            candidates.Clear();
        }
    }
}
