using LudeonTK;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class DebugActions_JusticeBoss
    {
        private static CompJusticeBossController? FindControllerOnMap()
        {
            Map? map = Find.CurrentMap;
            if (map == null)
            {
                return null;
            }

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (!JusticePawnUtility.IsBossJustice(pawn))
                {
                    continue;
                }

                CompJusticeBossController? comp = pawn.TryGetComp<CompJusticeBossController>();
                if (comp != null)
                {
                    return comp;
                }
            }

            return null;
        }

        [DebugAction(
            "MAP-机械族机械师",
            "正义Boss：立即呼叫",
            false,
            false,
            false,
            false,
            false,
            0,
            false,
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Playing)]
        private static void DebugCallJusticeBoss()
        {
            Map? map = Find.CurrentMap;
            if (map == null)
            {
                Messages.Message("无当前地图。", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            GameComponent_JusticeBossCallTracker.Current?.Clear();
            if (!JusticeBossCallUtility.TryCall(map))
            {
                Messages.Message("呼叫失败。", MessageTypeDefOf.RejectInput, historical: false);
            }
        }

        [DebugAction(
            "MAP-机械族机械师",
            "正义Boss：立即部署设施",
            false,
            false,
            false,
            false,
            false,
            0,
            false,
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Playing)]
        private static void DebugDeployInfra()
        {
            CompJusticeBossController? comp = FindControllerOnMap();
            if (comp == null)
            {
                Messages.Message("地图上没有正义Boss。", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            comp.DebugForceDeployInfrastructure();
        }

        [DebugAction(
            "MAP-机械族机械师",
            "正义Boss：立即召唤下一波",
            false,
            false,
            false,
            false,
            false,
            0,
            false,
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Playing)]
        private static void DebugNextWave()
        {
            CompJusticeBossController? comp = FindControllerOnMap();
            if (comp == null)
            {
                Messages.Message("地图上没有正义Boss。", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            comp.DebugForceNextWave(forceBossReplace: false);
        }

        [DebugAction(
            "MAP-机械族机械师",
            "正义Boss：强制下一波Boss替换",
            false,
            false,
            false,
            false,
            false,
            0,
            false,
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Playing)]
        private static void DebugNextWaveBossReplace()
        {
            CompJusticeBossController? comp = FindControllerOnMap();
            if (comp == null)
            {
                Messages.Message("地图上没有正义Boss。", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            comp.DebugForceNextWave(forceBossReplace: true);
        }

        [DebugAction(
            "MAP-机械族机械师",
            "正义Boss：进入撤退等待",
            false,
            false,
            false,
            false,
            false,
            0,
            false,
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Playing)]
        private static void DebugRetreatWait()
        {
            CompJusticeBossController? comp = FindControllerOnMap();
            if (comp == null)
            {
                Messages.Message("地图上没有正义Boss。", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            comp.DebugForceRetreatWait();
        }

        [DebugAction(
            "MAP-机械族机械师",
            "正义Boss：立即下达撤退",
            false,
            false,
            false,
            false,
            false,
            0,
            false,
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Playing)]
        private static void DebugRetreatNow()
        {
            CompJusticeBossController? comp = FindControllerOnMap();
            if (comp == null)
            {
                Messages.Message("地图上没有正义Boss。", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            comp.DebugForceRetreatNow();
        }

        [DebugAction(
            "MAP-机械族机械师",
            "正义Boss：清除召唤锁",
            false,
            false,
            false,
            false,
            false,
            0,
            false,
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Playing)]
        private static void DebugClearLock()
        {
            GameComponent_JusticeBossCallTracker.Current?.Clear();
            Messages.Message("已清除正义Boss召唤锁。", MessageTypeDefOf.TaskCompletion, historical: false);
        }

        [DebugAction(
            "MAP-机械族机械师",
            "正义Boss：输出状态",
            false,
            false,
            false,
            false,
            false,
            0,
            false,
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Playing)]
        private static void DebugDumpStatus()
        {
            GameComponent_JusticeBossCallTracker? tracker =
                GameComponent_JusticeBossCallTracker.Current;
            string trackerText = tracker == null
                ? "tracker=null"
                : $"state={tracker.State} pawn={tracker.JusticePawn?.LabelShort ?? "null"} map={tracker.TargetMapParent?.Label ?? "null"}";

            CompJusticeBossController? comp = FindControllerOnMap();
            string compText = comp?.GetDebugStatus() ?? "controller=null";
            string diagnosticText = JusticeBossDiagnosticUtility.GetLastStatusText();
            Log.Message(
                "[MAP-机械族机械师] 正义 BOSS： "
                + trackerText
                + " | "
                + compText
                + " | "
                + diagnosticText);
            Messages.Message(
                compText + "\n" + diagnosticText,
                MessageTypeDefOf.NeutralEvent,
                historical: false);
        }
    }
}
