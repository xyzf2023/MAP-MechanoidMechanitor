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
        public const int MaxBoostPercent = 300;
        public const int BoostStepPercent = 25;
        public const float BasePowerConsumption = 1600f;
        public const float PowerPerBoostStep = 400f;
        public const float IdlePowerConsumption = 100f;
        public const int FallbackRefreshIntervalTicks = 60;

        /// <summary>
        ///     将任意档位限制到 100~300，并对齐到 25% 离散档位。
        ///     存档出现异常数值时也必须自动规范化。
        /// </summary>
        public static int ClampBoostPercent(int percent)
        {
            int clamped = percent < MinBoostPercent ? MinBoostPercent : (percent > MaxBoostPercent ? MaxBoostPercent : percent);
            int step = BoostStepPercent;
            int aligned = (int)System.Math.Round((double)clamped / step) * step;
            return aligned < MinBoostPercent ? MinBoostPercent : (aligned > MaxBoostPercent ? MaxBoostPercent : aligned);
        }

        /// <summary>
        ///     增幅百分数 -> 意识偏移（100% => 1f）。
        /// </summary>
        public static float BoostPercentToConsciousnessOffset(int boostPercent)
        {
            return ClampBoostPercent(boostPercent) / 100f;
        }

        /// <summary>
        ///     增幅百分数 -> 请求耗电（W）。
        ///     100%=>1600, 125%=>2000, ... 300%=>4800。
        /// </summary>
        public static float GetPowerConsumptionForBoost(int boostPercent)
        {
            return BasePowerConsumption
                   + ((ClampBoostPercent(boostPercent) - MinBoostPercent) / BoostStepPercent)
                   * PowerPerBoostStep;
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
        ///     不设叠加上限。
        /// </summary>
        public static int GetTotalActiveBoostPercent(Pawn? target)
        {
            if (target == null)
            {
                return 0;
            }

            int total = 0;
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
                    }
                }
            }

            return total;
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
