using System.Collections.Generic;
using LudeonTK;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class FactionOutpostDebugActions
    {
        [DebugAction("MAP-机械族机械师", "派系前哨测试",
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static List<DebugActionNode> BuildRootMenu()
        {
            return new List<DebugActionNode>
            {
                new DebugActionNode("尝试生成派系前哨",
                    DebugActionType.Action, DevTryGenerateFactionOutpost),
                new DebugActionNode("尝试生成敌对派系前哨",
                    DebugActionType.Action, DevTryGenerateHostileFactionOutpost),
                new DebugActionNode("尝试生成中立派系前哨",
                    DebugActionType.Action, DevTryGenerateNeutralFactionOutpost),
                new DebugActionNode("尝试生成盟友派系前哨",
                    DebugActionType.Action, DevTryGenerateAllyFactionOutpost)
            };
        }

        private static void DevTryGenerateFactionOutpost()
        {
            TryRun(manager => manager.DevTryNaturalGenerationAttempt());
        }

        private static void DevTryGenerateHostileFactionOutpost()
        {
            TryRun(manager => manager.DevTryGenerationAttemptForRelation(FactionRelationKind.Hostile));
        }

        private static void DevTryGenerateNeutralFactionOutpost()
        {
            TryRun(manager => manager.DevTryGenerationAttemptForRelation(FactionRelationKind.Neutral));
        }

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
