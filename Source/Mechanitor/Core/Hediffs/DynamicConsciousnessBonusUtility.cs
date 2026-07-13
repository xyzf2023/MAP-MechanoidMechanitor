using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// Pawn 级动态意识加成统一刷新入口。
    /// 条件变化时只需调用本入口，无需分别查找三个身份 HediffDef。
    /// </summary>
    public static class DynamicConsciousnessBonusUtility
    {
        public static void RefreshForPawn(Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return;
            }

            HediffSet? hediffSet = pawn.health?.hediffSet;
            if (hediffSet?.hediffs == null)
            {
                return;
            }

            // 第一阶段：先建立本地快照，此阶段不调用 RefreshDynamicEffects，避免通知健康系统。
            List<Hediff_DynamicConsciousnessBonusBase> snapshot = new List<Hediff_DynamicConsciousnessBonusBase>();
            List<Hediff> hediffs = hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i] is Hediff_DynamicConsciousnessBonusBase dynamicBonus)
                {
                    snapshot.Add(dynamicBonus);
                }
            }

            // 第二阶段：遍历快照执行刷新。Notify_HediffChanged 可能触发倒地、死亡等，
            // 进而改变 hediff 集合，因此不能在通知过程中继续依赖原始列表的稳定性。
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (pawn.Destroyed || pawn.Dead)
                {
                    return;
                }

                hediffSet = pawn.health?.hediffSet;
                if (hediffSet?.hediffs == null)
                {
                    return;
                }

                Hediff_DynamicConsciousnessBonusBase dynamicBonus = snapshot[i];
                if (!hediffSet.hediffs.Contains(dynamicBonus))
                {
                    continue;
                }

                dynamicBonus.RefreshDynamicEffects();
            }
        }
    }
}
