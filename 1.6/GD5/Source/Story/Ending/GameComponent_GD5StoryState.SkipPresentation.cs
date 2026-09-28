using System;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.GD5
{
    // 只记录待播放的表现，不负责移出、生成或结算角色。
    internal sealed class GD5BlackHiveSkipArrival : IExposable
    {
        internal Thing? thing;
        internal Map? map;
        internal int expiresTick;

        public GD5BlackHiveSkipArrival() { }

        public void ExposeData()
        {
            Scribe_References.Look(ref thing, "thing");
            Scribe_References.Look(ref map, "map");
            Scribe_Values.Look(ref expiresTick, "expiresTick");
        }
    }

    public sealed partial class GameComponent_GD5StoryState
    {
        private const int SkipCameraDelayTicks = 60;
        private List<GD5BlackHiveSkipArrival> skipArrivals = new List<GD5BlackHiveSkipArrival>();
        private Map? skipCameraMap;
        private IntVec3 skipCameraCell = IntVec3.Invalid;
        private int skipCameraTick = -1;
        // 切图后再等一帧，让相机位置和可见范围更新，再生成受视野裁剪的粒子。
        private Map? skipObservedMap;
        private int skipObservedFrame = -1;

        internal void QueueSkipArrival(Thing thing)
        {
            if (!thing.Spawned) return;
            skipArrivals.RemoveAll(a => a.thing == thing);
            skipArrivals.Add(new GD5BlackHiveSkipArrival
            {
                thing = thing,
                map = thing.Map,
                expiresTick = Find.TickManager.TicksGame + 180
            });
        }

        private void QueueSkipCameraJump(Map map, IntVec3 cell)
        {
            skipCameraMap = map;
            skipCameraCell = cell;
            skipCameraTick = Find.TickManager.TicksGame + SkipCameraDelayTicks;
        }

        private void UpdateSkipPresentation()
        {
            if (skipCameraMap == null && skipArrivals.Count == 0) return;
            if (Verse.Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
            try
            {
                int now = Find.TickManager.TicksGame;
                if (skipCameraMap != null)
                {
                    if (!Find.Maps.Contains(skipCameraMap) || IsCompletingEnding
                        || (Find.CurrentMap == skipCameraMap && WorldRendererUtility.DrawingMap))
                        ClearSkipCameraJump();
                    else if (now >= skipCameraTick)
                    {
                        Map map = skipCameraMap;
                        IntVec3 cell = skipCameraCell.InBounds(map) ? skipCameraCell : map.Center;
                        ClearSkipCameraJump();
                        CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(cell, map), CameraJumper.MovementMode.Cut);
                        skipObservedMap = map;
                        skipObservedFrame = Time.frameCount;
                        return;
                    }
                }

                Map? drawnMap = WorldRendererUtility.DrawingMap ? Find.CurrentMap : null;
                if (skipObservedMap != drawnMap)
                {
                    skipObservedMap = drawnMap;
                    skipObservedFrame = Time.frameCount;
                }
                for (int i = skipArrivals.Count - 1; i >= 0; i--)
                {
                    GD5BlackHiveSkipArrival arrival = skipArrivals[i];
                    Thing? thing = arrival.thing;
                    if (thing?.Spawned != true || arrival.map == null || thing.Map != arrival.map || !Find.Maps.Contains(arrival.map)
                        || now > arrival.expiresTick)
                    {
                        skipArrivals.RemoveAt(i);
                        continue;
                    }
                    if (arrival.map != drawnMap || Time.frameCount <= skipObservedFrame) continue;
                    // 先移除再播放，异常和读档都不会重做角色传送。
                    skipArrivals.RemoveAt(i);
                    GD5BlackHiveEndingService.PlaySkip(thing.Map, thing.Position, arriving: true);
                }
            }
            catch (Exception ex)
            {
                ClearSkipCameraJump();
                skipArrivals.Clear();
                Log.ErrorOnce("[MAP-GD5] 折跃画面切换失败：" + ex, 0x47503522);
            }
        }

        private void ClearSkipCameraJump()
        {
            skipCameraMap = null;
            skipCameraCell = IntVec3.Invalid;
            skipCameraTick = -1;
        }

        private void ResetSkipPresentation()
        {
            ClearSkipCameraJump();
            skipArrivals.Clear();
            skipObservedMap = null;
            skipObservedFrame = -1;
        }

        private void ExposeSkipPresentation()
        {
            Scribe_Collections.Look(ref skipArrivals, "gd5BlackHiveSkipArrivals", LookMode.Deep);
            Scribe_References.Look(ref skipCameraMap, "gd5BlackHiveSkipCameraMap");
            Scribe_Values.Look(ref skipCameraCell, "gd5BlackHiveSkipCameraCell", IntVec3.Invalid);
            Scribe_Values.Look(ref skipCameraTick, "gd5BlackHiveSkipCameraTick", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                skipArrivals ??= new List<GD5BlackHiveSkipArrival>();
                skipArrivals.RemoveAll(a => a == null || a.thing == null || a.map == null);
                skipObservedMap = null;
                skipObservedFrame = -1;
            }
        }
    }
}
