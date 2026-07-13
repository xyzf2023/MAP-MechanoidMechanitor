using System;
using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 集中计算动态意识加成。Hediff 不得各自查询科研 Def 或统计机械师数量。
    /// </summary>
    public static class DynamicConsciousnessBonusCalculationUtility
    {
        private const int DefaultStageVariantKey = 0;
        private const int DataStreamReorganizationStageVariantKey = 1;
        private const int ParallelThoughtMatrixStageVariantKeyBase = 1000;

        /// <summary>
        /// 当前有效注册机械族机械师数量。仅遍历注册表列表，排除 null / Destroyed / Dead。
        /// </summary>
        public static int GetActiveRegisteredMechanitorCount()
        {
            IReadOnlyList<Pawn> registered =
                GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors;
            int count = 0;
            for (int i = 0; i < registered.Count; i++)
            {
                Pawn? pawn = registered[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead)
                {
                    continue;
                }

                count++;
            }

            return count;
        }

        /// <summary>
        /// 机械族机械师身份健康状态（先天/后天）的意识偏移。
        /// </summary>
        public static float GetMechanitorIdentityConsciousnessOffset()
        {
            if (ResearchFeatureUnlockUtility.IsParallelThoughtMatrixUnlocked())
            {
                int n = Math.Max(1, GetActiveRegisteredMechanitorCount());
                return 2.5f + 0.5f * n;
            }

            if (ResearchFeatureUnlockUtility.IsDataStreamReorganizationUnlocked())
            {
                return 2f;
            }

            return 1f;
        }

        /// <summary>
        /// 机械意识健康状态的意识偏移。
        /// </summary>
        public static float GetMechanicalConsciousnessOffset()
        {
            if (ResearchFeatureUnlockUtility.IsParallelThoughtMatrixUnlocked())
            {
                int n = Math.Max(1, GetActiveRegisteredMechanitorCount());
                return 2.5f + 1f * n;
            }

            if (ResearchFeatureUnlockUtility.IsDataStreamReorganizationUnlocked())
            {
                return 2f;
            }

            return 1f;
        }

        /// <summary>
        /// 阶段变体键：科研阶段变化，或并行阶段下 n 变化时用于使缓存阶段失效。
        /// </summary>
        public static int GetConsciousnessStageVariantKey()
        {
            if (ResearchFeatureUnlockUtility.IsParallelThoughtMatrixUnlocked())
            {
                int n = Math.Max(1, GetActiveRegisteredMechanitorCount());
                return ParallelThoughtMatrixStageVariantKeyBase + n;
            }

            if (ResearchFeatureUnlockUtility.IsDataStreamReorganizationUnlocked())
            {
                return DataStreamReorganizationStageVariantKey;
            }

            return DefaultStageVariantKey;
        }
    }
}
