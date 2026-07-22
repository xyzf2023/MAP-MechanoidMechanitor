using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompUseEffect_CallJusticeBossgroup : CompUseEffect_CallBossgroup
    {
        public override TaggedString ConfirmMessage(Pawn p)
        {
            return "MAP_MechanoidMechanitor.JusticeBoss.Call.ConfirmMessage".Translate();
        }
    }
}
