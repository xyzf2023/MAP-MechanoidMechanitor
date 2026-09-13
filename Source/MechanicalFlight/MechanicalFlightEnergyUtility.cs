using UnityEngine;
using Verse;
using RimWorld;

namespace MAP_MechanoidMechanitor
{
    public interface IMechanicalFlightEnergyProvider
    {
        bool TryGetEnergyFraction(Pawn pawn, out float fraction);
        bool TryConsumeMaximumEnergyFraction(Pawn pawn, float fraction);
        bool TrySetEnergyFraction(Pawn pawn, float fraction);
    }

    internal sealed class PawnMechEnergyProvider : IMechanicalFlightEnergyProvider
    {
        public bool TryGetEnergyFraction(Pawn pawn, out float fraction)
        {
            fraction = 0f;
            Need_MechEnergy? energy = pawn?.needs?.energy;
            if (energy == null)
            {
                return false;
            }

            fraction = energy.CurLevelPercentage;
            return true;
        }

        public bool TryConsumeMaximumEnergyFraction(Pawn pawn, float fraction)
        {
            Need_MechEnergy? energy = pawn?.needs?.energy;
            if (energy == null)
            {
                return false;
            }

            energy.CurLevelPercentage = Mathf.Max(0f,
                energy.CurLevelPercentage - Mathf.Max(0f, fraction));
            return true;
        }

        public bool TrySetEnergyFraction(Pawn pawn, float fraction)
        {
            Need_MechEnergy? energy = pawn?.needs?.energy;
            if (energy == null)
            {
                return false;
            }

            energy.CurLevelPercentage = Mathf.Clamp01(fraction);
            return true;
        }
    }

    /// <summary>
    /// 飞行能源的统一入口。普通机械族读取自身 Need_MechEnergy；
    /// 正在合体的人类读取权威合体记录；调用者不再各自判断是否合体。
    /// </summary>
    public static class MechanicalFlightEnergyUtility
    {
        private static readonly IMechanicalFlightEnergyProvider DefaultProvider =
            new PawnMechEnergyProvider();

        public static bool TryGetEnergyFraction(Pawn? pawn, out float fraction)
        {
            fraction = 0f;
            return pawn != null
                && GetProvider(pawn).TryGetEnergyFraction(pawn, out fraction);
        }

        public static bool TryConsumeMaximumEnergyFraction(Pawn? pawn, float fraction)
        {
            return pawn != null
                && GetProvider(pawn).TryConsumeMaximumEnergyFraction(pawn, fraction);
        }

        public static bool TrySetEnergyFraction(Pawn? pawn, float fraction)
        {
            return pawn != null
                && GetProvider(pawn).TrySetEnergyFraction(pawn, fraction);
        }

        private static IMechanicalFlightEnergyProvider GetProvider(Pawn pawn)
        {
            return MechFusionEnergyUtility.IsActiveFusionWearer(pawn)
                ? MechFusionEnergyUtility.FusionFlightEnergyProvider
                : DefaultProvider;
        }
    }
}
