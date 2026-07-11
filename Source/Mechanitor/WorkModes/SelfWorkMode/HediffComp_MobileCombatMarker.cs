using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class HediffCompProperties_MobileCombatMarker : HediffCompProperties
    {
        public HediffCompProperties_MobileCombatMarker()
        {
            compClass = typeof(HediffComp_MobileCombatMarker);
        }
    }

    public class HediffComp_MobileCombatMarker : HediffComp
    {
        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            base.CompPostPostAdd(dinfo);
            MechanoidMechanitorWorkModeUtility.SetMobileCombatFlag(Pawn, true);
        }

        public override void CompPostPostRemoved()
        {
            base.CompPostPostRemoved();
            MechanoidMechanitorWorkModeUtility.SetMobileCombatFlag(Pawn, false);
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                MechanoidMechanitorWorkModeUtility.SetMobileCombatFlag(Pawn, true);
            }
        }
    }
}
