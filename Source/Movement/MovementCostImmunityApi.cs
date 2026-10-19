using System;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// Optional, versioned movement-cost providers. No other MOD assembly is required.
    /// Register once after startup; queries must be side-effect-free and must not call this backend.
    /// Protocol 1 / semantics 1 preserves urgency, following, boosts and impassability.
    /// </summary>
    public static class MovementCostImmunityApi
    {
        public static bool IsActive => MovementCostImmunityCore.IsActive;

        public static bool TryRegisterProvider(int protocol, int semantics,
            string sourceId, Func<Pawn, bool> source)
        {
            return MovementCostImmunityCore.TryRegister(protocol, semantics, sourceId, source);
        }
    }
}
