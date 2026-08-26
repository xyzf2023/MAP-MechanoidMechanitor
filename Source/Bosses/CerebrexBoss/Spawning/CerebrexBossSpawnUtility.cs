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
        /// 成功装入空投舱的 Pawn 会立即加入 existingList（计入主脑本场战斗快照决定的最大同时存活上限，含在途单位），
        /// 并登记到空投追踪组件；失败的 PawnKind 通过 controller.RegisterPendingSummonKind 记录以便重试。
        /// 本批次优先围绕第一只成功落点集中空投（clusterCenter），附近无合法落点时才逐步扩大到主脑中心与地图边缘。
        /// 彻底失败（无任何合法落点）的 Pawn 会被安全销毁，不遗留 WorldPawn 或 ThingHolder 引用。
        /// </summary>
        public static int SpawnSummonWave(
            Map map,
            List<PawnKindDef> kinds,
            List<Pawn> existingList,
            IntVec3 center,
            Faction faction,
            CompCerebrexBossController controller,
            bool applyMobileCombat)
        {
            if (map == null || kinds == null || kinds.Count == 0 || faction == null || controller == null)
            {
                return 0;
            }

            MapComponent_CerebrexBossDropTracker tracker = MapComponent_CerebrexBossDropTracker.For(map);
            int coreThingId = controller.parent.thingIDNumber;
            List<IntVec3> reserved = new List<IntVec3>();
            int success = 0;

            // 本批次共享空投中心：仅属于当前这次生成调用，不写入存档。
            // 第一只成功落点保存为 clusterCenter，后续单位优先围绕它集中空投。
            IntVec3 clusterCenter = IntVec3.Invalid;

            foreach (PawnKindDef kind in kinds)
            {
                if (kind == null)
                {
                    continue;
                }

                Pawn? pawn = TryGeneratePawn(kind, faction, applyMobileCombat);
                if (pawn == null)
                {
                    controller.RegisterPendingSummonKind(kind);
                    continue;
                }

                if (!TryFindDropCell(map, center, clusterCenter, faction, reserved, out IntVec3 cell))
                {
                    controller.RegisterPendingSummonKind(kind);
                    DiscardPawn(pawn);
                    continue;
                }

                // 必须先成功创建空投舱，才允许登记本批次集中中心与占用记录。
                if (!TryMakeDropPod(map, faction, cell, new List<Pawn> { pawn }))
                {
                    controller.RegisterPendingSummonKind(kind);
                    DiscardPawn(pawn);
                    continue;
                }

                // 仅第一只成功落点成为本批次集中空投中心（不跨波次、不存档）。
                reserved.Add(cell);
                if (!clusterCenter.IsValid && cell.IsValid)
                {
                    clusterCenter = cell;
                }

                existingList.Add(pawn);
                tracker.RegisterDrop(pawn, coreThingId, faction, applyMobileCombat);
                success++;
            }

            return success;
        }

        private static Pawn? TryGeneratePawn(PawnKindDef kind, Faction faction, bool applyMobileCombat)
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
                if (applyMobileCombat)
                {
                    MechanoidMechanitorWorkModeUtility.EnsureMobileCombatHediff(pawn);
                }

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
            IntVec3 coreCenter,
            IntVec3 clusterCenter,
            Faction? faction,
            List<IntVec3> reserved,
            out IntVec3 cell)
        {
            // 1. 若本批次已有集中落点，先围绕 clusterCenter 使用较小安全半径搜索。
            if (clusterCenter.IsValid)
            {
                int[] nearRadii = { 5, 7, 9, 11 };
                foreach (int radius in nearRadii)
                {
                    if (TryFindDropSpotNearCenter(
                        map,
                        clusterCenter,
                        faction,
                        reserved,
                        radius,
                        out cell))
                    {
                        return true;
                    }
                }
            }

            // 2. 围绕主脑中心：5～20 格。
            for (int radius = 5; radius <= 20; radius += 2)
            {
                if (TryFindDropSpotNearCenter(
                    map,
                    coreCenter,
                    faction,
                    reserved,
                    radius,
                    out cell))
                {
                    return true;
                }
            }

            // 3. 围绕主脑中心：22～40 格。
            for (int radius = 22; radius <= MaximumDropSearchRadius; radius += 2)
            {
                if (TryFindDropSpotNearCenter(
                    map,
                    coreCenter,
                    faction,
                    reserved,
                    radius,
                    out cell))
                {
                    return true;
                }
            }

            // 4. 最终保障：地图边缘随机合法空投格（仍按相同合法性判断，禁止无界搜索）。
            cell = DropCellFinder.RandomDropSpot(map);
            if (IsValidDropCell(map, cell, faction, reserved))
            {
                return true;
            }

            cell = IntVec3.Invalid;
            return false;
        }

        private static bool TryFindDropSpotNearCenter(
            Map map,
            IntVec3 center,
            Faction? faction,
            List<IntVec3> reserved,
            int radius,
            out IntVec3 cell)
        {
            // RimWorld 1.6 会使用 maxRadius / 5 计算内部搜索步长；
            // 小于 5 的值会得到零步长，并可能在首次搜索失败后永久循环。
            int safeRadius = UnityEngine.Mathf.Max(radius, MinimumSafeDropSearchRadius);
            if (DropCellFinder.TryFindDropSpotNear(
                    center,
                    map,
                    out cell,
                    allowFogged: false,
                    canRoofPunch: false,
                    safeRadius,
                    allowIndoors: true,
                    IntVec2.One,
                    mustBeReachableFromCenter: true)
                && IsValidDropCell(map, cell, faction, reserved))
            {
                return true;
            }

            return false;
        }

        private static bool IsValidDropCell(
            Map map,
            IntVec3 cell,
            Faction? faction,
            List<IntVec3> reserved)
        {
            if (!cell.IsValid || !cell.InBounds(map))
            {
                return false;
            }

            if (!DropCellFinder.SkyfallerCanLandAt(cell, map, IntVec2.One, faction))
            {
                return false;
            }

            if (reserved.Contains(cell))
            {
                return false;
            }

            // 禁止为集中空投而允许砸穿厚岩顶。
            if (cell.GetRoof(map) == RoofDefOf.RoofRockThick)
            {
                return false;
            }

            return true;
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
