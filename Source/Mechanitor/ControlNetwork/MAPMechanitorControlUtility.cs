using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>监管能力的运行门控。接管与首次监管共享原版控制任务，不另设中继身份。</summary>
    public static class MAPMechanitorControlUtility
    {
        public static bool CanUseControlSystem(Pawn? pawn) =>
            ModsConfig.BiotechActive && pawn != null
            && pawn.Faction != null && pawn.Faction.IsPlayerSafe()
            && MechanoidMechanitorCapabilityUtility.HasCapability(
                pawn, MechanoidMechanitorCapability.MechanitorControl);

        /// <summary>沿实际上级方向检查祖先，拒绝直接及多级循环；不读取有歧义的关系顺序。</summary>
        public static bool WouldCreateControlCycle(Pawn controller, Pawn subject)
        {
            HashSet<Pawn> visited = new HashSet<Pawn>();
            Pawn? current = controller;
            while (current != null && visited.Add(current))
            {
                if (current == subject)
                    return true;
                current = MAPOverseerRelationDirectionUtility.FindActualOverseer(current);
            }
            return current != null;
        }
    }
}
