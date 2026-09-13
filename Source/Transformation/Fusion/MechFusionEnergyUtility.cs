using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体能源的唯一读写入口。能源只在权威合体记录中保存；
    /// 这里只负责合体开始时的快照读取与解除时的最终写回，
    /// 飞行与地面消耗在第四轮统一接入。
    /// </summary>
    public static class MechFusionEnergyUtility
    {
        internal static void CaptureInitialEnergy(
            MechFusionSession session,
            Pawn sourcePawn)
        {
            Need_MechEnergy? energy = sourcePawn?.needs?.energy;
            float fallbackMax = sourcePawn?.RaceProps?.maxMechEnergy ?? 100f;
            float max = energy?.MaxLevel ?? fallbackMax;
            float current = energy?.CurLevel ?? max;
            session.SetEnergy(current, max);
        }

        internal static void WriteBackToSource(
            MechFusionSession session,
            Pawn? sourcePawn)
        {
            Need_MechEnergy? energy = sourcePawn?.needs?.energy;
            if (energy == null || session.MaxEnergy <= 0f)
            {
                return;
            }

            energy.CurLevel = session.CurrentEnergy;
        }
    }
}
