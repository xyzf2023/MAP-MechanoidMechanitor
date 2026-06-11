using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompProperties_JusticeHediffSync : CompProperties
    {
        public CompProperties_JusticeHediffSync()
        {
            compClass = typeof(CompJusticeHediffSync);
        }
    }

    public class CompJusticeHediffSync : ThingComp
    {
        private const int SyncIntervalTicks = 250;

        public override void PostPostMake()
        {
            base.PostPostMake();
            TrySync();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            TrySync();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                TrySync();
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.IsHashIntervalTick(SyncIntervalTicks))
            {
                return;
            }
            TrySync();
        }

        private void TrySync()
        {
            if (parent is not Pawn commander)
            {
                return;
            }

            if (!MAPMechanitorNodeUtility.IsMechanitorNodeController(commander))
            {
                return;
            }

            Pawn_MechanitorTracker mechanitor = commander.mechanitor;
            if (mechanitor == null)
            {
                return;
            }

            List<Pawn> controlled = mechanitor.ControlledPawns;
            if (controlled.NullOrEmpty())
            {
                return;
            }

            Dictionary<HediffDef, float> source = CollectSyncableHediffs(commander);
            for (int i = 0; i < controlled.Count; i++)
            {
                Pawn mech = controlled[i];
                if (mech == null || mech.Dead || mech.health?.hediffSet == null)
                {
                    continue;
                }

                SyncToMech(mech, source);
            }
        }

        private static Dictionary<HediffDef, float> CollectSyncableHediffs(Pawn commander)
        {
            Dictionary<HediffDef, float> result = new Dictionary<HediffDef, float>();
            if (commander.health?.hediffSet == null)
            {
                return result;
            }

            List<Hediff> hediffs = commander.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];
                if (!IsSyncablePositiveHediff(hediff))
                {
                    continue;
                }

                HediffDef def = hediff.def;
                if (!result.TryGetValue(def, out float severity) || hediff.Severity > severity)
                {
                    result[def] = hediff.Severity;
                }
            }

            return result;
        }

        private static bool IsSyncablePositiveHediff(Hediff hediff)
        {
            if (hediff?.def == null)
            {
                return false;
            }

            HediffDef def = hediff.def;
            if (!JusticeHediffSyncUtility.IsSyncablePositiveHediffDef(def))
            {
                return false;
            }

            if (JusticeHediffSyncUtility.IsBlacklisted(def))
            {
                return false;
            }

            if (hediff.Part != null)
            {
                return false;
            }

            return true;
        }

        private static void SyncToMech(Pawn mech, Dictionary<HediffDef, float> source)
        {
            HashSet<HediffDef> sourceDefs = new HashSet<HediffDef>(source.Keys);
            HediffComp_JusticeSyncTracker? trackerComp = GetOrCreateTrackerComp(mech);
            if (trackerComp == null)
            {
                return;
            }

            for (int i = mech.health.hediffSet.hediffs.Count - 1; i >= 0; i--)
            {
                Hediff existing = mech.health.hediffSet.hediffs[i];
                if (!IsSyncablePositiveHediff(existing))
                {
                    continue;
                }

                if (trackerComp.Contains(existing.def) && !sourceDefs.Contains(existing.def))
                {
                    mech.health.RemoveHediff(existing);
                    trackerComp.Remove(existing.def);
                }
            }

            foreach (KeyValuePair<HediffDef, float> kvp in source)
            {
                HediffDef def = kvp.Key;
                float severity = kvp.Value;

                Hediff existing = mech.health.hediffSet.GetFirstHediffOfDef(def);
                if (existing == null)
                {
                    Hediff hediff = HediffMaker.MakeHediff(def, mech);
                    hediff.Severity = severity;
                    mech.health.AddHediff(hediff);
                    trackerComp.Add(def);
                }
                else
                {
                    if (trackerComp.Contains(def))
                    {
                        existing.Severity = severity;
                    }
                }
            }

            CleanupTracker(trackerComp, mech);
        }

        private static HediffComp_JusticeSyncTracker? GetOrCreateTrackerComp(Pawn mech)
        {
            HediffDef trackerDef = DefDatabase<HediffDef>.GetNamedSilentFail("MAP_Justice_SyncTracker");
            if (trackerDef == null)
            {
                return null;
            }

            Hediff trackerHediff = mech.health.hediffSet.GetFirstHediffOfDef(trackerDef);
            if (trackerHediff == null)
            {
                trackerHediff = HediffMaker.MakeHediff(trackerDef, mech);
                mech.health.AddHediff(trackerHediff);
            }

            return (trackerHediff as HediffWithComps)?.TryGetComp<HediffComp_JusticeSyncTracker>();
        }

        private static void CleanupTracker(HediffComp_JusticeSyncTracker trackerComp, Pawn mech)
        {
            if (trackerComp == null)
            {
                return;
            }

            HashSet<string> names = trackerComp.SyncedDefNames;
            if (names == null || names.Count == 0)
            {
                return;
            }

            List<string>? toRemove = null;
            foreach (string defName in names)
            {
                HediffDef? def = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
                if (def == null)
                {
                    (toRemove ??= new List<string>()).Add(defName);
                    continue;
                }

                Hediff existing = mech.health.hediffSet.GetFirstHediffOfDef(def);
                if (existing == null)
                {
                    (toRemove ??= new List<string>()).Add(defName);
                }
            }

            if (toRemove == null)
            {
                return;
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                trackerComp.RemoveByName(toRemove[i]);
            }
        }
    }
}
