#nullable enable
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 普通派系前哨的「布局档位」枚举。
    /// 档位由前哨创建时保存的完成态守军预算决定，保存后不再随财富或设置变化。
    /// </summary>
    public enum FactionOutpostLayoutTier : byte
    {
        Baseline = 0,
        Expanded = 1,
        Large = 2,
        Fortress = 3
    }

    /// <summary>
    /// 普通派系前哨的「布局档位」工具。
    /// 只根据前哨保存的完成态守军预算决定布局档位 / 对应 SitePartDef，
    /// 不计算财富、不生成 Pawn、不创建 WorldObject、不改写前哨保存状态。
    /// </summary>
    public static class FactionOutpostLayoutUtility
    {
        private const int BaselineMaxCompletedPoints = 3000;
        private const int ExpandedMaxCompletedPoints = 8000;
        private const int LargeMaxCompletedPoints = 15000;

        // 仅用于一次运行内对缺失 Def 去重告警，不影响任何游戏状态。
        private static readonly HashSet<string> reportedMissingDefs = new HashSet<string>();

        /// <summary>
        /// 由创建前哨时保存的完成态守军预算推导布局档位。
        /// 严格使用阈值：&gt;=15001 Fortress；8001-15000 Large；3001-8000 Expanded；&lt;=3000 或 &lt;=0 Baseline。
        /// </summary>
        public static FactionOutpostLayoutTier GetTierForCompletedPoints(int completedPoints)
        {
            if (completedPoints <= 0)
            {
                return FactionOutpostLayoutTier.Baseline;
            }

            if (completedPoints <= BaselineMaxCompletedPoints)
            {
                return FactionOutpostLayoutTier.Baseline;
            }

            if (completedPoints <= ExpandedMaxCompletedPoints)
            {
                return FactionOutpostLayoutTier.Expanded;
            }

            if (completedPoints <= LargeMaxCompletedPoints)
            {
                return FactionOutpostLayoutTier.Large;
            }

            return FactionOutpostLayoutTier.Fortress;
        }

        /// <summary>
        /// 返回给定档位对应的「建设中」SitePartDef。
        /// 新增 Def 意外为 null 时写一次明确警告并回退 Baseline，不让游戏因 Def 缺失崩溃。
        /// </summary>
        public static SitePartDef GetBuildingSitePartDef(FactionOutpostLayoutTier tier)
        {
            switch (tier)
            {
                case FactionOutpostLayoutTier.Expanded:
                    return ResolveOrFallback(
                        FactionOutpostDefOf.MAP_FactionOutpost_Building_Expanded,
                        FactionOutpostDefOf.MAP_FactionOutpost_Building,
                        nameof(FactionOutpostDefOf.MAP_FactionOutpost_Building_Expanded));
                case FactionOutpostLayoutTier.Large:
                    return ResolveOrFallback(
                        FactionOutpostDefOf.MAP_FactionOutpost_Building_Large,
                        FactionOutpostDefOf.MAP_FactionOutpost_Building,
                        nameof(FactionOutpostDefOf.MAP_FactionOutpost_Building_Large));
                case FactionOutpostLayoutTier.Fortress:
                    return ResolveOrFallback(
                        FactionOutpostDefOf.MAP_FactionOutpost_Building_Fortress,
                        FactionOutpostDefOf.MAP_FactionOutpost_Building,
                        nameof(FactionOutpostDefOf.MAP_FactionOutpost_Building_Fortress));
                default:
                    return FactionOutpostDefOf.MAP_FactionOutpost_Building;
            }
        }

        /// <summary>
        /// 返回给定档位对应的「建成」SitePartDef。
        /// 新增 Def 意外为 null 时写一次明确警告并回退 Baseline，不让游戏因 Def 缺失崩溃。
        /// </summary>
        public static SitePartDef GetCompletedSitePartDef(FactionOutpostLayoutTier tier)
        {
            switch (tier)
            {
                case FactionOutpostLayoutTier.Expanded:
                    return ResolveOrFallback(
                        FactionOutpostDefOf.MAP_FactionOutpost_Completed_Expanded,
                        FactionOutpostDefOf.MAP_FactionOutpost_Completed,
                        nameof(FactionOutpostDefOf.MAP_FactionOutpost_Completed_Expanded));
                case FactionOutpostLayoutTier.Large:
                    return ResolveOrFallback(
                        FactionOutpostDefOf.MAP_FactionOutpost_Completed_Large,
                        FactionOutpostDefOf.MAP_FactionOutpost_Completed,
                        nameof(FactionOutpostDefOf.MAP_FactionOutpost_Completed_Large));
                case FactionOutpostLayoutTier.Fortress:
                    return ResolveOrFallback(
                        FactionOutpostDefOf.MAP_FactionOutpost_Completed_Fortress,
                        FactionOutpostDefOf.MAP_FactionOutpost_Completed,
                        nameof(FactionOutpostDefOf.MAP_FactionOutpost_Completed_Fortress));
                default:
                    return FactionOutpostDefOf.MAP_FactionOutpost_Completed;
            }
        }

        private static SitePartDef ResolveOrFallback(
            SitePartDef? def,
            SitePartDef fallback,
            string defName)
        {
            if (def != null)
            {
                return def;
            }

            if (reportedMissingDefs.Add(defName))
            {
                Log.Warning(
                    "[MAP] 普通派系前哨布局 Def 缺失，已回退 Baseline 布局: " + defName);
            }

            return fallback;
        }

        /// <summary>
        /// 返回给定 phase / 档位下预期生成的真实建筑群数量（仅用于 DEV 日志与断言参考）。
        /// 建设中任何档位均为 1；建成态 Large / Fortress 为 3，其余为 2。
        /// </summary>
        public static int GetExpectedClusterCount(
            MechanoidMechanitorFactionOutpostPhase phase,
            FactionOutpostLayoutTier tier)
        {
            if (phase != MechanoidMechanitorFactionOutpostPhase.Completed)
            {
                return 1;
            }

            return tier >= FactionOutpostLayoutTier.Large ? 3 : 2;
        }
    }
}
