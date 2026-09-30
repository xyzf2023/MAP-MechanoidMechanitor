using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    internal static class AnnihilationAftermathUtility
    {
        internal static void Create(Map map, IntVec3 center, IEnumerable<IntVec3> affectedCells)
        {
            RemoveFloorsAndFoundations(map, affectedCells);
            TrySpawnCrater(map, center);
        }

        private static void RemoveFloorsAndFoundations(Map map, IEnumerable<IntVec3> affectedCells)
        {
            // 正式攻击只使用爆炸前快照；空快照不回退到当前地图重算。
            foreach (IntVec3 cell in affectedCells)
            {
                if (!cell.InBounds(map)) continue;
                try
                {
                    if (map.terrainGrid.CanRemoveTopLayerAt(cell))
                        map.terrainGrid.RemoveTopLayer(cell, false);
                    if (map.terrainGrid.CanRemoveFoundationAt(cell))
                        map.terrainGrid.RemoveFoundation(cell, false);
                }
                catch (Exception ex)
                {
                    // 单格地形回调异常不阻止其余格子和落点生命周期继续推进。
                    Log.ErrorOnce("[MAP-机械族机械师] 湮灭炮移除地形失败：" + ex, 1908263103);
                }
            }
        }

        private static void TrySpawnCrater(Map map, IntVec3 center)
        {
            // 双方太阳共用此入口；只控制新坑生成，外圈地形破坏已独立完成。
            if (MAPMechanitorMod.Settings?.enableAnnihilationCannonCrater != true) return;

            ThingDef craterDef = AnnihilationCannonDefOf.MAP_AnnihilationImpactCrater;
            CellRect occupiedRect = GenAdj.OccupiedRect(center, Rot4.North, craterDef.size);
            // 原版地形检查会裁切范围，因此必须先检查完整占地是否越界。
            if (!occupiedRect.InBounds(map)
                || !GenConstruct.CanBuildOnTerrain(craterDef, center, map, Rot4.North)) return;

            foreach (IntVec3 cell in occupiedRect)
            {
                foreach (Thing thing in cell.GetThingList(map))
                {
                    // 完整占地内存在任何建筑（包括白名单保留建筑）就不生成，不移动落点。
                    // 矩形坑角落可能超出圆形内圈，不得因生成弹坑扩大攻击破坏。
                    if (thing is Building || thing is Blueprint || thing is Frame) return;
                    if ((thing.def.category != ThingCategory.Filth || AnnihilationWhitelistUtility.IsProtected(thing))
                        && GenSpawn.SpawningWipes(craterDef, thing.def)) return;
                }
            }

            // 所有检查均通过后才创建；失败时不移动落点、不回滚已完成的地形破坏。
            GenSpawn.Spawn(craterDef, center, map, Rot4.North, WipeMode.Vanish);
        }
    }

    /// <summary>内圈实体弹坑；绘制、填平和存档直接沿用原版，固定尺寸由 Def 定义。</summary>
    public sealed class AnnihilationImpactCrater : Crater
    {
    }
}
