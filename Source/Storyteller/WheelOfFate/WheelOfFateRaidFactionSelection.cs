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
                    && theme.permanentEnemyRaidFactionFactor == 1f)
                || incident != IncidentDefOf.RaidEnemy || parms.faction != null
                || parms.quest != null || parms.forced || !(parms.target is Map)
                || !StorytellerCompProperties_WheelOfFateRandomMain.IsFinitePositive(parms.points)
                || incident.Worker.GetType() != typeof(IncidentWorker_RaidEnemy))
                return true;

            var worker = (IncidentWorker_RaidEnemy)incident.Worker;
            float points = parms.points;
            // 不让派系资格检查中暂时填写的 faction 泄漏进正式事件参数。
            IncidentParms validationParms = parms.ShallowCopy();
            List<Faction> candidates = Candidates(false);
            // 只有原版正常候选本身为空时才使用其 desperate 回退，
            // 不因主题将正常候选权重设为 0 而放宽资格。
            if (candidates.Count == 0) candidates = Candidates(true);
            if (!candidates.TryRandomElementByWeight(Weight, out var selected)) return false;
            parms.faction = selected;
            return true;

            List<Faction> Candidates(bool desperate)
            {
                // 原版 PawnGroupMakerUtility.UsableFactions 是 private；这里保留其
                // RaidEnemy 调用的筛选条件，并复用公开的 FactionCanBeGroupSource。
                return Find.FactionManager.AllFactions.Where(faction =>
                    !faction.temporary
                    && faction.def.pawnGroupMakers != null
                    && faction.def.pawnGroupMakers.Any(maker => maker.kindDef == PawnGroupKindDefOf.Combat)
                    && !faction.def.raidsForbidden
                    && worker.FactionCanBeGroupSource(faction, validationParms, desperate)
                    && points >= faction.def.MinPointsToGeneratePawnGroup(PawnGroupKindDefOf.Combat)
                    && StorytellerCompProperties_WheelOfFateRandomMain.IsFinitePositive(BaseWeight(faction)))
                    .ToList();
            }

            float BaseWeight(Faction faction)
            {
                float repeatFactor = parms.target.StoryState.lastRaidFaction == faction ? 0.4f : 1f;
                return faction.def.RaidCommonalityFromPoints(points) * repeatFactor;
            }

            float Weight(Faction faction)
            {
                float weight = BaseWeight(faction) * theme.RaidFactionFactor(faction.def);
                return StorytellerCompProperties_WheelOfFateRandomMain.IsFiniteNonNegative(weight)
                    ? weight : 0f;
            }
        }
    }
}
