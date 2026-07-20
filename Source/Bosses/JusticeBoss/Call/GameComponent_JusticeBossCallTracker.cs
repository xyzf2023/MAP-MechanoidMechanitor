using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public enum JusticeBossCallState
    {
        None = 0,
        Pending = 1,
        Active = 2,
        Retreating = 3,
    }

    public sealed class GameComponent_JusticeBossCallTracker : GameComponent
    {
        private JusticeBossCallState state = JusticeBossCallState.None;

        private MapParent? targetMapParent;

        private Pawn? justicePawn;

        private int questId = -1;

        public JusticeBossCallState State => state;

        public MapParent? TargetMapParent => targetMapParent;

        public Pawn? JusticePawn => justicePawn;

        public bool HasActiveCall =>
            state == JusticeBossCallState.Pending
            || state == JusticeBossCallState.Active
            || state == JusticeBossCallState.Retreating;

        public GameComponent_JusticeBossCallTracker(Game game)
        {
        }

        public static GameComponent_JusticeBossCallTracker? Current =>
            Verse.Current.Game?.GetComponent<GameComponent_JusticeBossCallTracker>();

        public void BeginPending(MapParent mapParent)
        {
            Clear();
            state = JusticeBossCallState.Pending;
            targetMapParent = mapParent;
            questId = -1;
        }

        public void UpdatePendingQuestId(int newQuestId)
        {
            if (state != JusticeBossCallState.Pending)
            {
                return;
            }

            questId = newQuestId;
        }

        public void SetJusticePawn(Pawn? pawn)
        {
            justicePawn = pawn;
        }

        public void MarkActive()
        {
            if (state == JusticeBossCallState.Pending || state == JusticeBossCallState.Active)
            {
                state = JusticeBossCallState.Active;
            }
        }

        public void MarkRetreating()
        {
            if (state == JusticeBossCallState.Active || state == JusticeBossCallState.Retreating)
            {
                state = JusticeBossCallState.Retreating;
            }
        }

        public void Clear()
        {
            state = JusticeBossCallState.None;
            targetMapParent = null;
            justicePawn = null;
            questId = -1;
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            ValidateOrClear();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref state, "justiceBossCallState", JusticeBossCallState.None);
            Scribe_References.Look(ref targetMapParent, "justiceBossTargetMapParent");
            Scribe_References.Look(ref justicePawn, "justiceBossPawn");
            Scribe_Values.Look(ref questId, "justiceBossQuestId", -1);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                ValidateOrClear();
            }
        }

        public void ValidateOrClear()
        {
            if (state == JusticeBossCallState.None)
            {
                Clear();
                return;
            }

            if (targetMapParent == null || targetMapParent.Destroyed || !targetMapParent.HasMap)
            {
                Clear();
                return;
            }

            if (questId >= 0)
            {
                Quest? quest = Find.QuestManager.QuestsListForReading.Find(q => q.id == questId);
                if (quest == null
                    || quest.State == QuestState.EndedSuccess
                    || quest.State == QuestState.EndedFailed
                    || quest.State == QuestState.EndedUnknownOutcome)
                {
                    Clear();
                    return;
                }
            }

            if (state == JusticeBossCallState.Pending)
            {
                if (justicePawn != null && (justicePawn.Destroyed || justicePawn.Dead))
                {
                    Clear();
                }

                return;
            }

            if (justicePawn == null || justicePawn.Destroyed || justicePawn.Dead)
            {
                Clear();
                return;
            }

            bool onMap = justicePawn.Spawned && justicePawn.Map != null;
            bool inWorld = Find.WorldPawns.Contains(justicePawn);
            bool inTransporter = justicePawn.ParentHolder is IThingHolder;
            if (!onMap && !inWorld && !inTransporter && justicePawn.MapHeld == null)
            {
                Clear();
            }
        }
    }
}