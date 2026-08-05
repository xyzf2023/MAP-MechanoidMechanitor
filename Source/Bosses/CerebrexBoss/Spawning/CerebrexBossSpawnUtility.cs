using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class CerebrexBossSpawnUtility
    {
        public const int DropOpenDelayTicks = 60;

        private const int MinimumSafeDropSearchRadius = 5;

        private const int MaximumDropSearchRadius = 40;

        /// <summary>
        /// 由主脑组件召唤一批机械族。每只机械族单独空投（以保留按 PawnKind 的失败重试粒度）。
        /// 成功装入空投舱的 Pawn 会立即加入 existingList（计入 32 只上限，含在途单位），
        /// 并登记到空投追踪组件；失败的 PawnKind 通过 controller.RegisterPendingSummonKind 记录以便重试。
        /// 彻底失败（无任何合法落点）的 Pawn 会被安全销毁，不遗留 WorldPawn 或 ThingHolder 引用。
        /// </summary>
        public static int SpawnSummonWave(
            Map map,
            List<PawnKindDef> kinds,
            List<Pawn> existingList,
            IntVec3 center,
            Faction faction,
            CompCerebrexBossController controller)
        {
            if (map == null || kinds == null || kinds.Count == 0 || faction == null || controller == null)
            {
                return 0;
            }

            MapComponent_CerebrexBossDropTracker tracker = MapComponent_CerebrexBossDropTracker.For(map);
            int coreThingId = controller.parent.thingIDNumber;
            List<IntVec3> reserved = new List<IntVec3>();
            int success = 0;

            foreach (PawnKindDef kind in kinds)
            {
                if (kind == null)
                {
                    continue;
                }

                Pawn? pawn = TryGeneratePawn(kind, faction);
                if (pawn == null)
                {
                    controller.RegisterPendingSummonKind(kind);
                    continue;
                }

                if (!TryFindDropCell(map, center, faction, reserved, out IntVec3 cell))
                {
                    controller.RegisterPendingSummonKind(kind);
                    DiscardPawn(pawn);
                    continue;
                }

                reserved.Add(cell);

                if (!TryMakeDropPod(map, faction, cell, new List<Pawn> { pawn }))
                {
                    controller.RegisterPendingSummonKind(kind);
                    DiscardPawn(pawn);
                    continue;
                }

                existingList.Add(pawn);
                tracker.RegisterDrop(pawn, coreThingId, faction);
                success++;
            }

            return success;
        }

        private static Pawn? TryGeneratePawn(PawnKindDef kind, Faction faction)
        {
            try
            {
                PawnGenerationRequest request = new PawnGenerationRequest(
                    kind,
                    faction,
                    PawnGenerationContext.NonPlayer,
                    forceGenerateNewPawn: true);
                Pawn pawn = PawnGenerator.GeneratePawn(request);
                pawn.SetFaction(faction);
                MechanoidMechanitorWorkModeUtility.EnsureMobileCombatHediff(pawn);
                return pawn;
            }
            catch (System.Exception e)
            {
                Log.Warning("[MAP CerebrexBoss] Failed to generate " + kind?.defName + ": " + e.Message);
                return null;
            }
        }

        private static bool TryMakeDropPod(Map map, Faction? faction, IntVec3 cell, List<Pawn> group)
        {
            try
            {
                ActiveTransporterInfo info = new ActiveTransporterInfo();
                info.openDelay = DropOpenDelayTicks;
                info.leaveSlag = false;
                info.savePawnsWithReferenceMode = false;
                info.moveItemsAsideBeforeSpawning = true;
                info.despawnPodBeforeSpawningThing = true;

                for (int i = 0; i < group.Count; i++)
                {
                    if (info.innerContainer.TryAdd(group[i], 1) <= 0)
                    {
                        return false;
                    }
                }

                DropPodUtility.MakeDropPodAt(cell, map, info, faction);
                return true;
            }
            catch (System.Exception e)
            {
                Log.Warning("[MAP CerebrexBoss] Drop pod creation failed: " + e.Message);
                return false;
            }
        }

        private static bool TryFindDropCell(
            Map map,
            IntVec3 center,
            Faction? faction,
            List<IntVec3> reserved,
            out IntVec3 cell)
        {
            IntVec2 size = IntVec2.One;

            // RimWorld 1.6 会使用 maxRadius / 5 计算内部搜索步长；
            // 小于 5 的值会得到零步长，并可能在首次搜索失败后永久循环。
            for (
                int radius = MinimumSafeDropSearchRadius;
                radius <= MaximumDropSearchRadius;
                radius += 2)
            {
                int safeRadius = radius < MinimumSafeDropSearchRadius
                    ? MinimumSafeDropSearchRadius
                    : radius;
                if (DropCellFinder.TryFindDropSpotNear(
                        center,
                        map,
                        out cell,
                        allowFogged: false,
                        canRoofPunch: false,
                        safeRadius,
                        allowIndoors: true,
                        size,
                        mustBeReachableFromCenter: true)
                    && DropCellFinder.SkyfallerCanLandAt(cell, map, size, faction)
                    && !reserved.Contains(cell)
                    && cell.GetRoof(map) != RoofDefOf.RoofRockThick)
                {
                    return true;
                }
            }

            // 最终尝试地图边缘合法空投格。
            cell = DropCellFinder.RandomDropSpot(map);
            if (cell.IsValid
                && cell.InBounds(map)
                && DropCellFinder.SkyfallerCanLandAt(cell, map, size, faction)
                && cell.GetRoof(map) != RoofDefOf.RoofRockThick
                && !reserved.Contains(cell))
            {
                return true;
            }

            cell = IntVec3.Invalid;
            return false;
        }

        private static void DiscardPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return;
            }

            if (pawn.ParentHolder is IThingHolder holder
                && holder.GetDirectlyHeldThings() is ThingOwner owner)
            {
                owner.Remove(pawn);
            }

            if (Find.WorldPawns.Contains(pawn))
            {
                Find.WorldPawns.RemovePawn(pawn);
            }

            if (!pawn.Destroyed)
            {
                pawn.Destroy();
            }
        }
    }
}
