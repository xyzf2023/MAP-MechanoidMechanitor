using System.Collections.Generic;
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

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref justiceEventId, "justiceEventId", 0);
            Scribe_Values.Look(ref role, "role", JusticeBossDropRole.Assault);
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref anchorCell, "anchorCell");
            Scribe_Values.Look(ref registeredTick, "registeredTick", 0);
        }
    }

    public sealed class MapComponent_JusticeBossDropTracker : MapComponent
    {
        private List<PendingJusticeBossDrop> pending = new List<PendingJusticeBossDrop>();

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

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                PendingJusticeBossDrop entry = pending[i];
                Pawn? pawn = entry.pawn;
                if (pawn == null || pawn.Destroyed || pawn.Dead)
                {
                    if (JusticeBossDiagnosticUtility.Enabled && pawn != null)
                    {
                        JusticeBossDiagnosticUtility.ForgetPawn(pawn);
                        JusticeBossDiagnosticUtility.Write(
                            "Tracker.RemovedInvalid",
                            "pawnId=" + pawn.thingIDNumber
                                + " destroyed=" + pawn.Destroyed
                                + " dead=" + pawn.Dead);
                    }

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
                    continue;
                }

                bool inWorld = Find.WorldPawns.Contains(pawn);
                bool held = pawn.ParentHolder is IThingHolder;
                if (!inWorld && !held && pawn.MapHeld == null)
                {
                    if (JusticeBossDiagnosticUtility.Enabled)
                    {
                        JusticeBossDiagnosticUtility.ForgetPawn(pawn);
                        JusticeBossDiagnosticUtility.Write(
                            "Tracker.Lost",
                            "pawnId=" + pawn.thingIDNumber
                                + " pawn="
                                + JusticeBossDiagnosticUtility.Sanitize(
                                    pawn.LabelShort));
                    }

                    Log.WarningOnce(
                        "[MAP JusticeBoss] Pending drop pawn lost before landing: "
                        + pawn.LabelShort,
                        pawn.thingIDNumber ^ 0x4A05);
                    pending.RemoveAt(i);
                }
            }
        }

        private static bool IsInTransit(Pawn pawn)
        {
            IThingHolder? holder = pawn.ParentHolder;
            while (holder != null)
            {
                if (holder is ActiveTransporter || holder is ActiveTransporterInfo)
                {
                    return true;
                }

                if (holder is Skyfaller)
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
            Faction? faction = entry.faction ?? pawn.Faction ?? Faction.OfMechanoids;
            if (faction != null && pawn.Faction != faction)
            {
                pawn.SetFaction(faction);
            }

            MechanoidMechanitorWorkModeUtility.EnsureMobileCombatHediff(pawn);

            Lord lord = entry.role == JusticeBossDropRole.Guard
                ? JusticeBossLordUtility.EnsureGuardLord(
                    map,
                    faction,
                    entry.justiceEventId,
                    entry.anchorCell)
                : JusticeBossLordUtility.EnsureAssaultLord(map, faction, entry.justiceEventId);

            if (!lord.ownedPawns.Contains(pawn))
            {
                // Lord.AddPawn already invokes the current LordToil's duty refresh.
                // Do not end the pawn's job or refresh all duties a second time here.
                lord.AddPawn(pawn);
            }

            landedAndAssignedCount++;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref pending, "pendingJusticeBossDrops", LookMode.Deep);
            Scribe_Values.Look(ref landedAndAssignedCount, "landedAndAssignedCount", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pending ??= new List<PendingJusticeBossDrop>();
                pending.RemoveAll(p => p == null || p.pawn == null);
            }
        }
    }
}
