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
            return JusticeBossCallUtility.CanCall(map, caller);
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
                Log.Error("[MAP-机械族机械师] 正义 BOSS： 缺少正义 BOSS 呼叫跟踪组件 GameComponent_JusticeBossCallTracker。");
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
                    "[MAP-机械族机械师] 正义 BOSS： BossgroupDef "
                    + def.defName
                    + " 的 rewardDef 为空，已中止呼叫。");
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
                Log.Error("[MAP-机械族机械师] 正义 BOSS： 任务生成失败：" + e);
                tracker.Clear();
                return;
            }

            if (quest == null)
            {
                tracker.Clear();
                return;
            }

            tracker.UpdatePendingQuestId(quest.id);
            // 成功创建任务后才登记共享 CD；生成失败不消耗召唤冷却。
            Find.BossgroupManager.Notify_BossgroupCalled(def);
        }
    }
}
