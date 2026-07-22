using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompUseEffect_CallJusticeBossgroup : CompUseEffect_CallBossgroup
    {
        public override bool SelectedUseOption(Pawn p)
        {
            Find.WindowStack.Add(new Dialog_JusticeBossConfirmation(delegate
            {
                if (parent == null || parent.Destroyed || p == null || p.Destroyed)
                {
                    return;
                }

                CompUsable usable = parent.GetComp<CompUsable>();
                if (usable == null)
                {
                    return;
                }

                usable.TryStartUseJob(p, usable.GetExtraTarget(p), usable.Props.ignoreOtherReservations);
            }));
            return true;
        }

        public override TaggedString ConfirmMessage(Pawn p)
        {
            return null;
        }
    }
}
