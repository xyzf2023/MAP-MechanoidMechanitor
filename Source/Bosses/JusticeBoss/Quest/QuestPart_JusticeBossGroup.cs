using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor
{
    public class QuestPart_JusticeBossGroup : QuestPart_MakeLord
    {
        public List<Pawn> bosses = new List<Pawn>();

        public IntVec3 stageLocation = IntVec3.Invalid;

        protected override Lord MakeLord()
        {
            IntVec3 defendPoint = stageLocation;
            Pawn? boss = bosses.Find(p => p != null && !p.Destroyed);
            if (boss != null && boss.Spawned)
            {
                defendPoint = boss.Position;
            }

            if (!defendPoint.IsValid && Map != null)
            {
                defendPoint = Map.Center;
            }

            GameComponent_JusticeBossCallTracker? tracker =
                GameComponent_JusticeBossCallTracker.Current;
            tracker?.MarkActive();
            if (boss != null)
            {
                tracker?.SetJusticePawn(boss);
            }

            LordJob_JusticeBoss lordJob = new LordJob_JusticeBoss(faction, defendPoint);
            return LordMaker.MakeNewLord(faction, lordJob, Map);
        }

        public override void Notify_PawnKilled(Pawn pawn, DamageInfo? dinfo)
        {
            base.Notify_PawnKilled(pawn, dinfo);
            if (!bosses.Contains(pawn))
            {
                return;
            }

            bosses.Remove(pawn);
            Messages.Message(
                "MAP_MechanoidMechanitor.JusticeBoss.Message.Defeated".Translate(),
                pawn,
                MessageTypeDefOf.PositiveEvent);
            EndEvent();
        }

        public override void Notify_PawnDiscarded(Pawn pawn)
        {
            base.Notify_PawnDiscarded(pawn);
            if (!bosses.Contains(pawn))
            {
                return;
            }

            bosses.Remove(pawn);
            GameComponent_JusticeBossCallTracker? tracker =
                GameComponent_JusticeBossCallTracker.Current;
            if (tracker != null && tracker.State == JusticeBossCallState.Retreating)
            {
                Messages.Message(
                    "MAP_MechanoidMechanitor.JusticeBoss.Message.Exited".Translate(),
                    MessageTypeDefOf.NeutralEvent);
            }
            else
            {
                Log.Warning(
                    "[MAP JusticeBoss] Justice left map unexpectedly while state="
                    + (tracker?.State.ToString() ?? "null"));
            }

            EndEvent();
        }

        public void NotifyJusticeExitedMap(Pawn pawn)
        {
            if (!bosses.Contains(pawn))
            {
                return;
            }

            bosses.Remove(pawn);
            Messages.Message(
                "MAP_MechanoidMechanitor.JusticeBoss.Message.Exited".Translate(),
                MessageTypeDefOf.NeutralEvent);
            EndEvent();
        }

        private void EndEvent()
        {
            GameComponent_JusticeBossCallTracker.Current?.Clear();
            if (quest != null && quest.State == QuestState.Ongoing)
            {
                quest.End(QuestEndOutcome.Unknown);
            }
        }

        public override void Cleanup()
        {
            base.Cleanup();
            GameComponent_JusticeBossCallTracker? tracker =
                GameComponent_JusticeBossCallTracker.Current;
            if (tracker != null
                && tracker.HasActiveCall
                && (justiceStillOurs(tracker)))
            {
                tracker.Clear();
            }
        }

        private bool justiceStillOurs(GameComponent_JusticeBossCallTracker tracker)
        {
            if (tracker.JusticePawn == null)
            {
                return true;
            }

            return bosses.Contains(tracker.JusticePawn);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref stageLocation, "stageLocation");
            Scribe_Collections.Look(ref bosses, "bosses", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                bosses ??= new List<Pawn>();
                bosses.RemoveAll(p => p == null);
            }
        }
    }
}