using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class BossgroupWorker_Justice : BossgroupWorker
    {
        public override AcceptanceReport CanResolve(Pawn caller)
        {
            Map? map = caller?.Map ?? Find.CurrentMap;
            return JusticeBossCallUtility.CanCall(map);
        }

        public override AcceptanceReport ShouldSummonNow(Map map)
        {
            return base.ShouldSummonNow(map);
        }

        public override void Resolve(Map map, int wave)
        {
            GameComponent_JusticeBossCallTracker? tracker =
                GameComponent_JusticeBossCallTracker.Current;
            if (tracker == null)
            {
                Log.Error("[MAP JusticeBoss] Missing GameComponent_JusticeBossCallTracker.");
                return;
            }

            if (tracker.HasActiveCall)
            {
                return;
            }

            AcceptanceReport canCall = JusticeBossCallUtility.CanCall(map);
            if (!canCall.Accepted)
            {
                return;
            }

            if (def.rewardDef == null)
            {
                Log.Error(
                    "[MAP JusticeBoss] BossgroupDef "
                    + def.defName
                    + " has null rewardDef; aborting call.");
                return;
            }

            tracker.BeginPending(map.Parent);

            Slate slate = new Slate();
            slate.Set("bossgroup", def);
            slate.Set("map", map);
            slate.Set("reward", def.rewardDef);
            slate.Set("wave", 0);
            slate.Set("bossKind", def.boss.kindDef);

            Quest? quest = null;
            try
            {
                quest = QuestUtility.GenerateQuestAndMakeAvailable(def.quest, slate);
            }
            catch (System.Exception e)
            {
                Log.Error("[MAP JusticeBoss] Failed to generate quest: " + e);
                tracker.Clear();
                return;
            }

            if (quest == null)
            {
                tracker.Clear();
                return;
            }

            tracker.UpdatePendingQuestId(quest.id);
        }
    }
}
