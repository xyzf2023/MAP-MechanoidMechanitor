using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    internal static class WheelOfFateRaidFactionSelection
    {
        /// <summary>
        /// 在本叙事者主池完成事件抽选后，为尚未指定派系的原版 RaidEnemy 选择派系。
        /// 不安装全局补丁；自定义 Worker、任务与已有派系参数仍由原路径处理。
        /// </summary>
        internal static bool TryApply(IncidentDef incident, IncidentParms parms, StoryThemeDef? theme)
        {
            if (theme == null || (theme.raidFactionWeightFactors.NullOrEmpty()
                    && theme.EffectivePermanentEnemyRaidFactionFactor == 1f)
                || incident != IncidentDefOf.RaidEnemy || parms.faction != null
                || parms.quest != null || parms.forced || !(parms.target is Map)
                || !StorytellerCompProperties_WheelOfFateRandomMain.IsFinitePositive(parms.points)
                || incident.Worker.GetType() != typeof(IncidentWorker_RaidEnemy))
                return true;

            var worker = (IncidentWorker_RaidEnemy)incident.Worker;
            List<Faction> candidates = Candidates(worker, parms);
            if (!candidates.TryRandomElementByWeight(Weight, out var selected)) return false;
            parms.faction = selected;
            return true;

            float Weight(Faction faction)
            {
                float weight = BaseWeight(faction, parms) * theme.RaidFactionFactor(faction.def);
                return StorytellerCompProperties_WheelOfFateRandomMain.IsFiniteNonNegative(weight)
                    ? weight : 0f;
            }
        }

        /// <summary>主池与主题连波共用原版敌对派系资格；只在正常候选为空时放宽最早袭击天数。</summary>
        internal static List<Faction> Candidates(IncidentWorker_RaidEnemy worker, IncidentParms parms)
        {
            // 不让资格检查中暂时填写的 faction 泄漏进正式事件参数。
            IncidentParms validationParms = parms.ShallowCopy();
            List<Faction> candidates = Collect(false);
            return candidates.Count > 0 ? candidates : Collect(true);

            List<Faction> Collect(bool desperate)
            {
                // UsableFactions 是 private，保留其筛选条件并复用公开资格入口。
                return Find.FactionManager.AllFactions.Where(faction =>
                    !faction.temporary
                    && faction.def.pawnGroupMakers != null
                    && faction.def.pawnGroupMakers.Any(maker => maker.kindDef == PawnGroupKindDefOf.Combat)
                    && !faction.def.raidsForbidden
                    && worker.FactionCanBeGroupSource(faction, validationParms, desperate)
                    && parms.points >= faction.def.MinPointsToGeneratePawnGroup(PawnGroupKindDefOf.Combat)
                    && StorytellerCompProperties_WheelOfFateRandomMain.IsFinitePositive(BaseWeight(faction, parms)))
                    .ToList();
            }
        }

        internal static float BaseWeight(Faction faction, IncidentParms parms)
        {
            float repeatFactor = parms.target.StoryState.lastRaidFaction == faction ? 0.4f : 1f;
            return faction.def.RaidCommonalityFromPoints(parms.points) * repeatFactor;
        }
    }
}
