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

            // 正向遍历且不在此修改列表，避免遍历中改集合；各实例内部仅在结果变化时通知健康系统。
            for (int i = 0; i < hediffSet.hediffs.Count; i++)
            {
                if (hediffSet.hediffs[i] is Hediff_DynamicConsciousnessBonusBase dynamicBonus)
                {
                    dynamicBonus.RefreshDynamicEffects();
                }
            }
        }
    }
}
