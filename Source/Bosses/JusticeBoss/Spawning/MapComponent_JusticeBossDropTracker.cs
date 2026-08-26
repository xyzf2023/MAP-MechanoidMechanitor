using System;
using System.Collections.Generic;
using System.Diagnostics;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor
{
    public enum JusticeBossDropRole
    {
        Assault = 0,
        Guard = 1,
    }

    public sealed class PendingJusticeBossDrop : IExposable
    {
        public Pawn? pawn;

        public int justiceEventId;

        public JusticeBossDropRole role;

        public Faction? faction;

        public IntVec3 anchorCell = IntVec3.Invalid;

        public int registeredTick;

        // 落地阶段是否补充机动作战由待落地记录决定（来自本场正义战斗快照）。
        // 默认 true：保证旧存档中已存在的待落地空投继续旧版行为。
        // 关闭正义机动作战开关的记录不再补加，但不会移除单位通过其他来源已获得的机动作战。
        public bool applyMobileCombat = true;

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref justiceEventId, "justiceEventId", 0);
            Scribe_Values.Look(ref role, "role", JusticeBossDropRole.Assault);
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref anchorCell, "anchorCell");
            Scribe_Values.Look(ref registeredTick, "registeredTick", 0);
            Scribe_Values.Look(ref applyMobileCombat, "applyMobileCombat", true);
        }
    }

    public sealed class MapComponent_JusticeBossDropTracker : MapComponent
    {
        private List<PendingJusticeBossDrop> pending =
            new List<PendingJusticeBossDrop>();

        private int landedAndAssignedCount;

        public int PendingCount => pending.Count;

        public int LandedAndAssignedCount => landedAndAssignedCount;

        public MapComponent_JusticeBossDropTracker(Map map)
            : base(map)
        {
        }

        public static MapComponent_JusticeBossDropTracker For(Map map)
        {
            MapComponent_JusticeBossDropTracker? existing =
                map.GetComponent<MapComponent_JusticeBossDropTracker>();
            if (existing != null)
            {
                return existing;
            }

            MapComponent_JusticeBossDropTracker created =
                new MapComponent_JusticeBossDropTracker(map);
            map.components.Add(created);
            return created;
        }

        public void Register(PendingJusticeBossDrop drop)
        {
            if (drop?.pawn == null)
            {
                return;
            }

            pending.Add(drop);
        }

        public void Unregister(Pawn pawn)
        {
            pending.RemoveAll(p => p.pawn == pawn);
        }

        public override void MapComponentTick()
        {
            if (pending.Count == 0 || !map.IsHashIntervalTick(10))
            {
                return;
            }

            bool diagnostics = JusticeBossDiagnosticUtility.Enabled;
            int pendingBefore = pending.Count;
            int landedBefore = landedAndAssignedCount;
            long startedTimestamp =
                diagnostics ? Stopwatch.GetTimestamp() : 0L;
            Dictionary<string, int>? kindCounts =
                diagnostics ? new Dictionary<string, int>() : null;
            HashSet<int>? eventIds =
                diagnostics ? new HashSet<int>() : null;
            Exception? exception = null;

            try
            {
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    PendingJusticeBossDrop entry = pending[i];
                    Pawn? pawn = entry.pawn;
                    if (pawn == null || pawn.Destroyed || pawn.Dead)
                    {
                        pending.RemoveAt(i);
                        continue;
                    }

                    if (IsInTransit(pawn))
                    {
                        continue;
                    }

                    if (pawn.Spawned && pawn.Map == map)
                    {
                        CompleteLanding(entry);
                        pending.RemoveAt(i);

                        if (diagnostics)
                        {
                            string key =
                                (pawn.kindDef?.defName ?? "null")
                                + "/"
                                + entry.role;
                            kindCounts!.TryGetValue(key, out int current);
                            kindCounts[key] = current + 1;
                            eventIds!.Add(entry.justiceEventId);
                        }

                        continue;
                    }

                    bool inWorld = Find.WorldPawns.Contains(pawn);
                    bool held = pawn.ParentHolder is IThingHolder;
                    if (!inWorld && !held && pawn.MapHeld == null)
                    {
                        Log.WarningOnce(
                            "[MAP JusticeBoss] Pending drop pawn lost before landing: "
                                + pawn.LabelShort,
                            pawn.thingIDNumber ^ 0x4A05);
                        pending.RemoveAt(i);
                    }
                }
            }
            catch (Exception caught)
            {
                exception = caught;
                throw;
            }
            finally
            {
                if (diagnostics)
                {
                    int landedCount =
                        landedAndAssignedCount - landedBefore;
                    int pendingAfter = pending.Count;
                    int eventCount = eventIds?.Count ?? 0;
                    string kindCountsText =
                        JusticeBossTraceFormatting.DescribeKindCounts(
                            kindCounts);
                    double elapsedMs =
                        JusticeBossTraceFormatting.ElapsedMilliseconds(
                            startedTimestamp);

                    JusticeBossDiagnosticUtility.WriteLandingSummary(
                        map.uniqueID,
                        pendingBefore,
                        pendingAfter,
                        landedCount,
                        eventCount,
                        kindCountsText,
                        elapsedMs,
                        exception);

                    JusticeBossLaunchTracePatchManager.WriteLandingBatchSummary(
                        map.uniqueID,
                        pendingBefore,
                        pendingAfter,
                        landedCount,
                        eventCount,
                        kindCountsText,
                        elapsedMs,
                        exception);
                }
            }
        }

        private static bool IsInTransit(Pawn pawn)
        {
            IThingHolder? holder = pawn.ParentHolder;
            while (holder != null)
            {
                if (holder is ActiveTransporter
                    || holder is ActiveTransporterInfo
                    || holder is Skyfaller)
                {
                    return true;
                }

                holder = holder.ParentHolder;
            }

            return false;
        }

        private void CompleteLanding(PendingJusticeBossDrop entry)
        {
            Pawn pawn = entry.pawn!;
            Faction? faction =
                entry.faction
                ?? pawn.Faction
                ?? Faction.OfMechanoids;
            if (faction != null && pawn.Faction != faction)
            {
                pawn.SetFaction(faction);
            }

            // 落地阶段是否补充机动作战由待落地记录决定：关闭正义机动作战开关的记录不再补加，
            // 但不会移除该 Pawn 通过其他来源已经获得的机动作战。
            if (entry.applyMobileCombat)
            {
                MechanoidMechanitorWorkModeUtility.EnsureMobileCombatHediff(pawn);
            }

            Lord lord = entry.role == JusticeBossDropRole.Guard
                ? JusticeBossLordUtility.EnsureGuardLord(
                    map,
                    faction,
                    entry.justiceEventId,
                    entry.anchorCell)
                : JusticeBossLordUtility.EnsureAssaultLord(
                    map,
                    faction,
                    entry.justiceEventId);

            if (!lord.ownedPawns.Contains(pawn))
            {
                lord.AddPawn(pawn);
            }

            landedAndAssignedCount++;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(
                ref pending,
                "pendingJusticeBossDrops",
                LookMode.Deep);
            Scribe_Values.Look(
                ref landedAndAssignedCount,
                "landedAndAssignedCount",
                0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pending ??= new List<PendingJusticeBossDrop>();
                pending.RemoveAll(p => p == null || p.pawn == null);
            }
        }
    }
}
