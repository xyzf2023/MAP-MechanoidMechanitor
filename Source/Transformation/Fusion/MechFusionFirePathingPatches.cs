using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Unity.Collections;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    // 原版 1.6 的寻路在后台使用地图成本快照。按地图注册独立数据源，
    // 不临时改写共享网格；数组的刷新与释放均交给原版 PathFinderMapData。
    internal sealed class MechFusionFirePathingSources
    {
        internal static readonly ConditionalWeakTable<PathFinderMapData, MechFusionFirePathingSources>
            ByMapData = new();

        internal readonly FirelessCostSource Normal;
        internal readonly FirelessCostSource FenceBlocked;
        internal readonly FirelessDangerSource Danger;

        internal MechFusionFirePathingSources(Map map, PathFinderMapData owner)
        {
            Normal = new FirelessCostSource(map, map.pathing.Normal.pathGrid);
            FenceBlocked = new FirelessCostSource(map, map.pathing.FenceBlocked.pathGrid);
            Danger = new FirelessDangerSource(map);
            owner.RegisterSource(Normal);
            owner.RegisterSource(FenceBlocked);
            owner.RegisterSource(Danger);
        }

        internal sealed class FirelessCostSource : SimplePathFinderDataSource<int>
        {
            private readonly PathGrid grid;

            internal FirelessCostSource(Map map, PathGrid grid) : base(map)
            {
                this.grid = grid;
            }

            public override void ComputeAll(IEnumerable<PathRequest> requests)
            {
                for (int i = 0; i < cellCount; i++)
                {
                    UpdateCell(map.cellIndices.IndexToCell(i));
                }
            }

            public override bool UpdateIncrementally(
                IEnumerable<PathRequest> requests, List<IntVec3> cellDeltas)
            {
                foreach (IntVec3 cell in cellDeltas)
                {
                    UpdateCell(cell);
                }
                return false;
            }

            private void UpdateCell(IntVec3 cell)
            {
                // perceivedStatic 只控制原版地面火焰及邻格的附加成本。
                // 地形、建筑、门、围栏和积雪等成本仍由原版计算。
                data[map.cellIndices.CellToIndex(cell)] =
                    grid.CalculatedCostAt(cell, false, IntVec3.Invalid);
            }
        }

        internal sealed class FirelessDangerSource : SimpleBoolPathFinderDataSource
        {
            internal FirelessDangerSource(Map map) : base(map) { }

            public override void ComputeAll(IEnumerable<PathRequest> requests)
            {
                for (int i = 0; i < cellCount; i++)
                {
                    UpdateCell(map.cellIndices.IndexToCell(i));
                }
            }

            public override bool UpdateIncrementally(
                IEnumerable<PathRequest> requests, List<IntVec3> cellDeltas)
            {
                foreach (IntVec3 cell in cellDeltas)
                {
                    UpdateCell(cell);
                }
                return false;
            }

            private void UpdateCell(IntVec3 cell)
            {
                bool dangerous = cell.GetTerrain(map).dangerous;
                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; !dangerous && i < things.Count; i++)
                {
                    Thing thing = things[i];
                    dangerous = thing.def.pathfinderDangerous
                        && thing is not Pawn && thing is not Fire;
                }
                data.Set(map.cellIndices.CellToIndex(cell), dangerous);
            }
        }
    }

    [HarmonyPatch(typeof(PathFinderMapData), MethodType.Constructor, new[] { typeof(Map) })]
    internal static class MechFusionFirePathingRegistrationPatch
    {
        public static void Postfix(PathFinderMapData __instance, Map map)
        {
            MechFusionFirePathingSources.ByMapData.Add(
                __instance, new MechFusionFirePathingSources(map, __instance));
        }
    }

    // 仅用于区分原版网格缓存键，原有 customizer 及其额外成本仍由原版使用。
    internal sealed class MechFusionFirePathingCacheKey : PathRequest.IPathGridCustomizer
    {
        private static readonly MechFusionFirePathingCacheKey WithoutCustomizer = new();
        private static readonly ConditionalWeakTable<PathRequest.IPathGridCustomizer,
            MechFusionFirePathingCacheKey> WithCustomizer = new();
        private readonly PathRequest.IPathGridCustomizer? original;

        private MechFusionFirePathingCacheKey(PathRequest.IPathGridCustomizer? original = null)
        {
            this.original = original;
        }

        internal static MechFusionFirePathingCacheKey For(PathRequest.IPathGridCustomizer? original)
        {
            return original == null ? WithoutCustomizer
                : WithCustomizer.GetValue(original, value => new MechFusionFirePathingCacheKey(value));
        }

        public NativeArray<ushort> GetOffsetGrid() => original?.GetOffsetGrid() ?? default;
    }

    [HarmonyPatch(typeof(PathFinder.MapGridRequest), nameof(PathFinder.MapGridRequest.For))]
    internal static class MechFusionFirePathingCachePatch
    {
        public static void Postfix(PathRequest request, ref PathFinder.MapGridRequest __result)
        {
            if (CompMechFusionShell.IsWornBy(request.pawn ?? request.TraverseParms.pawn))
            {
                __result.customizer = MechFusionFirePathingCacheKey.For(__result.customizer);
            }
        }
    }

    [HarmonyPatch(typeof(PathFinderMapData), nameof(PathFinderMapData.ParameterizeGridJob))]
    internal static class MechFusionFirePathingGridPatch
    {
        public static void Postfix(PathFinderMapData __instance,
            ref PathFinder.MapGridRequest query, ref PathGridJob job)
        {
            // 使用与缓存键相同的资格快照，不在后台读取 Pawn 或合体会话。
            if (query.customizer is MechFusionFirePathingCacheKey
                && MechFusionFirePathingSources.ByMapData.TryGetValue(__instance, out var sources))
            {
                job.pathGridDirect = query.traverseParams.fenceBlocked
                    ? sources.FenceBlocked.Data : sources.Normal.Data;
                job.persistentDanger = sources.Danger.Data;
            }
        }
    }
}
