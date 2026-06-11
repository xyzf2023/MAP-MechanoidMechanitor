using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class JusticeHediffSyncUtility
    {
        public static bool IsSyncablePositiveHediffDef(HediffDef def)
        {
            if (def == null)
            {
                return false;
            }

            if (def.isBad)
            {
                return false;
            }

            if (def.addedPartProps != null || def.injuryProps != null)
            {
                return false;
            }

            if (WorkModeUtility.IsJusticeWorkMode(def))
            {
                return false;
            }

            if (IsSyncTrackerDef(def))
            {
                return false;
            }

            return true;
        }

        public static bool IsBlacklisted(HediffDef def)
        {
            return IsDefaultBlacklisted(def);
        }

        public static bool IsDefaultBlacklisted(HediffDef def)
        {
            return def != null && GetDefaultBlacklistDefNames().Contains(def.defName);
        }

        public static bool IsSyncTrackerDef(HediffDef def)
        {
            return def != null && def.defName == "MAP_Justice_SyncTracker";
        }

        private static HashSet<string> GetDefaultBlacklistDefNames()
        {
            HashSet<string> names = new HashSet<string>();
            List<JusticeHediffBlacklistDef> defs = DefDatabase<JusticeHediffBlacklistDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                List<HediffDef> hediffs = defs[i].hediffs;
                if (hediffs == null)
                {
                    continue;
                }
                for (int j = 0; j < hediffs.Count; j++)
                {
                    HediffDef def = hediffs[j];
                    if (def != null)
                    {
                        names.Add(def.defName);
                    }
                }
            }
            return names;
        }

        public static List<HediffDef> GetCandidateHediffDefsSorted()
        {
            return DefDatabase<HediffDef>.AllDefsListForReading
                .Where(IsSyncablePositiveHediffDef)
                .OrderBy(d => d.label ?? d.defName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
