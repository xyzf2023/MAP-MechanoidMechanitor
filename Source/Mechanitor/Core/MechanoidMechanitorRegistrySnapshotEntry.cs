using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械族机械师注册表持久化记录的只读快照项，仅供调试窗口显示。
    /// </summary>
    public sealed class MechanoidMechanitorRegistrySnapshotEntry
    {
        public Pawn Pawn { get; }

        public MechanoidMechanitorOrigin Origin { get; }

        public bool IsMechanicalConsciousnessHost { get; }

        public MechanoidMechanitorRegistrySnapshotEntry(
            Pawn pawn,
            MechanoidMechanitorOrigin origin,
            bool isMechanicalConsciousnessHost)
        {
            Pawn = pawn;
            Origin = origin;
            IsMechanicalConsciousnessHost = isMechanicalConsciousnessHost;
        }
    }
}
