using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 物资评级覆盖：允许通过 XML 把指定 ThingDef 显式锁定到某个所需评级等级，
    /// 覆盖按市场价值的自动分级。例如把某件贵重但低价的功能性建筑固定为 3 级。
    /// 集中在一处维护，避免把大量具体 DefName 写死成另一套权限表。
    /// </summary>
    public sealed class PurgeDirectiveGoodsRatingOverrideDef : Def
    {
        public List<ThingRatingOverride> overrides = new List<ThingRatingOverride>();

        public class ThingRatingOverride : IExposable
        {
            public ThingDef? thing;
            public int requiredLevel = 1;

            public void ExposeData()
            {
                Scribe_Defs.Look(ref thing, "thing");
                Scribe_Values.Look(ref requiredLevel, "requiredLevel", 1);
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            if (overrides == null)
            {
                yield break;
            }

            HashSet<ThingDef> seen = new HashSet<ThingDef>();
            foreach (ThingRatingOverride o in overrides)
            {
                if (o.thing == null)
                {
                    yield return $"{defName}: override with null thing.";
                    continue;
                }

                if (o.requiredLevel < 1 || o.requiredLevel > 5)
                {
                    yield return $"{defName}: override {o.thing.defName} requiredLevel must be 1..5.";
                }

                if (!seen.Add(o.thing))
                {
                    yield return $"{defName}: duplicate override for {o.thing.defName}.";
                }
            }
        }
    }

    [DefOf]
    public static class PurgeDirectiveGoodsRatingOverrideDefOf
    {
        public static PurgeDirectiveGoodsRatingOverrideDef MAP_PurgeDirectiveGoodsRatingOverrides = null!;

        static PurgeDirectiveGoodsRatingOverrideDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(PurgeDirectiveGoodsRatingOverrideDefOf));
        }
    }

    /// <summary>
    /// 物资评级覆盖查询工具。带缓存，避免每次 UI 刷新都遍历列表。
    /// </summary>
    public static class PurgeDirectiveGoodsRatingOverrideUtility
    {
        private static Dictionary<ThingDef, int>? cache;

        public static void ClearCache()
        {
            cache = null;
        }

        public static int GetOverrideLevel(ThingDef? def)
        {
            if (def == null)
            {
                return 0;
            }

            cache ??= BuildCache();
            return cache.TryGetValue(def, out int level) ? level : 0;
        }

        private static Dictionary<ThingDef, int> BuildCache()
        {
            Dictionary<ThingDef, int> dict = new Dictionary<ThingDef, int>();
            PurgeDirectiveGoodsRatingOverrideDef? def =
                PurgeDirectiveGoodsRatingOverrideDefOf.MAP_PurgeDirectiveGoodsRatingOverrides;
            if (def?.overrides == null)
            {
                return dict;
            }

            foreach (PurgeDirectiveGoodsRatingOverrideDef.ThingRatingOverride o in def.overrides)
            {
                if (o.thing != null)
                {
                    dict[o.thing] = o.requiredLevel;
                }
            }

            return dict;
        }
    }
}
