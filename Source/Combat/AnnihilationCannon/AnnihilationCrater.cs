using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    internal static class AnnihilationAftermathUtility
    {
        internal static void Create(Map map, IntVec3 center, float radius, int seed,
            IEnumerable<IntVec3> affectedCells)
        {
            // 只掀掉可移除的人造表层/地基；天然地貌、水面与不可移除地形保持不变。
            // 正式攻击必须传入爆炸前快照；空快照只生成弹坑，不回退到当前地图重算。
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
                    Log.ErrorOnce("[MAP] 湮灭炮移除地形失败：" + ex, 1908263103);
                }
            }

            // 贴花直径由实际爆炸半径驱动。材质直接取原版 BlastMark 与 CraterLarge。
            AnnihilationCrater crater = (AnnihilationCrater)ThingMaker.MakeThing(
                AnnihilationCannonDefOf.MAP_AnnihilationCrater);
            crater.Initialize(radius, seed);
            GenSpawn.Spawn(crater, center, map);
        }

        [DebugAction("MAP-机械族机械师", "湮灭炮余波：点击地图测试（会摧毁地板）",
            false, false, false, false, false, 0, false,
            actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.Playing)]
        private static void DebugCreateAtMouse()
        {
            Map? map = Find.CurrentMap;
            IntVec3 cell = UI.MouseCell();
            if (map == null || !cell.InBounds(map)) return;
            float radius = AnnihilationHitDebugActions.CreatePreviewSettings().outerRadius;
            var cells = DamageDefOf.Bomb.Worker.ExplosionCellsToHit(cell, map, radius).ToList();
            Create(map, cell, radius, Gen.HashCombineInt(Find.TickManager.TicksGame, cell.GetHashCode()), cells);
        }
    }

    /// <summary>
    /// 永久余波贴花。直接复用原版爆炸痕迹和大弹坑材质，只把绘制尺寸绑定到实际爆炸半径。
    /// </summary>
    public sealed class AnnihilationCrater : Thing
    {
        private float blastRadius = 1f;
        private int visualSeed;
        private bool visualFailed;

        internal void Initialize(float radius, int seed)
        {
            blastRadius = Mathf.Max(1f, radius);
            visualSeed = seed;
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (visualFailed) return;
            try { DrawCrater(); }
            catch (Exception ex)
            {
                visualFailed = true;
                Log.ErrorOnce("[MAP] 湮灭炮弹坑绘制失败：" + ex, 1908263104);
            }
        }

        private void DrawCrater()
        {
            float diameter = blastRadius * 2f;
            float angle = Mathf.Abs(visualSeed * 0.618034f) % 360f;
            Vector3 center = Position.ToVector3Shifted();

            // 原版 BlastMark 铺满爆炸直径，先画焦痕，再叠原版 CraterLarge 形成坑缘与凹陷。
            DrawVanillaDecal(ThingDefOf.Filth_BlastMark, center,
                diameter, angle, AltitudeLayer.Filth.AltitudeFor());
            DrawVanillaDecal(ThingDefOf.CraterLarge, center,
                diameter * 0.94f, angle + 137f, AltitudeLayer.FloorEmplacement.AltitudeFor() + 0.002f);
        }

        private void DrawVanillaDecal(ThingDef source, Vector3 center, float size,
            float angle, float altitude)
        {
            Material material = source.graphicData.Graphic.MatAt(Rot4.North, this);
            Vector3 location = new Vector3(center.x, altitude, center.z);
            Matrix4x4 matrix = Matrix4x4.TRS(location,
                Quaternion.AngleAxis(angle, Vector3.up), new Vector3(size, 1f, size));
            Graphics.DrawMesh(MeshPool.plane10, matrix, material, 0);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref blastRadius, "blastRadius", 1f);
            Scribe_Values.Look(ref visualSeed, "visualSeed");
        }
    }
}
