using LudeonTK;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class FactionOutpostDebugActions
    {
        [DebugAction(
            "MAP-机械族机械师",
            "尝试生成派系前哨",
            false,
            false,
            false,
            false,
            false,
            0,
            false,
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Playing)]
        private static void DevTryGenerateFactionOutpost()
        {
            TryRun(manager => manager.DevTryNaturalGenerationAttempt());
        }

        [DebugAction(
            "MAP-机械族机械师",
            "尝试生成敌对派系前哨",
            false,
            false,
            false,
            false,
            false,
            0,
            false,
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Playing)]
        private static void DevTryGenerateHostileFactionOutpost()
        {
            TryRun(manager => manager.DevTryGenerationAttemptForRelation(FactionRelationKind.Hostile));
        }

        [DebugAction(
            "MAP-机械族机械师",
            "尝试生成中立派系前哨",
            false,
            false,
            false,
            false,
            false,
            0,
            false,
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Playing)]
        private static void DevTryGenerateNeutralFactionOutpost()
        {
            TryRun(manager => manager.DevTryGenerationAttemptForRelation(FactionRelationKind.Neutral));
        }

        [DebugAction(
            "MAP-机械族机械师",
            "尝试生成盟友派系前哨",
            false,
            false,
            false,
            false,
            false,
            0,
            false,
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Playing)]
        private static void DevTryGenerateAllyFactionOutpost()
        {
            TryRun(manager => manager.DevTryGenerationAttemptForRelation(FactionRelationKind.Ally));
        }

        private static void TryRun(System.Action<FactionOutpostManager> action)
        {
            if (Current.Game == null || Find.World == null)
            {
                return;
            }

            FactionOutpostManager? manager = Find.World.GetComponent<FactionOutpostManager>();
            if (manager == null)
            {
                Log.Warning("[MAP-机械族机械师] 未找到 FactionOutpostManager，无法执行开发者生成指令。");
                return;
            }

            action(manager);
        }
    }
}
