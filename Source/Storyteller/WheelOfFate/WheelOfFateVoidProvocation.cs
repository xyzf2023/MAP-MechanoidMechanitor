using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>复用原版虚空扰动的实体候选与延迟规则，不结算仪式主持者或发送完成信件。</summary>
    internal static class WheelOfFateVoidProvocation
    {
        internal static bool MonolithActivated => ModsConfig.AnomalyActive
            && Find.Anomaly != null && Find.Anomaly.HighestLevelReached > 0
            && Find.Storyteller?.difficulty?.AnomalyPlaystyleDef?.enableAnomalyContent == true;

        internal static void TryQueue(PsychicRitualDef_VoidProvocation ritual)
        {
            if (!MonolithActivated || Find.EntityCodex == null
                || Find.Storyteller?.incidentQueue == null || Find.TickManager == null) return;

            Map? map = Find.Anomaly.monolith?.MapHeld;
            if (map == null || !map.IsPlayerHome) map = WheelOfFateExtraIncidents.HomeMap();
            if (map == null) return;

            // 原版 ApplyOutcome 为私有方法，且要求真实仪式及主持者；这里只复刻引来实体的部分。
            // 按图鉴类别顺序，优先使用第一个存在可触发未发现实体的类别。
            var candidates = new List<IncidentDef>();
            foreach (EntityCategoryDef category in DefDatabase<EntityCategoryDef>.AllDefs
                .OrderBy(entry => entry.listOrder))
            {
                foreach (EntityCodexEntryDef entry in DefDatabase<EntityCodexEntryDef>.AllDefs)
                {
                    if (entry.category == category && !entry.Discovered) AddCandidates(entry);
                }
                if (candidates.Count > 0) break;
            }

            if (candidates.Count == 0)
            {
                foreach (EntityCodexEntryDef entry in DefDatabase<EntityCodexEntryDef>.AllDefs)
                    AddCandidates(entry);
            }

            // 保持原版候选列表的抽选方式；空池静默结束，不在后续 tick 重试。
            if (!candidates.TryRandomElement(out var incident)) return;
            IncidentParms parms = MakeParms(incident);
            int delayTicks = Mathf.RoundToInt(ritual.incidentDelayHoursRange.RandomInRange
                * GenDate.TicksPerHour);
            // 原版队列负责存档及到期执行；保留实体事件自身的正常警报。
            Find.Storyteller.incidentQueue.Add(incident, GenTicks.TicksGame + delayTicks, parms);

            IncidentParms MakeParms(IncidentDef candidate)
            {
                IncidentParms result = StorytellerUtility.DefaultParmsNow(candidate.category, map);
                result.bypassStorytellerSettings = true;
                return result;
            }

            void AddCandidates(EntityCodexEntryDef entry)
            {
                if (entry.provocationIncidents.NullOrEmpty()) return;
                foreach (IncidentDef candidate in entry.provocationIncidents)
                {
                    if (candidate?.category != null && candidate.Worker.CanFireNow(MakeParms(candidate)))
                        candidates.Add(candidate);
                }
            }
        }
    }
}
