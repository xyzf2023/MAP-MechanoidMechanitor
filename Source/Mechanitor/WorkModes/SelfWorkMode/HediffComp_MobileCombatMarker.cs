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
        // 与“机体同调”的额外效果说明保持相同的列表格式。
        public override string CompTipStringExtra =>
            "MAP_MechanoidMechanitor.WorkMode.MobileCombat.MovementCostImmunity".Translate();

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
