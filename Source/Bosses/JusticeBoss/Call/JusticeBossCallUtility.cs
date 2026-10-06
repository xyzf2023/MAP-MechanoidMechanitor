using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class JusticeBossCallUtility
    {
        // 与原版 Bossgroup 抵达等待一致：最短约 2500～5000，最长约 60000～180000。
        public static readonly IntRange ArrivalMinDelayTicksRange = new IntRange(2500, 5000);

        public static readonly IntRange ArrivalMaxDelayTicksRange = new IntRange(60000, 180000);

        public const string RetreatMemo = "MAP_JusticeBossRetreat";

        // 只复用原版的全局 CD 与待抵达检查，不调用其任务生成逻辑。
        private static readonly BossgroupWorker VanillaCallChecks = new BossgroupWorker();

        public static AcceptanceReport CanCall(Map? map, Pawn? caller = null)
        {
            if (map == null || map.Parent == null)
            {
                return false;
            }

            if (!map.Parent.HasMap)
            {
                return false;
            }

            GameComponent_JusticeBossCallTracker? tracker =
                GameComponent_JusticeBossCallTracker.Current;
            if (tracker != null && tracker.HasActiveCall)
            {
                return "MAP_MechanoidMechanitor.JusticeBoss.Call.DisabledAlreadyCalled".Translate();
            }

            // 原版 CanResolve 不使用 caller，直接呼叫入口也可复用同一检查。
            return VanillaCallChecks.CanResolve(caller!);
        }

        public static bool TryCall(Map map)
        {
            AcceptanceReport canCall = CanCall(map);
            if (!canCall.Accepted)
            {
                if (!canCall.Reason.NullOrEmpty())
                {
                    Messages.Message(
                        canCall.Reason,
                        MessageTypeDefOf.RejectInput,
                        historical: false);
                }

                return false;
            }

            BossgroupDef? def = MAP_JusticeBossDefOf.MAP_JusticeBossGroup;
            if (def?.Worker == null)
            {
                Log.Error("[MAP-机械族机械师] 正义 BOSS： 缺少 BOSS 编组定义或执行器。");
                return false;
            }

            def.Worker.Resolve(map, 0);
            return true;
        }
    }
}
