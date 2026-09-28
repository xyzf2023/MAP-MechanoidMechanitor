using System;
using System.Collections.Generic;
using System.Linq;
using GD3;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.GD5
{
    internal sealed class GD5BlackHiveReturnPoint : IExposable
    {
        internal Pawn? pawn;
        internal IntVec3 cell = IntVec3.Invalid;

        public GD5BlackHiveReturnPoint() { }

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref cell, "cell", IntVec3.Invalid);
        }
    }

    internal static class GD5BlackHiveDryseaUtility
    {
        // 与 GD3.QuestPart_Drysea 的地图尺寸、玩家入口一致。
        internal static readonly IntVec3 Size = new IntVec3(75, 1, 75);
        internal static readonly IntVec3 Entrance = new IntVec3(37, 0, 73);

        internal static void PrepareMap(Map map)
        {
            // Drysea 生成器提供地形；任务中的屋顶、花海和中轴道路在 QuestPart 中另行布置。
            // 这里只复用场景，不启动原任务的毒蜂、对话和任务信号。
            foreach (IntVec3 cell in map.AllCells)
            {
                map.roofGrid.SetRoof(cell, RoofDefOf.RoofRockThin);
                if (!(cell.x == 37 && cell.z >= 37) && cell.GetTerrain(map) == TerrainDefOf.SoilRich)
                {
                    GenPlace.TryPlaceThing(ThingMaker.MakeThing(GDDefOf.Plant_PeaceLily), cell, map, ThingPlaceMode.Direct);
                    GenPlace.TryPlaceThing(ThingMaker.MakeThing(ThingDefOf.Plant_Grass), cell, map, ThingPlaceMode.Direct);
                }
                else if (cell.x == 37 && cell.z >= 37 && cell.GetTerrain(map) != TerrainDefOf.Ice)
                    map.terrainGrid.SetTerrain(cell, GDDefOf.FlagstoneMarble);
            }
            foreach (Plant plant in map.listerThings.ThingsInGroup(ThingRequestGroup.Plant).OfType<Plant>())
            {
                plant.Age += (int)((1f - plant.Growth) * plant.def.plant.growDays);
                plant.Growth = 1f;
            }
            MissionComponent? mission = Find.World.GetComponent<MissionComponent>();
            if (mission?.BranchDict == null || !mission.BranchDict.TryGetValue("WillMilitorDie", out bool died) || died)
                GenPlace.TryPlaceThing(ThingMaker.MakeThing(ThingDefOf.Sarcophagus, ThingDefOf.Uranium),
                    new IntVec3(37, 0, 34), map, ThingPlaceMode.Near);
        }

        internal static bool TryFindCell(Map map, IntVec3 center, out IntVec3 result)
        {
            // 优先入口/原位置附近，拥挤时仍允许 Pawn 共用格子，不把人数限制在固定半径内。
            IntVec3 fallback = IntVec3.Invalid;
            foreach (IntVec3 cell in map.AllCells.OrderBy(c => c.DistanceToSquared(center)))
            {
                if (!cell.Standable(map) || cell.ContainsStaticFire(map)) continue;
                if (!fallback.IsValid) fallback = cell;
                if (cell.GetFirstPawn(map) != null) continue;
                result = cell;
                return true;
            }
            result = fallback;
            return result.IsValid;
        }

        internal static List<Pawn> GetReturnPawns(Map map, Pawn? visitor)
        {
            // 注册表涵盖建筑/合体形态；AllPawns 涵盖普通机械族和容器中的机械族。
            return MechanoidStoryDepartureUtility.GetCandidates(map)
                .Concat(map.mapPawns.AllPawns.Where(p => p.RaceProps.IsMechanoid))
                .Where(p => p != visitor && !p.Dead && !p.Discarded).Distinct().ToList();
        }

        internal static bool TryTransfer(Pawn pawn, Map destination, IntVec3 near, Rot4 rotation)
        {
            if (pawn.Dead || pawn.Destroyed || pawn.Discarded
                || !TryFindCell(destination, near, out IntVec3 cell)) return false;
            Map? source = pawn.Map;
            IntVec3 previous = pawn.Position;
            Rot4 previousRotation = pawn.Rotation;
            bool teleporting = pawn.teleporting;
            try
            {
                pawn.jobs?.StopAll();
                if (source != null && pawn.carryTracker?.CarriedThing != null
                    && !pawn.carryTracker.TryDropCarriedThing(previous, ThingPlaceMode.Near, out _)) return false;
                if (ThingOwnerUtility.GetAllThingsRecursively(pawn).Any(t => t is Pawn)) return false;
                pawn.teleporting = true;
                if (pawn.Spawned)
                {
                    GD5BlackHiveEndingService.PlaySkip(source!, previous, arriving: false);
                    pawn.DeSpawn();
                }
                else if (pawn.holdingOwner != null) return false;
                if (pawn.IsWorldPawn()) Find.WorldPawns.RemovePawn(pawn);
                GenSpawn.Spawn(pawn, cell, destination, rotation);
                if (!pawn.Spawned || pawn.Map != destination)
                    throw new InvalidOperationException("角色未能在目标地图生成。");
                GameComponent_GD5StoryState.Current?.QueueSkipArrival(pawn);
                pawn.Notify_Teleported();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[MAP-GD5] 枯海折跃失败：" + pawn + "\n" + ex);
                // SpawnSetup 的第三方通知可能在生成成功后才抛错，以实际地图归属判断结果。
                if (pawn.Spawned)
                {
                    if (pawn.Map == destination) GameComponent_GD5StoryState.Current?.QueueSkipArrival(pawn);
                    return pawn.Map == destination;
                }
                if (!pawn.Destroyed && pawn.MapHeld == null && source != null)
                {
                    try
                    {
                        GenSpawn.Spawn(pawn, previous, source, previousRotation);
                        GameComponent_GD5StoryState.Current?.QueueSkipArrival(pawn);
                        pawn.Notify_Teleported();
                    }
                    catch (Exception restoreException)
                    {
                        Log.Error("[MAP-GD5] 折跃失败后恢复原位置失败：\n" + restoreException);
                    }
                }
                return false;
            }
            finally
            {
                pawn.teleporting = teleporting;
                // 生成和回滚都失败时仍保留世界引用，返程记录可以在后续重试或读档后找回。
                if (!pawn.Spawned && pawn.holdingOwner == null && pawn.MapHeld == null
                    && !pawn.Destroyed && !pawn.Discarded && !pawn.IsWorldPawn())
                    Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
            }
        }

        internal static bool TryReturnCorpse(Corpse corpse, Map destination, IntVec3 near)
        {
            if (!TryFindCell(destination, near, out IntVec3 cell)) return false;
            Map? source = corpse.MapHeld;
            IntVec3 previous = corpse.PositionHeld;
            try
            {
                if (!corpse.Spawned && (source == null || corpse.holdingOwner == null
                    || !corpse.holdingOwner.TryDrop(corpse, previous, source, ThingPlaceMode.Near, out _))) return false;
                GD5BlackHiveEndingService.PlaySkip(source!, corpse.Position, arriving: false);
                corpse.DeSpawn();
                GenSpawn.Spawn(corpse, cell, destination);
                if (!corpse.Spawned || corpse.Map != destination)
                    throw new InvalidOperationException("机械族遗体未能在返程地图生成。");
                GameComponent_GD5StoryState.Current?.QueueSkipArrival(corpse);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[MAP-GD5] 枯海机械族遗体返程失败：\n" + ex);
                if (!corpse.Spawned && !corpse.Destroyed && source != null)
                    GenSpawn.Spawn(corpse, previous, source);
                if (corpse.Spawned) GameComponent_GD5StoryState.Current?.QueueSkipArrival(corpse);
                return corpse.Spawned && corpse.Map == destination;
            }
        }
    }
}
