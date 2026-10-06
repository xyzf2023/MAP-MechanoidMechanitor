using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 物资评级覆盖：允许通过 XML 把指定 ThingDef 显式锁定到某个所需评级等级，
    /// 支持 1..5 级指定物资；未配置物品继续使用原有三级自动分级。
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
                if (o == null || o.thing == null)
                {
                    yield return $"{defName}: 覆盖配置的物品为空。";
                    continue;
                }

                if (o.requiredLevel < 1 || o.requiredLevel > 5)
                {
                    yield return $"{defName}: 物品 {o.thing.defName} 的覆盖等级 requiredLevel 必须在 1..5 范围内（序列五至序列一）。";
                }

                if (!seen.Add(o.thing))
                {
                    yield return $"{defName}: 物品 {o.thing.defName} 的覆盖配置重复。";
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
                if (o != null && o.thing != null)
                {
                    int level = o.requiredLevel >= 1 && o.requiredLevel <= 5
                        ? o.requiredLevel
                        : 5; // 非法等级从严处理，避免提前开放。
                    if (dict.TryGetValue(o.thing, out int previous))
                    {
                        level = System.Math.Max(previous, level);
                    }
                    dict[o.thing] = level;
                }
            }

            return dict;
        }
    }
}
