using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_GroupAntigravity : CompProperties
    {
        public MechanicalFlightProfileDef? profile;

        public CompProperties_GroupAntigravity()
        {
            compClass = typeof(CompGroupAntigravity);
        }
    }

    /// <summary>仅声明群体反重力能力，不授予永久自主飞行资格。</summary>
    public sealed class CompGroupAntigravity : ThingComp
    {
        public MechanicalFlightProfileDef? Profile =>
            ((CompProperties_GroupAntigravity)props).profile;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
                yield return gizmo;
            if (parent is Pawn pawn && pawn.Faction == Faction.OfPlayer)
                yield return GroupFlightUtility.MakeCommand(pawn, Profile);
        }
    }
}
