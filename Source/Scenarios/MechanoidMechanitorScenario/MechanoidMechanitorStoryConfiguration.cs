using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidMechanitorStoryConfiguration : IExposable
    {
        public const int DefaultFactionOutpostRaidChancePercent = 1;
        public const int DefaultFactionOutpostSupportChancePercent = 10;
        public const int DefaultMechHiveNodeRaidChancePercent = 1;
        public const int DefaultMechHiveNodeSupportChancePercent = 10;

        public MechanoidMechanitorOrdinaryFactionRelationsMode ordinaryFactionRelationsMode =
            MechanoidMechanitorOrdinaryFactionRelationsMode.Default;

        public List<MechanoidMechanitorFactionRelationSetting> ordinaryFactionRelationSettings =
            new List<MechanoidMechanitorFactionRelationSetting>();

        public MechanoidMechanitorFactionOutpostFrequency factionOutpostFrequency =
            MechanoidMechanitorFactionOutpostFrequency.Off;

        public int hostileFactionOutpostWeight = 100;

        public int allyFactionOutpostWeight = 100;

        public int neutralFactionOutpostWeight = 100;

        public int factionOutpostRaidChancePercent =
            DefaultFactionOutpostRaidChancePercent;

        public int factionOutpostSupportChancePercent =
            DefaultFactionOutpostSupportChancePercent;

        public MechanoidMechanitorMechHiveRelationMode mechHiveRelationMode =
            MechanoidMechanitorMechHiveRelationMode.Default;

        public MechanoidMechanitorMechHiveNodeFrequency mechHiveNodeFrequency =
            MechanoidMechanitorMechHiveNodeFrequency.Off;

        public int mechHiveNodeRaidChancePercent =
            DefaultMechHiveNodeRaidChancePercent;

        public int mechHiveNodeSupportChancePercent =
            DefaultMechHiveNodeSupportChancePercent;

        public bool purgeDirectiveEnabled;

        public bool symbiosisCovenantEnabled;

        public MechanoidMechanitorIdeologyAdaptationLevel ideologyAdaptationLevel =
            MechanoidMechanitorIdeologyAdaptationLevel.Basic;

        public static MechanoidMechanitorStoryConfiguration CreateDefault()
        {
            MechanoidMechanitorStoryConfiguration configuration =
                new MechanoidMechanitorStoryConfiguration();

            // 普通剧本开放剧情风格后，文化适配默认保持完全关闭；真正的机械族机械师
            // 专用剧本仍沿用原有 Basic 默认值。仅在新游戏存在明确 Scenario 时分流，
            // 避免其他非开局调用在缺少 Scenario 上下文时改变历史默认行为。
            if (Find.Scenario != null
                && !MechanoidMechanitorScenarioUtility.ScenarioContainsMarker(Find.Scenario))
            {
                configuration.ideologyAdaptationLevel =
                    MechanoidMechanitorIdeologyAdaptationLevel.Disabled;
            }

            return configuration;
        }

        public MechanoidMechanitorStoryConfiguration CreateCopy()
        {
            MechanoidMechanitorStoryConfiguration copy = new MechanoidMechanitorStoryConfiguration
            {
                ordinaryFactionRelationsMode = ordinaryFactionRelationsMode,
                factionOutpostFrequency = factionOutpostFrequency,
                hostileFactionOutpostWeight = hostileFactionOutpostWeight,
                allyFactionOutpostWeight = allyFactionOutpostWeight,
                neutralFactionOutpostWeight = neutralFactionOutpostWeight,
                factionOutpostRaidChancePercent = factionOutpostRaidChancePercent,
                factionOutpostSupportChancePercent = factionOutpostSupportChancePercent,
                mechHiveRelationMode = mechHiveRelationMode,
                mechHiveNodeFrequency = mechHiveNodeFrequency,
                mechHiveNodeRaidChancePercent = mechHiveNodeRaidChancePercent,
                mechHiveNodeSupportChancePercent = mechHiveNodeSupportChancePercent,
                purgeDirectiveEnabled = purgeDirectiveEnabled,
                symbiosisCovenantEnabled = symbiosisCovenantEnabled,
                ideologyAdaptationLevel = ideologyAdaptationLevel
            };

            for (int i = 0; i < ordinaryFactionRelationSettings.Count; i++)
            {
                MechanoidMechanitorFactionRelationSetting? setting =
                    ordinaryFactionRelationSettings[i];
                if (setting == null)
                {
                    continue;
                }

                copy.ordinaryFactionRelationSettings.Add(setting.CreateCopy());
            }

            return copy;
        }

        public void SyncOrdinaryFactionEntries(
            MechanoidMechanitorStoryConfigurationContext context)
        {
            RemoveInvalidOrdinaryFactionEntries(context);
            EnsureMissingOrdinaryFactionEntries(context);
        }

        public void RemoveInvalidOrdinaryFactionEntries(
            MechanoidMechanitorStoryConfigurationContext context)
        {
            for (int i = ordinaryFactionRelationSettings.Count - 1; i >= 0; i--)
            {
                MechanoidMechanitorFactionRelationSetting? setting =
                    ordinaryFactionRelationSettings[i];
                if (setting?.faction == null
                    || !MechanoidMechanitorStoryConfigurationContext.IsOrdinaryFaction(
                        setting.faction))
                {
                    ordinaryFactionRelationSettings.RemoveAt(i);
                }
            }
        }

        public void EnsureMissingOrdinaryFactionEntries(
            MechanoidMechanitorStoryConfigurationContext context)
        {
            IReadOnlyList<Faction> ordinaryFactions = context.OrdinaryFactions;
            for (int i = 0; i < ordinaryFactions.Count; i++)
            {
                Faction faction = ordinaryFactions[i];
                if (FindSettingFor(faction) == null)
                {
                    ordinaryFactionRelationSettings.Add(
                        new MechanoidMechanitorFactionRelationSetting(
                            faction,
                            MechanoidMechanitorFactionRelationOption.Default));
                }
            }
        }

        public MechanoidMechanitorFactionRelationSetting? FindSettingFor(Faction faction)
        {
            for (int i = 0; i < ordinaryFactionRelationSettings.Count; i++)
            {
                MechanoidMechanitorFactionRelationSetting? setting =
                    ordinaryFactionRelationSettings[i];
                if (setting != null && setting.faction == faction)
                {
                    return setting;
                }
            }

            return null;
        }

        public MechanoidMechanitorFactionRelationOption GetRelationOptionFor(Faction faction)
        {
            MechanoidMechanitorFactionRelationSetting? setting = FindSettingFor(faction);
            return setting?.relationOption
                ?? MechanoidMechanitorFactionRelationOption.Default;
        }

        public void SetRelationOptionFor(
            Faction faction,
            MechanoidMechanitorFactionRelationOption option)
        {
            MechanoidMechanitorFactionRelationSetting? setting = FindSettingFor(faction);
            if (setting == null)
            {
                ordinaryFactionRelationSettings.Add(
                    new MechanoidMechanitorFactionRelationSetting(faction, option));
                return;
            }

            setting.relationOption = option;
        }

        public bool IsSymbiosisCovenantAvailable(
            MechanoidMechanitorStoryConfigurationContext context)
        {
            if (!context.HasOrdinaryFactions)
            {
                return false;
            }

            switch (ordinaryFactionRelationsMode)
            {
                case MechanoidMechanitorOrdinaryFactionRelationsMode.Default:
                case MechanoidMechanitorOrdinaryFactionRelationsMode.AllHostile:
                case MechanoidMechanitorOrdinaryFactionRelationsMode.AllAlly:
                    return true;

                case MechanoidMechanitorOrdinaryFactionRelationsMode.AllPermanentHostile:
                case MechanoidMechanitorOrdinaryFactionRelationsMode.AllPermanentNeutral:
                case MechanoidMechanitorOrdinaryFactionRelationsMode.AllPermanentAlly:
                    return false;

                case MechanoidMechanitorOrdinaryFactionRelationsMode.Custom:
                    return HasAnySymbiosisCovenantSpaceInCustomSettings(context);

                default:
                    return false;
            }
        }

        public void Normalize(MechanoidMechanitorStoryConfigurationContext context)
        {
            RemoveInvalidOrdinaryFactionEntries(context);
            EnsureMissingOrdinaryFactionEntries(context);

            hostileFactionOutpostWeight = Mathf.Clamp(hostileFactionOutpostWeight, 0, 100);
            allyFactionOutpostWeight = Mathf.Clamp(allyFactionOutpostWeight, 0, 100);
            neutralFactionOutpostWeight = Mathf.Clamp(neutralFactionOutpostWeight, 0, 100);

            // 概率字段保存为 0～100 的整数百分比，与外交关系动态变化无关，
            // 不应因关系或节点频率被清零，以免破坏玩家原先填写的数值。
            factionOutpostRaidChancePercent =
                Mathf.Clamp(factionOutpostRaidChancePercent, 0, 100);
            factionOutpostSupportChancePercent =
                Mathf.Clamp(factionOutpostSupportChancePercent, 0, 100);
            mechHiveNodeRaidChancePercent =
                Mathf.Clamp(mechHiveNodeRaidChancePercent, 0, 100);
            mechHiveNodeSupportChancePercent =
                Mathf.Clamp(mechHiveNodeSupportChancePercent, 0, 100);

            if (!context.HasMechHive || context.HasPursuingMechanoidsScenarioPart)
            {
                mechHiveRelationMode = MechanoidMechanitorMechHiveRelationMode.Default;
                purgeDirectiveEnabled = false;
            }

            if (!context.HasMechHive)
            {
                mechHiveNodeFrequency = MechanoidMechanitorMechHiveNodeFrequency.Off;
            }
            else if (mechHiveRelationMode != MechanoidMechanitorMechHiveRelationMode.Ally)
            {
                purgeDirectiveEnabled = false;
            }

            if (!context.HasOrdinaryFactions)
            {
                ordinaryFactionRelationsMode =
                    MechanoidMechanitorOrdinaryFactionRelationsMode.Default;
                symbiosisCovenantEnabled = false;
            }
            else if (!IsSymbiosisCovenantAvailable(context))
            {
                symbiosisCovenantEnabled = false;
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(
                ref ordinaryFactionRelationsMode,
                "ordinaryFactionRelationsMode",
                MechanoidMechanitorOrdinaryFactionRelationsMode.Default);
            Scribe_Collections.Look(
                ref ordinaryFactionRelationSettings,
                "ordinaryFactionRelationSettings",
                LookMode.Deep);
            Scribe_Values.Look(
                ref factionOutpostFrequency,
                "factionOutpostFrequency",
                MechanoidMechanitorFactionOutpostFrequency.Off);
            Scribe_Values.Look(
                ref hostileFactionOutpostWeight,
                "hostileFactionOutpostWeight",
                100);
            Scribe_Values.Look(
                ref allyFactionOutpostWeight,
                "allyFactionOutpostWeight",
                100);
            Scribe_Values.Look(
                ref neutralFactionOutpostWeight,
                "neutralFactionOutpostWeight",
                100);
            Scribe_Values.Look(
                ref factionOutpostRaidChancePercent,
                "factionOutpostRaidChancePercent",
                DefaultFactionOutpostRaidChancePercent);
            Scribe_Values.Look(
                ref factionOutpostSupportChancePercent,
                "factionOutpostSupportChancePercent",
                DefaultFactionOutpostSupportChancePercent);
            Scribe_Values.Look(
                ref mechHiveRelationMode,
                "mechHiveRelationMode",
                MechanoidMechanitorMechHiveRelationMode.Default);
            Scribe_Values.Look(
                ref mechHiveNodeFrequency,
                "mechHiveNodeFrequency",
                MechanoidMechanitorMechHiveNodeFrequency.Off);
            Scribe_Values.Look(
                ref mechHiveNodeRaidChancePercent,
                "mechHiveNodeRaidChancePercent",
                DefaultMechHiveNodeRaidChancePercent);
            Scribe_Values.Look(
                ref mechHiveNodeSupportChancePercent,
                "mechHiveNodeSupportChancePercent",
                DefaultMechHiveNodeSupportChancePercent);
            Scribe_Values.Look(ref purgeDirectiveEnabled, "purgeDirectiveEnabled", false);
            Scribe_Values.Look(ref symbiosisCovenantEnabled, "symbiosisCovenantEnabled", false);
            Scribe_Values.Look(
                ref ideologyAdaptationLevel,
                "ideologyAdaptationLevel",
                MechanoidMechanitorIdeologyAdaptationLevel.Basic);

            if (Scribe.mode == LoadSaveMode.LoadingVars
                && ordinaryFactionRelationSettings == null)
            {
                ordinaryFactionRelationSettings =
                    new List<MechanoidMechanitorFactionRelationSetting>();
            }
        }

        private bool HasAnySymbiosisCovenantSpaceInCustomSettings(
            MechanoidMechanitorStoryConfigurationContext context)
        {
            IReadOnlyList<Faction> ordinaryFactions = context.OrdinaryFactions;
            for (int i = 0; i < ordinaryFactions.Count; i++)
            {
                if (AllowsSymbiosisCovenantSpace(GetRelationOptionFor(ordinaryFactions[i])))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AllowsSymbiosisCovenantSpace(
            MechanoidMechanitorFactionRelationOption option)
        {
            return option == MechanoidMechanitorFactionRelationOption.Default
                || option == MechanoidMechanitorFactionRelationOption.Hostile
                || option == MechanoidMechanitorFactionRelationOption.Ally;
        }
    }
}
