using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 活动合体记录变化时主动清除相关 Stat 缓存，避免旧的缓存值继续生效。
    /// 只清除与本次合体快照相关的 Stat，不进行全量扫描。
    /// </summary>
    internal static class MechFusionStatCacheUtility
    {
        internal static void Invalidate(MechFusionSession? session)
        {
            if (session == null)
            {
                return;
            }

            HashSet<StatDef> stats = new HashSet<StatDef>
            {
                StatDefOf.MoveSpeed,
                StatDefOf.Mass,
                StatDefOf.ArmorRating_Sharp,
                StatDefOf.ArmorRating_Blunt,
                StatDefOf.ArmorRating_Heat,
                StatDefOf.AimingDelayFactor,
                StatDefOf.Insulation_Cold,
                StatDefOf.Insulation_Heat,
                StatDefOf.ToxicEnvironmentResistance
            };

            StatDef? vacuumResistance =
                DefDatabase<StatDef>.GetNamedSilentFail("VacuumResistance");
            if (vacuumResistance != null)
            {
                stats.Add(vacuumResistance);
            }

            session.CollectAffectedStats(stats);

            Thing? wearer = session.WearerPawn;
            Thing? apparel = session.FusionApparel;
            foreach (StatDef stat in stats)
            {
                StatWorker? worker = stat?.Worker;
                if (worker == null)
                {
                    continue;
                }

                if (wearer != null && !wearer.Destroyed)
                {
                    worker.ClearCacheForThing(wearer);
                }

                if (apparel != null && !apparel.Destroyed)
                {
                    worker.ClearCacheForThing(apparel);
                }
            }
        }
    }
}
