using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class HediffCompProperties_JusticeSyncTracker : HediffCompProperties
    {
        public HediffCompProperties_JusticeSyncTracker()
        {
            compClass = typeof(HediffComp_JusticeSyncTracker);
        }
    }

    public class HediffComp_JusticeSyncTracker : HediffComp
    {
        private HashSet<string> syncedDefNames = new HashSet<string>();

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Collections.Look(ref syncedDefNames, "syncedDefNames", LookMode.Value);
            if (syncedDefNames == null)
            {
                syncedDefNames = new HashSet<string>();
            }
        }

        public override bool CompDisallowVisible()
        {
            return true;
        }

        public bool Contains(HediffDef def)
        {
            return def != null && syncedDefNames.Contains(def.defName);
        }

        public void Add(HediffDef def)
        {
            if (def != null)
            {
                syncedDefNames.Add(def.defName);
            }
        }

        public void Remove(HediffDef def)
        {
            if (def != null)
            {
                syncedDefNames.Remove(def.defName);
            }
        }

        public void RemoveByName(string defName)
        {
            if (!defName.NullOrEmpty())
            {
                syncedDefNames.Remove(defName);
            }
        }

        public HashSet<string> SyncedDefNames => syncedDefNames;
    }
}
