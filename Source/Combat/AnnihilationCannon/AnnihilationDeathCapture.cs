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
        internal sealed class Capture
        {
            internal Map map = null!;
            internal List<AnnihilationImpact> impacts = null!;
            internal Dictionary<Thing, int> before = null!;
            internal List<Thing> possessions = null!;
            internal Thing source = null!;
        }

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
            return new Capture
            {
                map = map,
                source = source,
                impacts = impacts,
                before = map.listerThings.AllThings.Where(t => AnnihilationImpact.IsPhysicalThing(t) && !(t is Pawn))
                    .ToDictionary(t => t, t => t.stackCount),
                possessions = source is IThingHolder holder
                    ? ThingOwnerUtility.GetAllThingsRecursively(holder, allowUnreal: false)
                    : new List<Thing>()
            };
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
            if (capture == null) return;
            foreach (AnnihilationImpact impact in capture.impacts)
            {
                if (impact.Destroyed) continue;
                try
                {
                    foreach (Thing thing in capture.map.listerThings.AllThings)
                    {
                        if (!AnnihilationImpact.IsPhysicalThing(thing) || thing is Pawn) continue;
                        capture.before.TryGetValue(thing, out int count);
                        if (thing.stackCount > count) impact.RecordResidue(thing, count);
                    }
                    // Kill 被拦截或半途失败时，不登记仍活着的 Pawn 的装备和库存。
                    if (!(capture.source is Pawn pawn) || pawn.Dead)
                        foreach (Thing thing in capture.possessions)
                            if (thing != null && !thing.Destroyed && !(thing is Pawn)) impact.RecordResidue(thing, 0);
                }
                catch (Exception ex)
                {
                    Log.Error("[MAP] 湮灭炮收集击毁残留时发生异常：" + ex);
                }
            }
        }
    }
}
