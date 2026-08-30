using System.Collections.Generic;
using MAP_MechanoidMechanitor.Scenarios;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 原版 Anomaly DLC 腐化复制方尖碑（CompObelisk_Duplicator）兼容辅助。
    /// 当机械族机械师剧本启用时，阻止机械族机械师被自动选为敌对复制目标。
    /// </summary>
    internal static class MechanoidMechanitorScenarioAnomalyDuplicationUtility
    {
        /// <summary>
        /// 从腐化复制方尖碑的候选列表中就地移除机械族机械师。
        /// candidates 即原版 CompObelisk_Duplicator.CompTick 本次使用的同一个静态候选列表；
        /// 在 RandomElement 与 TryDuplicatePawn 之前调用，因此过滤后为空时原版会自然跳过本次复制。
        /// </summary>
        public static void FilterCorruptedObeliskCandidates(List<Pawn> candidates)
        {
            if (candidates == null)
            {
                return;
            }

            if (!GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
            {
                return;
            }

            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                Pawn pawn = candidates[i];
                if (pawn != null
                    && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
                {
                    candidates.RemoveAt(i);
                }
            }
        }
    }
}
