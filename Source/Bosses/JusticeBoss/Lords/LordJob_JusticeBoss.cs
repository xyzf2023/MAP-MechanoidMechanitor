using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor
{
    public class LordJob_JusticeBoss : LordJob
    {
        private Faction? faction;

        private IntVec3 defendPoint = IntVec3.Invalid;

        private float defendRadius = 18f;

        private float wanderRadius = 10f;

        public LordJob_JusticeBoss()
        {
        }

        public LordJob_JusticeBoss(Faction? faction, IntVec3 defendPoint)
        {
            this.faction = faction;
            this.defendPoint = defendPoint;
        }

        public override StateGraph CreateGraph()
        {
            StateGraph graph = new StateGraph();
            LordToil_DefendPoint defend = new LordToil_DefendPoint(
                defendPoint,
                defendRadius,
                wanderRadius);
            graph.StartingToil = defend;

            LordToil_ExitMap exit = new LordToil_ExitMap(
                LocomotionUrgency.Jog,
                canDig: false,
                interruptCurrentJob: true);
            exit.useAvoidGrid = true;
            graph.AddToil(exit);

            Transition retreat = new Transition(defend, exit);
            retreat.AddTrigger(new Trigger_Memo(JusticeBossCallUtility.RetreatMemo));
            retreat.AddPreAction(
                new TransitionAction_Message(
                    "MAP_MechanoidMechanitor.JusticeBoss.Message.Retreating".Translate(),
                    MessageTypeDefOf.NeutralEvent));
            graph.AddTransition(retreat);

            return graph;
        }

        public override void Notify_PawnLost(Pawn pawn, PawnLostCondition condition)
        {
            base.Notify_PawnLost(pawn, condition);
            if (!JusticePawnUtility.IsBossJustice(pawn))
            {
                return;
            }

            if (condition == PawnLostCondition.ExitedMap)
            {
                foreach (Quest quest in Find.QuestManager.QuestsListForReading)
                {
                    if (quest.State != QuestState.Ongoing)
                    {
                        continue;
                    }

                    foreach (QuestPart part in quest.PartsListForReading)
                    {
                        if (part is QuestPart_JusticeBossGroup justicePart
                            && justicePart.bosses.Contains(pawn))
                        {
                            justicePart.NotifyJusticeExitedMap(pawn);
                            return;
                        }
                    }
                }

                GameComponent_JusticeBossCallTracker.Current?.Clear();
            }
            else if (condition == PawnLostCondition.Killed
                || condition == PawnLostCondition.ChangedFaction
                || condition == PawnLostCondition.Vanished)
            {
                GameComponent_JusticeBossCallTracker.Current?.Clear();
            }
        }

        public override void ExposeData()
        {
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref defendPoint, "defendPoint");
            Scribe_Values.Look(ref defendRadius, "defendRadius", 18f);
            Scribe_Values.Look(ref wanderRadius, "wanderRadius", 10f);
        }
    }
}