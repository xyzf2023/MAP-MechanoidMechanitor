using System;
using System.Collections.Generic;
using LudeonTK;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 纯原生 LudeonTK 开发者控制台剧情状态切换树。
    /// 不创建 Window/FloatMenu，不接管 Dialog_Debug；所有层级均由 DebugActionNode.childGetter 提供。
    /// 所有固定菜单项都使用稳定 label，便于人工操作以及后续自动测试按控制台路径调用。
    /// </summary>
    public static class MechanoidMechanitorStoryStateDebugActions
    {
        private static readonly MechanoidMechanitorOrdinaryFactionRelationsMode[]
            OrdinaryFactionRelationModes =
            {
                MechanoidMechanitorOrdinaryFactionRelationsMode.Default,
                MechanoidMechanitorOrdinaryFactionRelationsMode.AllHostile,
                MechanoidMechanitorOrdinaryFactionRelationsMode.AllPermanentHostile,
                MechanoidMechanitorOrdinaryFactionRelationsMode.AllPermanentNeutral,
                MechanoidMechanitorOrdinaryFactionRelationsMode.AllAlly,
                MechanoidMechanitorOrdinaryFactionRelationsMode.AllPermanentAlly
            };

        private static readonly MechanoidMechanitorFactionRelationOption[]
            OrdinaryFactionRelationOptions =
            {
                MechanoidMechanitorFactionRelationOption.Default,
                MechanoidMechanitorFactionRelationOption.Hostile,
                MechanoidMechanitorFactionRelationOption.PermanentHostile,
                MechanoidMechanitorFactionRelationOption.PermanentNeutral,
                MechanoidMechanitorFactionRelationOption.Ally,
                MechanoidMechanitorFactionRelationOption.PermanentAlly
            };

        private static readonly MechanoidMechanitorFactionOutpostFrequency[]
            FactionOutpostFrequencies =
            {
                MechanoidMechanitorFactionOutpostFrequency.Off,
                MechanoidMechanitorFactionOutpostFrequency.Low,
                MechanoidMechanitorFactionOutpostFrequency.Medium,
                MechanoidMechanitorFactionOutpostFrequency.High
            };

        private static readonly MechanoidMechanitorMechHiveRelationMode[]
            MechHiveRelationModes =
            {
                MechanoidMechanitorMechHiveRelationMode.Default,
                MechanoidMechanitorMechHiveRelationMode.Neutral,
                MechanoidMechanitorMechHiveRelationMode.PermanentNeutral,
                MechanoidMechanitorMechHiveRelationMode.Ally
            };

        private static readonly MechanoidMechanitorMechHiveNodeFrequency[]
            MechHiveNodeFrequencies =
            {
                MechanoidMechanitorMechHiveNodeFrequency.Off,
                MechanoidMechanitorMechHiveNodeFrequency.Low,
                MechanoidMechanitorMechHiveNodeFrequency.Medium,
                MechanoidMechanitorMechHiveNodeFrequency.High
            };

        private static readonly MechanoidMechanitorIdeologyAdaptationLevel[]
            IdeologyAdaptationLevels =
            {
                MechanoidMechanitorIdeologyAdaptationLevel.Disabled,
                MechanoidMechanitorIdeologyAdaptationLevel.Basic,
                MechanoidMechanitorIdeologyAdaptationLevel.Partial,
                MechanoidMechanitorIdeologyAdaptationLevel.Full
            };

        private delegate bool TryStoryStateAction(out string message);

        private delegate bool TryBooleanStoryStateAction(bool value, out string message);

        [DebugAction(
            "MAP-机械族机械师",
            "切换剧本状态",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Playing)]
        private static List<DebugActionNode> ChangeStoryState()
        {
            MechanoidMechanitorStoryConfiguration? configuration =
                GameComponent_MechanoidMechanitorStoryState.CurrentConfiguration;
            if (configuration == null)
            {
                return new List<DebugActionNode>
                {
                    new DebugActionNode(
                        "当前没有活动剧情配置",
                        DebugActionType.Action,
                        () => Execute(
                            (out string message) =>
                            {
                                message =
                                    "无法切换剧本状态：当前存档没有活动的机械族机械师剧情配置。";
                                return false;
                            }))
                };
            }

            MechanoidMechanitorStoryConfigurationContext context =
                MechanoidMechanitorStoryConfigurationContext.Create(configuration);
            List<DebugActionNode> nodes = new List<DebugActionNode>();

            if (context.HasOrdinaryFactions)
            {
                nodes.Add(CreateSubmenuNode(
                    "普通派系关系",
                    BuildOrdinaryFactionRelationsMenu));
            }

            if (FactionOutpostFactionUtility.HasAnyEligibleFaction())
            {
                nodes.Add(CreateSubmenuNode(
                    "派系前哨生成频率",
                    BuildFactionOutpostFrequencyMenu));
                nodes.Add(CreateSubmenuNode(
                    "派系前哨：敌对权重",
                    () => BuildFactionOutpostWeightMenu(
                        MechanoidMechanitorFactionOutpostWeightKind.Hostile)));
                nodes.Add(CreateSubmenuNode(
                    "派系前哨：中立权重",
                    () => BuildFactionOutpostWeightMenu(
                        MechanoidMechanitorFactionOutpostWeightKind.Neutral)));
                nodes.Add(CreateSubmenuNode(
                    "派系前哨：盟友权重",
                    () => BuildFactionOutpostWeightMenu(
                        MechanoidMechanitorFactionOutpostWeightKind.Ally)));
            }

            if (context.HasMechHive)
            {
                nodes.Add(CreateSubmenuNode(
                    "与机械巢关系",
                    BuildMechHiveRelationMenu));
                nodes.Add(CreateSubmenuNode(
                    "机械巢节点生成频率",
                    BuildMechHiveNodeFrequencyMenu));
                nodes.Add(CreateSubmenuNode(
                    "肃清指令",
                    BuildPurgeDirectiveMenu));
            }

            if (context.HasOrdinaryFactions)
            {
                nodes.Add(CreateSubmenuNode(
                    "共生盟约",
                    BuildSymbiosisCovenantMenu));
            }

            if (ModsConfig.IdeologyActive)
            {
                nodes.Add(CreateSubmenuNode(
                    "文化适配等级",
                    BuildIdeologyAdaptationMenu));
            }

            return nodes;
        }

        private static List<DebugActionNode> BuildOrdinaryFactionRelationsMenu()
        {
            List<DebugActionNode> nodes = new List<DebugActionNode>();
            for (int i = 0; i < OrdinaryFactionRelationModes.Length; i++)
            {
                MechanoidMechanitorOrdinaryFactionRelationsMode captured =
                    OrdinaryFactionRelationModes[i];
                nodes.Add(CreateActionOption(
                    MechanoidMechanitorStoryConfigurationLabels.LabelFor(captured),
                    (out string message) =>
                        MechanoidMechanitorStoryRuntimeConfigurationUtility
                            .TrySetOrdinaryFactionRelationsMode(captured, out message)));
            }

            nodes.Add(CreateSubmenuNode(
                MechanoidMechanitorStoryConfigurationLabels.LabelFor(
                    MechanoidMechanitorOrdinaryFactionRelationsMode.Custom),
                BuildCustomOrdinaryFactionMenu));
            return nodes;
        }

        private static List<DebugActionNode> BuildCustomOrdinaryFactionMenu()
        {
            List<Faction> factions =
                MechanoidMechanitorStoryConfigurationContext.GetOrdinaryFactionsSorted();
            List<DebugActionNode> nodes = new List<DebugActionNode>(factions.Count);
            for (int i = 0; i < factions.Count; i++)
            {
                Faction capturedFaction = factions[i];
                nodes.Add(CreateSubmenuNode(
                    capturedFaction.Name,
                    () => BuildSingleFactionRelationMenu(capturedFaction)));
            }

            if (nodes.Count == 0)
            {
                nodes.Add(new DebugActionNode(
                    "当前没有可调整的普通派系",
                    DebugActionType.Action,
                    () => Messages.Message(
                        "当前没有可调整的普通派系。",
                        MessageTypeDefOf.RejectInput,
                        historical: false)));
            }

            return nodes;
        }

        private static List<DebugActionNode> BuildSingleFactionRelationMenu(Faction faction)
        {
            List<DebugActionNode> nodes =
                new List<DebugActionNode>(OrdinaryFactionRelationOptions.Length);
            for (int i = 0; i < OrdinaryFactionRelationOptions.Length; i++)
            {
                MechanoidMechanitorFactionRelationOption captured =
                    OrdinaryFactionRelationOptions[i];
                nodes.Add(CreateActionOption(
                    MechanoidMechanitorStoryConfigurationLabels.LabelFor(captured),
                    (out string message) =>
                        MechanoidMechanitorStoryRuntimeConfigurationUtility
                            .TrySetOrdinaryFactionRelationOption(
                                faction,
                                captured,
                                out message)));
            }

            return nodes;
        }

        private static List<DebugActionNode> BuildFactionOutpostFrequencyMenu()
        {
            List<DebugActionNode> nodes =
                new List<DebugActionNode>(FactionOutpostFrequencies.Length);
            for (int i = 0; i < FactionOutpostFrequencies.Length; i++)
            {
                MechanoidMechanitorFactionOutpostFrequency captured =
                    FactionOutpostFrequencies[i];
                nodes.Add(CreateActionOption(
                    LabelForFactionOutpostFrequency(captured),
                    (out string message) =>
                        MechanoidMechanitorStoryRuntimeConfigurationUtility
                            .TrySetFactionOutpostFrequency(captured, out message)));
            }

            return nodes;
        }

        private static List<DebugActionNode> BuildFactionOutpostWeightMenu(
            MechanoidMechanitorFactionOutpostWeightKind kind)
        {
            List<DebugActionNode> nodes = new List<DebugActionNode>(101);
            for (int value = 0; value <= 100; value++)
            {
                int captured = value;
                nodes.Add(CreateActionOption(
                    captured.ToString(),
                    (out string message) =>
                        MechanoidMechanitorStoryRuntimeConfigurationUtility
                            .TrySetFactionOutpostWeight(kind, captured, out message)));
            }

            return nodes;
        }

        private static List<DebugActionNode> BuildMechHiveRelationMenu()
        {
            List<DebugActionNode> nodes =
                new List<DebugActionNode>(MechHiveRelationModes.Length);
            for (int i = 0; i < MechHiveRelationModes.Length; i++)
            {
                MechanoidMechanitorMechHiveRelationMode captured = MechHiveRelationModes[i];
                nodes.Add(CreateActionOption(
                    MechanoidMechanitorStoryConfigurationLabels.LabelFor(captured),
                    (out string message) =>
                        MechanoidMechanitorStoryRuntimeConfigurationUtility
                            .TrySetMechHiveRelationMode(captured, out message)));
            }

            return nodes;
        }

        private static List<DebugActionNode> BuildMechHiveNodeFrequencyMenu()
        {
            List<DebugActionNode> nodes =
                new List<DebugActionNode>(MechHiveNodeFrequencies.Length);
            for (int i = 0; i < MechHiveNodeFrequencies.Length; i++)
            {
                MechanoidMechanitorMechHiveNodeFrequency captured =
                    MechHiveNodeFrequencies[i];
                nodes.Add(CreateActionOption(
                    MechanoidMechanitorStoryConfigurationLabels.LabelFor(captured),
                    (out string message) =>
                        MechanoidMechanitorStoryRuntimeConfigurationUtility
                            .TrySetMechHiveNodeFrequency(captured, out message)));
            }

            return nodes;
        }

        private static List<DebugActionNode> BuildPurgeDirectiveMenu()
        {
            return BuildBooleanMenu(
                (bool value, out string message) =>
                    MechanoidMechanitorStoryRuntimeConfigurationUtility
                        .TrySetPurgeDirectiveEnabled(value, out message));
        }

        private static List<DebugActionNode> BuildSymbiosisCovenantMenu()
        {
            return BuildBooleanMenu(
                (bool value, out string message) =>
                    MechanoidMechanitorStoryRuntimeConfigurationUtility
                        .TrySetSymbiosisCovenantEnabled(value, out message));
        }

        private static List<DebugActionNode> BuildBooleanMenu(
            TryBooleanStoryStateAction action)
        {
            List<DebugActionNode> nodes = new List<DebugActionNode>(2);
            bool[] values = { false, true };
            for (int i = 0; i < values.Length; i++)
            {
                bool captured = values[i];
                nodes.Add(CreateActionOption(
                    LabelForBoolean(captured),
                    (out string message) => action(captured, out message)));
            }

            return nodes;
        }

        private static List<DebugActionNode> BuildIdeologyAdaptationMenu()
        {
            List<DebugActionNode> nodes =
                new List<DebugActionNode>(IdeologyAdaptationLevels.Length);
            for (int i = 0; i < IdeologyAdaptationLevels.Length; i++)
            {
                MechanoidMechanitorIdeologyAdaptationLevel captured =
                    IdeologyAdaptationLevels[i];
                nodes.Add(CreateActionOption(
                    MechanoidMechanitorStoryConfigurationLabels.LabelFor(captured),
                    (out string message) =>
                        MechanoidMechanitorStoryRuntimeConfigurationUtility
                            .TrySetIdeologyAdaptationLevel(captured, out message)));
            }

            return nodes;
        }

        private static DebugActionNode CreateSubmenuNode(
            string label,
            Func<List<DebugActionNode>> childGetter)
        {
            return new DebugActionNode(label)
            {
                actionType = DebugActionType.Action,
                childGetter = childGetter
            };
        }

        private static DebugActionNode CreateActionOption(
            string label,
            TryStoryStateAction action)
        {
            return new DebugActionNode(
                label,
                DebugActionType.Action,
                () => Execute(action));
        }

        private static void Execute(TryStoryStateAction action)
        {
            bool succeeded;
            string message;
            try
            {
                succeeded = action(out message);
            }
            catch (Exception ex)
            {
                Log.Error("[MAP-StoryStateDebug] 控制台剧情状态切换发生未处理异常。\n" + ex);
                succeeded = false;
                message = "切换剧本状态时发生异常；请查看日志。";
            }

            if (message.NullOrEmpty())
            {
                message = succeeded ? "剧本状态切换完成。" : "剧本状态切换失败。";
            }

            Messages.Message(
                message,
                succeeded
                    ? MessageTypeDefOf.TaskCompletion
                    : MessageTypeDefOf.RejectInput,
                historical: false);

            if (succeeded)
            {
                Log.Message("[MAP-StoryStateDebug] OK " + message);
            }
            else
            {
                Log.Warning("[MAP-StoryStateDebug] REJECT " + message);
            }
        }

        private static string LabelForBoolean(bool value)
        {
            return value
                ? MechanoidMechanitorStoryConfigurationLabels.EnabledLabel
                : MechanoidMechanitorStoryConfigurationLabels.DisabledLabel;
        }

        private static string LabelForFactionOutpostFrequency(
            MechanoidMechanitorFactionOutpostFrequency frequency)
        {
            return frequency switch
            {
                MechanoidMechanitorFactionOutpostFrequency.Off =>
                    "MAP_MechanoidMechanitor.Story.FactionOutpostFrequency.Off".Translate(),
                MechanoidMechanitorFactionOutpostFrequency.Low =>
                    "MAP_MechanoidMechanitor.Story.FactionOutpostFrequency.Low".Translate(),
                MechanoidMechanitorFactionOutpostFrequency.Medium =>
                    "MAP_MechanoidMechanitor.Story.FactionOutpostFrequency.Medium".Translate(),
                MechanoidMechanitorFactionOutpostFrequency.High =>
                    "MAP_MechanoidMechanitor.Story.FactionOutpostFrequency.High".Translate(),
                _ => frequency.ToString()
            };
        }
    }
}
