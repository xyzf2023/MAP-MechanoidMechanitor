using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class SunBossSiteUtility
    {
        // 创建入口同时绑定专用 WorldObjectDef 与 SitePart，确保使用受控地图生成链。
        // 自然调度与开发者工具共用此入口。
        public static Site? CreateSite(PlanetTile tile, float threatPoints = 1800f)
        {
            return CreateSite(tile, out _, threatPoints);
        }

        internal static Site? TryCreateNaturalSite()
        {
            if (!ModsConfig.OdysseyActive || Faction.OfMechanoids == null) return null;

            // 沿用原版据点选址距离与可达性检查，限制为未被占用的可通行陆地。
            if (!TileFinder.TryFindNewSiteTile(out PlanetTile tile, canBeSpace: false,
                validator: candidate => candidate.Valid && !candidate.Layer.Def.isSpace
                    && !Find.World.Impassable(candidate) && !Find.WorldGrid[candidate].WaterCovered
                    && !Find.WorldObjects.AnyMapParentAt(candidate))) return null;

            return CreateSite(tile);
        }

        private static Site? CreateSite(PlanetTile tile, out string failureReason, float threatPoints = 1800f)
        {
            failureReason = string.Empty;
            if (!ModsConfig.OdysseyActive)
                failureReason = "需要启用奥德赛。";
            else if (!tile.Valid)
                failureReason = "未点击有效的世界地块。";
            else if (tile.Layer.Def.isSpace)
                failureReason = "目标位于太空层，需要选择陆地地块。";
            else if (Find.World.Impassable(tile))
                failureReason = "目标地块不可通行。";
            else if (Find.WorldGrid[tile].WaterCovered)
                failureReason = "目标地块被水覆盖，需要选择陆地地块。";
            else if (Find.WorldObjects.AnyMapParentAt(tile))
                failureReason = "目标地块已有据点或其他承载地图的世界对象。";
            else if (Faction.OfMechanoids == null)
                failureReason = "当前世界不存在机械族派系。";

            if (!string.IsNullOrEmpty(failureReason))
                return null;

            SitePartDef part = DefDatabase<SitePartDef>.GetNamed("MAP_SunBossFacility");
            WorldObjectDef worldObject = DefDatabase<WorldObjectDef>.GetNamed("MAP_SunBossFacility");
            Site site = SiteMaker.MakeSite(part, tile, Faction.OfMechanoids,
                threatPoints: threatPoints, worldObjectDef: worldObject);
            Find.WorldObjects.Add(site);
            return site;
        }

        [DebugAction("MAP-机械族机械师", "太阳据点：在指定地块创建", false, false, false, false, false, 0, false,
            actionType = DebugActionType.ToolWorld, allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        private static void CreateAtClickedTile()
        {
            PlanetTile tile = GenWorld.MouseTile();
            Site? site = CreateSite(tile, out string failureReason);
            if (site == null)
            {
                Log.Warning($"[MAP-机械族机械师] 太阳 BOSS： 太阳据点创建失败，地块={tile}：{failureReason}");
                return;
            }
            Find.WorldSelector.Select(site);
            Log.Message($"[MAP-机械族机械师] 太阳 BOSS： 已创建太阳据点，地块={tile}。进入地点后生成设施地图。");
        }
    }
}
