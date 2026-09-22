using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    ///     集中判断一名 Pawn 是否能成为数据处理分配系统的监管者，
    ///     以及是否能成为并行思维阵列的增幅目标。
    /// </summary>
    public static class DataProcessingAllocatorEligibilityUtility
    {
        /// <summary>
        ///     机械族机械师（原版/后天身份均可），且为玩家安全阵营的存活机械师。
        /// </summary>
        public static bool IsMechanoidDataProcessingOverseer(Pawn? pawn)
        {
            return pawn != null
                   && !pawn.Dead
                   && !pawn.Destroyed
                   && pawn.mechanitor != null
                   && MechanoidMechanitorCapabilityUtility.HasCapability(pawn, MechanoidMechanitorCapability.SelfDataProcessing)
                   && pawn.Faction != null
                   && pawn.Faction.IsPlayerSafe();
        }

        /// <summary>
        ///     安装了并行思维接口的人类机械师（玩家安全阵营、存活）。
        ///     注意：本方法不对机械族返回 true。
        /// </summary>
        public static bool IsHumanInterfaceMechanitor(Pawn? pawn)
        {
            return ModsConfig.BiotechActive
                   && pawn != null
                   && !pawn.Dead
                   && !pawn.Destroyed
                   && pawn.RaceProps.Humanlike
                   && !pawn.RaceProps.IsMechanoid
                   && pawn.mechanitor != null
                   && pawn.Faction != null
                   && pawn.Faction.IsPlayerSafe()
                   && MechanoidMechanitorCapabilityUtility.HasCapability(pawn, MechanoidMechanitorCapability.DataProcessing);
        }

        /// <summary>
        ///     机械族机械师，或安装了并行思维接口的人类机械师。
        /// </summary>
        public static bool IsEligibleDataProcessingOverseer(Pawn? pawn)
        {
            return IsMechanoidDataProcessingOverseer(pawn)
                   || IsHumanInterfaceMechanitor(pawn);
        }

        /// <summary>
        ///     与数据处理监管者相同的资格判断，用于并行思维阵列候选目标筛选。
        /// </summary>
        public static bool IsEligibleParallelThoughtArrayTarget(Pawn? pawn)
        {
            return IsEligibleDataProcessingOverseer(pawn);
        }

        /// <summary>
        ///     是否允许将该 Pawn 自身作为分配目标（自我指令聚焦）。
        ///     只对机械族机械师返回 true；人类接口机械师始终返回 false。
        /// </summary>
        public static bool CanUseSelfAllocation(Pawn? pawn)
        {
            return IsMechanoidDataProcessingOverseer(pawn);
        }
    }
}
