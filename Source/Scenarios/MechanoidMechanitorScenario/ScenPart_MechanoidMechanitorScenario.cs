using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class ScenPart_MechanoidMechanitorScenario : ScenPart
    {
        private const int TooltipId = 684271;

        public override void DoEditInterface(Listing_ScenEdit listing)
        {
            Rect rightRect = listing.GetScenPartRect(this, RowHeight);

            Rect fullRowRect = new Rect(
                rightRect.x - rightRect.width,
                rightRect.y,
                rightRect.width * 2f,
                rightRect.height);

            TooltipHandler.TipRegion(
                fullRowRect,
                new TipSignal(
                    "MAP_MechanoidMechanitor.Scenario.EditorTooltip".Translate(),
                    TooltipId));
        }

        public override void PostIdeoChosen()
        {
            base.PostIdeoChosen();
            MechanoidMechanitorScenarioUtility.ClearOrdinaryStartingPawnData();
            GameComponent_MechanoidMechanitorScenarioState.EnableForCurrentGame();
        }

        public override void PreMapGenerate()
        {
            base.PreMapGenerate();
            if (!GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
            {
                return;
            }

            if (Current.Game == null)
            {
                return;
            }

            GameComponent_MechanoidMechanitorStoryState? storyState =
                Current.Game.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            if (storyState == null
                || !GameComponent_MechanoidMechanitorStoryState.HasActiveConfiguration)
            {
                return;
            }

            try
            {
                if (!storyState.InitialMechHiveRelationApplied)
                {
                    MechanoidMechanitorMechHiveRelationApplier
                        .ApplyInitialMechHiveRelation(
                            storyState,
                            MechanoidMechanitorFactionRelationNotificationMode.Deferred);
                }
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] PreMapGenerate 阶段应用机械巢初始关系失败。"
                    + "已继续执行其他剧本关系初始化。\n"
                    + ex);
            }

            try
            {
                if (!storyState.InitialOrdinaryFactionRelationsApplied)
                {
                    MechanoidMechanitorOrdinaryFactionRelationApplier
                        .ApplyInitialOrdinaryFactionRelations(
                            storyState,
                            MechanoidMechanitorFactionRelationNotificationMode.Deferred);
                }
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] PreMapGenerate 阶段应用普通派系初始关系失败。"
                    + "已阻止异常继续中断地图生成。\n"
                    + ex);
            }
        }

        public override string Summary(Scenario scen)
        {
            return def.description;
        }
    }
}
