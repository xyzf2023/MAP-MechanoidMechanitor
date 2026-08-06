using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    ///     并行思维阵列的固定数值与跨地图汇总逻辑集中处。
    ///     建筑引用只保存在 Comp 中；健康状态不保存任何建筑引用。
    /// </summary>
    public static class ParallelThoughtArrayUtility
    {
        public const int MinBoostPercent = 100;

        // 仅作为 int 存档与加法的技术安全护栏，不作为平衡上限。
        // 该值与 100% 起点、50% 档位严格对齐，并略低于 int.MaxValue。
        public const int MaxBoostPercent = 2147483600;

        public const int BoostStepPercent = 50;
        public const int ShiftBoostPercent = 100;
        public const int ControlBoostPercent = 1000;

        public const float BasePowerConsumption = 1000f;
        public const float PowerPerBoostStep = 600f;
        public const float MaxPowerPerBoostStep = 12000f;
        public const float IdlePowerConsumption = 100f;

        public const int LowLoadStepCount = 16;
        public const int StandardLoadEndStep = 96;
        public const int FallbackRefreshIntervalTicks = 60;

        /// <summary>
        ///     将任意档位限制到技术安全范围，并对齐到 50% 离散档位。
        ///     存档出现异常数值时也必须自动规范化。
        /// </summary>
        public static int ClampBoostPercent(int percent)
        {
            long min = MinBoostPercent;
            long max = MaxBoostPercent;
            long step = BoostStepPercent;
            long clamped = Math.Max(min, Math.Min(max, (long)percent));
            long relative = clamped - min;
            long alignedSteps = (relative + step / 2L) / step;
            long aligned = min + alignedSteps * step;
            return (int)Math.Max(min, Math.Min(max, aligned));
        }

        /// <summary>
        ///     增幅百分数 -> 意识偏移（100% => 1f）。
        /// </summary>
        public static float BoostPercentToConsciousnessOffset(int boostPercent)
        {
            return ClampBoostPercent(boostPercent) / 100f;
        }

        /// <summary>
        ///     使用默认参数计算三阶段耗电曲线。
        ///     第 1~16 档固定 600W；第 17~96 档平滑增长至 12000W；其后固定 12000W/档。
        /// </summary>
        public static float GetPowerConsumptionForBoost(int boostPercent)
        {
            int clamped = ClampBoostPercent(boostPercent);
            long totalSteps =
                Math.Max(0L, ((long)clamped - MinBoostPercent) / BoostStepPercent);

            double total = BasePowerConsumption;

            long lowSteps = Math.Min(totalSteps, LowLoadStepCount);
            total += lowSteps * PowerPerBoostStep;

            int curveStepCount = Math.Max(0, StandardLoadEndStep - LowLoadStepCount);
            long usedCurveSteps =
                Math.Min(
                    Math.Max(0L, totalSteps - LowLoadStepCount),
                    curveStepCount);

            for (int i = 0; i < usedCurveSteps; i++)
            {
                double t = curveStepCount <= 1
                    ? 1d
                    : i / (double)(curveStepCount - 1);
                double smooth = t * t * (3d - 2d * t);
                total += PowerPerBoostStep
                    + (MaxPowerPerBoostStep - PowerPerBoostStep) * smooth;
            }

            long highSteps = Math.Max(0L, totalSteps - StandardLoadEndStep);
            total += highSteps * MaxPowerPerBoostStep;

            if (double.IsNaN(total) || total <= 0d)
            {
                return 0f;
            }

            if (double.IsInfinity(total) || total >= float.MaxValue)
            {
                return float.MaxValue;
            }

            return (float)total;
        }

        /// <summary>
        ///     根据目标类型返回翻译后的能力术语：机械体“数据处理”，非机械体“意识”。
        /// </summary>
        public static string GetCapacityTerm(Pawn? pawn)
        {
            if (pawn != null && pawn.RaceProps.IsMechanoid)
            {
                return "MAP_MechanoidMechanitor.DataProcessing".Translate();
            }

            return "MAP_MechanoidMechanitor.Consciousness".Translate();
        }

        /// <summary>
        ///     遍历全部已加载地图，汇总当前正在为该目标提供增幅的阵列的总增幅百分数。
        ///     不设平衡叠加上限；只在 int 返回类型达到极限时饱和，避免多建筑求和溢出。
        /// </summary>
        public static int GetTotalActiveBoostPercent(Pawn? target)
        {
            if (target == null)
            {
                return 0;
            }

            long total = 0L;
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (map == null)
                {
                    continue;
                }

                List<Building> buildings =
                    map.listerBuildings.AllBuildingsColonistOfDef(
                        MAPMechanitor_ThingDefOf.MAP_ParallelThoughtArray);
                if (buildings == null)
                {
                    continue;
                }

                for (int j = 0; j < buildings.Count; j++)
                {
                    CompParallelThoughtArray? comp =
                        buildings[j].TryGetComp<CompParallelThoughtArray>();
                    if (comp != null && comp.IsProvidingBoostTo(target))
                    {
                        total += comp.EffectiveBoostPercent;
                        if (total >= int.MaxValue)
                        {
                            return int.MaxValue;
                        }
                    }
                }
            }

            return (int)total;
        }

        /// <summary>
        ///     全部阵列对该目标的有效意识偏移总和。
        /// </summary>
        public static float GetTotalActiveConsciousnessOffset(Pawn? target)
        {
            return GetTotalActiveBoostPercent(target) / 100f;
        }

        /// <summary>
        ///     刷新目标动态意识。内部只调用统一入口。
        /// </summary>
        public static void RefreshTargetDynamicConsciousness(Pawn? target)
        {
            DynamicConsciousnessBonusUtility.RefreshForPawn(target);
        }

        /// <summary>
        ///     扫描全部已加载地图中的阵列，清理与该目标建立的连接。
        ///     用于人类接口被移除、失去机械师资格、转为非玩家阵营等永久失效场景。
        /// </summary>
        public static void ClearAllArrayTargetsFor(Pawn? target)
        {
            if (target == null)
            {
                return;
            }

            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (map == null)
                {
                    continue;
                }

                List<Building> buildings =
                    map.listerBuildings.AllBuildingsColonistOfDef(
                        MAPMechanitor_ThingDefOf.MAP_ParallelThoughtArray);
                if (buildings == null)
                {
                    continue;
                }

                // 先创建快照，避免在遍历中修改建筑目标导致列表结构变化。
                List<Building> snapshot =
                    new List<Building>(buildings);
                for (int j = 0; j < snapshot.Count; j++)
                {
                    CompParallelThoughtArray? comp =
                        snapshot[j].TryGetComp<CompParallelThoughtArray>();
                    comp?.ClearTargetFromExternalInvalidation(target);
                }
            }
        }
    }
}
