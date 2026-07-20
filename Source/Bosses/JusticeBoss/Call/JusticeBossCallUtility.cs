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

        public static AcceptanceReport CanCall(Map? map)
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

            return true;
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
                Log.Error("[MAP JusticeBoss] BossgroupDef or Worker missing.");
                return false;
            }

            def.Worker.Resolve(map, 0);
            return true;
        }
    }
}