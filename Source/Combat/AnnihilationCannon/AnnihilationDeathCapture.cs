using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 仅落点结算期间观察内圈死亡。不改写 Kill；捕获附近放置和堆叠合并产生的残留，
    /// 同时覆盖被外圈爆炸提前杀死的 Pawn。快照只在命中本能力的死亡事件上创建。
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill), new[] { typeof(DamageInfo?), typeof(Hediff) })]
    internal static class AnnihilationDeathCapture
    {
        [ThreadStatic]
        private static CaptureBatch? activeBatch;

        internal sealed class CaptureBuffers
        {
            internal readonly Dictionary<Thing, int> before = new Dictionary<Thing, int>();
            internal readonly List<Thing> possessions = new List<Thing>();
            internal readonly List<KeyValuePair<Thing, int>> changes = new List<KeyValuePair<Thing, int>>();

            internal void Clear()
            {
                before.Clear();
                possessions.Clear();
                changes.Clear();
            }
        }

        // 阶段作用域只复用已结束捕获的缓存，不合并不同 Kill 的观察窗口。
        internal sealed class CaptureBatch : IDisposable
        {
            internal readonly Map map;
            private readonly CaptureBatch? previous;
            private readonly Stack<CaptureBuffers> available = new Stack<CaptureBuffers>();
            private bool disposed;

            internal CaptureBatch(Map map)
            {
                this.map = map;
                previous = activeBatch;
                activeBatch = this;
            }

            internal CaptureBuffers Rent() => available.Count > 0 ? available.Pop() : new CaptureBuffers();

            internal void Return(CaptureBuffers buffers)
            {
                buffers.Clear();
                if (!disposed) available.Push(buffers);
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                activeBatch = previous;
                available.Clear();
            }
        }

        internal sealed class Capture
        {
            internal Map map = null!;
            internal List<AnnihilationImpact> impacts = null!;
            internal CaptureBuffers buffers = null!;
            internal CaptureBatch? batch;
            internal Thing source = null!;
            internal bool finished;
        }

        internal static IDisposable BeginBatch(Map map) => new CaptureBatch(map);

        [HarmonyPrefix]
        private static void Prefix(Pawn __instance, out Capture? __state)
        {
            __state = null;
            Map? map = __instance.MapHeld;
            if (map == null || __instance.Dead) return;
            List<AnnihilationImpact> impacts = map.listerThings
                .ThingsOfDef(AnnihilationCannonDefOf.MAP_AnnihilationImpact)
                .OfType<AnnihilationImpact>().Where(i => i.WatchesDeath(__instance)).ToList();
            if (impacts.Count == 0) return;
            __state = BeginCapture(map, impacts, __instance);
        }

        internal static Capture BeginCapture(Map map, List<AnnihilationImpact> impacts, Thing source)
        {
            CaptureBatch? batch = activeBatch?.map == map ? activeBatch : null;
            CaptureBuffers buffers = batch?.Rent() ?? new CaptureBuffers();
            Capture capture = new Capture
            {
                map = map,
                source = source,
                impacts = impacts,
                buffers = buffers,
                batch = batch
            };
            try
            {
                foreach (Thing thing in map.listerThings.AllThings)
                    if (AnnihilationImpact.IsPhysicalThing(thing) && !(thing is Pawn))
                        buffers.before.Add(thing, thing.stackCount);
                if (source is IThingHolder holder)
                    ThingOwnerUtility.GetAllThingsRecursively(holder, buffers.possessions, allowUnreal: false);
                return capture;
            }
            catch
            {
                batch?.Return(buffers);
                throw;
            }
        }

        // 在死亡 Postfix（包括合体解除）之后观察；异常路径也收集，不吞掉原始异常。
        [HarmonyFinalizer, HarmonyPriority(Priority.Last)]
        private static void Finalizer(Pawn __instance, Capture? __state)
        {
            if (__state == null) return;
            FinishCapture(__state);
            foreach (AnnihilationImpact impact in __state.impacts)
                if (!impact.Destroyed && __instance.Corpse != null) impact.RecordResidue(__instance.Corpse, 0);
        }

        // 主动 Kill 的调用方从 finally 收集，覆盖回调只执行了一部分便抛异常的情况。
        internal static void FinishCapture(Capture? capture)
        {
            if (capture == null || capture.finished) return;
            capture.finished = true;
            CaptureBuffers buffers = capture.buffers;
            try
            {
                if (!capture.impacts.Any(impact => !impact.Destroyed)) return;
                bool snapshotCompleted = false;
                try
                {
                    // 同次死亡的前后差异只计算一次，再交给所有观察该死亡的落点。
                    // 保留全图观测，覆盖范围外掉落及第三方直接修改 stackCount 的回调。
                    foreach (Thing thing in capture.map.listerThings.AllThings)
                    {
                        if (!AnnihilationImpact.IsPhysicalThing(thing) || thing is Pawn) continue;
                        buffers.before.TryGetValue(thing, out int count);
                        if (thing.stackCount > count)
                            buffers.changes.Add(new KeyValuePair<Thing, int>(thing, count));
                    }
                    snapshotCompleted = true;
                }
                catch (Exception ex)
                {
                    Log.Error("[MAP-机械族机械师] 湮灭炮收集击毁残留时发生异常：" + ex);
                }
                foreach (AnnihilationImpact impact in capture.impacts)
                {
                    if (impact.Destroyed) continue;
                    try
                    {
                        foreach (KeyValuePair<Thing, int> change in buffers.changes)
                            impact.RecordResidue(change.Key, change.Value);
                        // Kill 被拦截或半途失败时，不登记仍活着的 Pawn 的装备和库存。
                        if (snapshotCompleted && (!(capture.source is Pawn pawn) || pawn.Dead))
                            foreach (Thing thing in buffers.possessions)
                                if (thing != null && !thing.Destroyed && !(thing is Pawn)) impact.RecordResidue(thing, 0);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[MAP-机械族机械师] 湮灭炮收集击毁残留时发生异常：" + ex);
                    }
                }
            }
            finally { capture.batch?.Return(buffers); }
        }
    }
}
