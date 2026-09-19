using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class SunBossSiteUtility
    {
        // 创建入口同时绑定专用 WorldObjectDef 与 SitePart，确保使用受控地图生成链。
        // 后续任务可以复用此入口；当前不加入自然事件、任务或 BOSS 战斗行为。
        public static Site? CreateSite(PlanetTile tile, float threatPoints = 1800f)
        {
            if (!ModsConfig.OdysseyActive || !tile.Valid || tile.Layer.Def.isSpace ||
                Find.World.Impassable(tile) || Find.WorldGrid[tile].WaterCovered ||
                Find.WorldObjects.AnyMapParentAt(tile) || Faction.OfMechanoids == null)
                return null;

            SitePartDef part = DefDatabase<SitePartDef>.GetNamed("MAP_SunBossFacility");
            WorldObjectDef worldObject = DefDatabase<WorldObjectDef>.GetNamed("MAP_SunBossFacility");
            Site site = SiteMaker.MakeSite(part, tile, Faction.OfMechanoids,
                threatPoints: threatPoints, worldObjectDef: worldObject);
            Find.WorldObjects.Add(site);
            return site;
        }

        [DebugAction("MAP-机械族机械师", "太阳据点：在选中世界地块创建", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void CreateAtSelectedTile()
        {
            if (!ModsConfig.OdysseyActive)
            {
                Messages.Message("太阳据点需要启用奥德赛。", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            Site? site = CreateSite(Find.WorldSelector.SelectedTile);
            if (site == null)
            {
                Messages.Message("请在世界地图选择可通行、无其他据点的陆地地块。", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            Find.WorldSelector.Select(site);
            Messages.Message("已创建太阳据点。进入地点后生成设施地图。", MessageTypeDefOf.TaskCompletion, historical: false);
        }
    }
}
