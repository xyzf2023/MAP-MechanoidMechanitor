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
            return ParallelThoughtArrayUtility.GetTotalActiveConsciousnessOffset(pawn);
        }

        protected override int GetStageVariantKey()
        {
            return ParallelThoughtArrayUtility.GetTotalActiveBoostPercent(pawn);
        }

        public override void PostRemoved()
        {
            Pawn? localPawn = pawn;
            base.PostRemoved();

            if (localPawn == null || localPawn.Destroyed)
            {
                return;
            }

            // 清理实际分配（保留配置），移除负向数据流分发与各目标正向指令聚焦。
            GameComponent_DataProcessingAllocationRegistry.CurrentRegistry?.ClearOverseer(localPawn);

            // 解除所有阵列对该 Pawn 的连接（建筑回到待机，并触发安全回收）。
            ParallelThoughtArrayUtility.ClearAllArrayTargetsFor(localPawn);

            // 刷新 Pawn 动态意识，使阵列加成从本 Hediff 中移除。
            ParallelThoughtArrayUtility.RefreshTargetDynamicConsciousness(localPawn);
        }
    }
}
