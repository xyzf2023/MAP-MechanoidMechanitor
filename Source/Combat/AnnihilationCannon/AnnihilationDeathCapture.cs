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
            __state = new Capture
            {
                map = map,
                impacts = impacts,
                before = map.listerThings.AllThings.Where(t => AnnihilationImpact.IsPhysicalThing(t) && !(t is Pawn))
                    .ToDictionary(t => t, t => t.stackCount),
                possessions = ThingOwnerUtility.GetAllThingsRecursively(__instance, allowUnreal: false)
            };
        }

        // 最后观察原版及其他死亡 Postfix 留下的对象（包括合体解除）。
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void Postfix(Pawn __instance, Capture? __state)
        {
            if (__state == null || !__instance.Dead) return;
            foreach (AnnihilationImpact impact in __state.impacts)
            {
                if (impact.Destroyed) continue;
                foreach (Thing thing in __state.map.listerThings.AllThings)
                {
                    if (!AnnihilationImpact.IsPhysicalThing(thing) || thing is Pawn) continue;
                    __state.before.TryGetValue(thing, out int count);
                    if (thing.stackCount > count) impact.RecordResidue(thing, count);
                }
                foreach (Thing thing in __state.possessions)
                    if (thing != null && !thing.Destroyed && !(thing is Pawn)) impact.RecordResidue(thing, 0);
                if (__instance.Corpse != null) impact.RecordResidue(__instance.Corpse, 0);
            }
        }
    }
}
