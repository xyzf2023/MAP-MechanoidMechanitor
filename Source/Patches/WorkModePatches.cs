using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    // 机动作战：忽略地形导致的移动减速
    [HarmonyPatch(typeof(Pawn_PathFollower), "CostToMoveIntoCell", new[] { typeof(Pawn), typeof(IntVec3) })]
    public static class Patch_Pawn_PathFollower_CostToMoveIntoCell_WorkMode
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, IntVec3 c, ref float __result)
        {
            if (!WorkModeUtility.HasMobileCombatFlag(pawn))
            {
                return;
            }

            float? cost = ComputeCostIgnoringTerrain(pawn, c);
            if (cost.HasValue)
            {
                __result = cost.Value;
            }
        }

        private static float? ComputeCostIgnoringTerrain(Pawn pawn, IntVec3 c)
        {
            return ComputeCostIgnoringTerrainRaw(pawn, c);
        }

        private static float? ComputeCostIgnoringTerrainRaw(Pawn pawn, IntVec3 c)
        {
            Map map = pawn.Map;
            if (map == null)
            {
                return null;
            }

            float num = (c.x == pawn.Position.x || c.z == pawn.Position.z)
                ? pawn.TicksPerMoveCardinal
                : pawn.TicksPerMoveDiagonal;

            int? baseCostOverride = Pawn_PathFollower.GetPawnCellBaseCostOverride(pawn, c);
            int pathCost = CalculatedCostAt_NoTerrain(map, pawn, c, pawn.Position, baseCostOverride);
            if (pathCost >= 10000)
            {
                return null;
            }

            num += pathCost;

            Building edifice = c.GetEdifice(map);
            if (edifice != null)
            {
                num += edifice.PathWalkCostFor(pawn);
            }

            if (num > 450f)
            {
                num = 450f;
            }

            if (pawn.CurJob != null)
            {
                Pawn locomotionUrgencySameAs = pawn.jobs.curDriver.locomotionUrgencySameAs;
                if (locomotionUrgencySameAs != null && locomotionUrgencySameAs != pawn && locomotionUrgencySameAs.Spawned)
                {
                    float? num2 = ComputeCostIgnoringTerrainRaw(locomotionUrgencySameAs, c);
                    if (num2.HasValue && num < num2.Value)
                    {
                        num = num2.Value;
                    }
                }
                else
                {
                    switch (pawn.jobs.curJob.locomotionUrgency)
                    {
                        case LocomotionUrgency.Amble:
                            num *= 3f;
                            if (num < 60f)
                            {
                                num = 60f;
                            }
                            break;
                        case LocomotionUrgency.Walk:
                            num *= 2f;
                            if (num < 50f)
                            {
                                num = 50f;
                            }
                            break;
                        case LocomotionUrgency.Jog:
                            num *= 1f;
                            break;
                        case LocomotionUrgency.Sprint:
                            num = Mathf.RoundToInt(num * 0.75f);
                            break;
                    }
                }
            }

            return Mathf.Max(num, 1f);
        }

        private static int CalculatedCostAt_NoTerrain(Map map, Pawn pawn, IntVec3 c, IntVec3 prevCell, int? baseCostOverride)
        {
            bool flying = pawn.Flying;
            bool fenceBlocked = pawn.ShouldAvoidFences && (pawn.CurJob == null || !pawn.CurJob.canBashFences);

            TerrainDef terrainDef = map.terrainGrid.TerrainAt(c);
            if (terrainDef == null || (terrainDef.passability == Traversability.Impassable && (!flying || !terrainDef.forcePassableByFlyingPawns)))
            {
                return 10000;
            }

            int num;
            if (baseCostOverride != null)
            {
                num = baseCostOverride.Value;
            }
            else
            {
                num = 0;
            }

            List<Thing> list = map.thingGrid.ThingsListAt(c);
            for (int i = 0; i < list.Count; i++)
            {
                Thing thing = list[i];
                if (thing.def.passability == Traversability.Impassable && (!flying || !thing.def.forcePassableByFlyingPawns))
                {
                    return 10000;
                }
                if (fenceBlocked && thing.def.building != null && thing.def.building.isFence)
                {
                    return 10000;
                }
                // 机动作战：忽略物体 pathCost，仅保留不可通行判定
            }

            // 机动作战：忽略地形相关惩罚（雪/沙/门禁）

            return num;
        }

        // 机动作战：已忽略 pathCost，辅助方法不再需要
    }

    // 切换/分配工作模式时同步对应健康状态
    [HarmonyPatch(typeof(MechanitorControlGroup), "SetWorkModeForPawn", new[] { typeof(Pawn), typeof(MechWorkModeDef) })]
    public static class Patch_MechanitorControlGroup_SetWorkModeForPawn
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, MechWorkModeDef workMode)
        {
            WorkModeUtility.ApplyWorkModeHediff(pawn, workMode);
        }
    }

    // 控制组1的模式变化时，同步「正义」自身的健康状态
    [HarmonyPatch(typeof(MechanitorControlGroup), "SetWorkMode", new[] { typeof(MechWorkModeDef), typeof(GlobalTargetInfo) })]
    public static class Patch_MechanitorControlGroup_SetWorkMode
    {
        [HarmonyPostfix]
        public static void Postfix(MechanitorControlGroup __instance)
        {
            WorkModeUtility.SyncJusticeWithGroup1(__instance);
        }
    }

    // 载入后补齐已分配机械体的健康状态
    [HarmonyPatch(typeof(MechanitorControlGroup), "ExposeData")]
    public static class Patch_MechanitorControlGroup_ExposeData
    {
        [HarmonyPostfix]
        public static void Postfix(MechanitorControlGroup __instance)
        {
            if (Scribe.mode != LoadSaveMode.PostLoadInit)
            {
                return;
            }

            List<AssignedMech> assigned = __instance.AssignedMechs;
            if (assigned == null)
            {
                return;
            }

            for (int i = 0; i < assigned.Count; i++)
            {
                Pawn pawn = assigned[i].pawn;
                if (pawn != null)
                {
                    WorkModeUtility.ApplyWorkModeHediff(pawn, __instance.WorkMode);
                }
            }

            WorkModeUtility.SyncJusticeWithGroup1(__instance);
        }
    }

    // 仅在「正义」控制组显示自定义工作模式
    [HarmonyPatch(typeof(MechanitorControlGroupGizmo), "GetWorkModeOptions")]
    public static class Patch_MechanitorControlGroupGizmo_GetWorkModeOptions
    {
        [HarmonyPrefix]
        public static bool Prefix(MechanitorControlGroup controlGroup, ref IEnumerable<FloatMenuOption> __result)
        {
            bool isJustice = WorkModeUtility.IsJusticeControlGroup(controlGroup);

            IEnumerable<MechWorkModeDef> defs = DefDatabase<MechWorkModeDef>.AllDefsListForReading;
            if (!isJustice)
            {
                defs = defs.Where(d => !WorkModeUtility.IsJusticeWorkMode(d));
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (MechWorkModeDef wm in defs.OrderBy(d => d.uiOrder))
            {
                options.Add(new FloatMenuOption(wm.LabelCap, delegate
                {
                    controlGroup.SetWorkMode(wm);
                }, wm.uiIcon, Color.white, MenuOptionPriority.Default, null, null, 0f, null, null, true, 0, HorizontalJustification.Left, false)
                {
                    tooltip = new TipSignal?(new TipSignal(wm.description, (int)wm.index ^ 234784353))
                });
            }

            __result = options;
            return false;
        }
    }
}
