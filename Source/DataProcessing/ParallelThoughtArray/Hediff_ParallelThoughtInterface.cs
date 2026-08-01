using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    ///     并行思维接口健康状态。
    ///     同时承担“资格标记”和“阵列意识增幅载体”。
    ///     接口本身不提供固定意识加成，只有正在为该人类供能的并行思维阵列才会产生增幅。
    /// </summary>
    public class Hediff_ParallelThoughtInterface : Hediff_DynamicConsciousnessBonusBase
    {
        protected override float CalculateConsciousnessOffset()
        {
            if (pawn == null
                || !pawn.RaceProps.Humanlike
                || pawn.RaceProps.IsMechanoid)
            {
                return 0f;
            }

            return ParallelThoughtArrayUtility
                .GetTotalActiveConsciousnessOffset(pawn);
        }

        protected override int GetStageVariantKey()
        {
            if (pawn == null
                || !pawn.RaceProps.Humanlike
                || pawn.RaceProps.IsMechanoid)
            {
                return 0;
            }

            return ParallelThoughtArrayUtility
                .GetTotalActiveBoostPercent(pawn);
        }

        // 接口移除后，该 Pawn 已不再具备意识分配资格。
        // 清除其全部实际分配、相关正负 Hediff、特化记录与动态配置。
        public override void PostRemoved()
        {
            Pawn? localPawn = pawn;

            base.PostRemoved();

            if (localPawn == null
                || localPawn.Destroyed)
            {
                return;
            }

            GameComponent_DataProcessingAllocationRegistry
                .CurrentRegistry?
                .ClearOverseer(localPawn);

            ParallelThoughtArrayUtility
                .ClearAllArrayTargetsFor(localPawn);

            ParallelThoughtArrayUtility
                .RefreshTargetDynamicConsciousness(
                    localPawn);
        }
    }
}
