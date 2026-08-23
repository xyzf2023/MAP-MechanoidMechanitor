using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class HediffComp_ProxySubchain : HediffComp
    {
        public override void CompPostPostRemoved()
        {
            base.CompPostPostRemoved();

            Pawn? pawn = parent?.pawn;
            if (pawn?.mechanitor != null && !ProxySubchainUtility.HasImplant(pawn))
            {
                pawn.mechanitor.UndraftAllMechs();
            }
        }
    }

    public sealed class HediffCompProperties_ProxySubchain : HediffCompProperties
    {
        public HediffCompProperties_ProxySubchain()
        {
            compClass = typeof(HediffComp_ProxySubchain);
        }
    }
}
