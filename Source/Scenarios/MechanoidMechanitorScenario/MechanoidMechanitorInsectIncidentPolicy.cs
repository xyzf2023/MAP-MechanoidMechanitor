using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class MechanoidMechanitorInsectIncidentPolicy
    {
        private const string DeepDrillInfestationDefName =
            "DeepDrillInfestation";

        private const string WastepackInfestationDefName =
            "WastepackInfestation";

        public static bool ShouldBlock(IncidentDef? incidentDef)
        {
            // 最廉价的判断放最前面。
            // IncidentWorker.CanFireNow 被大量事件调用，
            // 非虫灾事件不要继续访问 StoryState。
            if (!IsBlockedInsectAttackIncident(incidentDef))
            {
                return false;
            }

            return IsAlliedInsectAttackBlockingActive();
        }

        /// <summary>
        /// 标准任务型虫灾（QuestNode_Infestation / QuestPart_Infestation）是否应被拦截。
        /// 与 Incident 判定共用同一套开关，避免设置逻辑分叉。
        /// </summary>
        public static bool ShouldBlockQuestInfestation()
        {
            return IsAlliedInsectAttackBlockingActive();
        }

        /// <summary>
        /// 统一的“盟友虫灾拦截是否启用”判定。
        /// 只有同时存在活动配置、当前虫巢关系为 Ally、且全局设置开启时才返回 true。
        /// </summary>
        public static bool IsAlliedInsectAttackBlockingActive()
        {
            MAPMechanitorModSettings? settings =
                MAPMechanitorMod.Settings;

            if (settings == null
                || !settings.blockInfestationIncidentsWhenInsectsAllied)
            {
                return false;
            }

            if (!GameComponent_MechanoidMechanitorStoryState
                .HasActiveConfiguration
                || Current.Game == null)
            {
                return false;
            }

            GameComponent_MechanoidMechanitorStoryState? storyState =
                Current.Game.GetComponent<
                    GameComponent_MechanoidMechanitorStoryState>();

            if (storyState == null
                || !storyState.TryGetInsectRelationMode(
                    out MechanoidMechanitorInsectRelationMode mode))
            {
                return false;
            }

            return mode == MechanoidMechanitorInsectRelationMode.Ally;
        }

        private static bool IsBlockedInsectAttackIncident(
            IncidentDef? incidentDef)
        {
            if (incidentDef == null)
            {
                return false;
            }

            if (incidentDef == IncidentDefOf.Infestation)
            {
                return true;
            }

            string defName = incidentDef.defName;

            if (defName == DeepDrillInfestationDefName)
            {
                return true;
            }

            if (defName == WastepackInfestationDefName)
            {
                return true;
            }

            return false;
        }
    }
}
