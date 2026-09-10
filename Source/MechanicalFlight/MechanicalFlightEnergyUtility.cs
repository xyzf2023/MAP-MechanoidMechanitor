using UnityEngine;
using Verse;
using RimWorld;

namespace MAP_MechanoidMechanitor
{
    public interface IMechanicalFlightEnergyProvider
    {
        bool TryGetEnergyFraction(Pawn pawn, out float fraction);
        bool TryConsumeMaximumEnergyFraction(Pawn pawn, float fraction);
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
    }

    public static class MechanicalFlightEnergyUtility
    {
        private static readonly IMechanicalFlightEnergyProvider DefaultProvider =
            new PawnMechEnergyProvider();

        public static bool TryGetEnergyFraction(Pawn? pawn, out float fraction)
        {
            fraction = 0f;
            return pawn != null && DefaultProvider.TryGetEnergyFraction(pawn, out fraction);
        }

        public static bool TryConsumeMaximumEnergyFraction(Pawn? pawn, float fraction)
        {
            return pawn != null
                && DefaultProvider.TryConsumeMaximumEnergyFraction(pawn, fraction);
        }
    }
}
