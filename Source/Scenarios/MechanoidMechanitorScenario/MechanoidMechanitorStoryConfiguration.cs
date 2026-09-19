using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidMechanitorStoryConfiguration : IExposable
    {
        public MechanoidMechanitorOrdinaryFactionRelationsMode ordinaryFactionRelationsMode =
            MechanoidMechanitorOrdinaryFactionRelationsMode.Default;

        public List<MechanoidMechanitorFactionRelationSetting> ordinaryFactionRelationSettings =
            new List<MechanoidMechanitorFactionRelationSetting>();

        public MechanoidMechanitorFactionOutpostFrequency factionOutpostFrequency =
            MechanoidMechanitorFactionOutpostFrequency.Off;

        public int hostileFactionOutpostWeight = 100;

        public int allyFactionOutpostWeight = 100;

        public int neutralFactionOutpostWeight = 100;

        public MechanoidMechanitorMechHiveRelationMode mechHiveRelationMode =
            MechanoidMechanitorMechHiveRelationMode.Default;

        public MechanoidMechanitorInsectRelationMode insectRelationMode =
            MechanoidMechanitorInsectRelationMode.Default;

        public MechanoidMechanitorMechHiveNodeFrequency mechHiveNodeFrequency =
            MechanoidMechanitorMechHiveNodeFrequency.Off;

        public bool purgeDirectiveEnabled;

        public bool symbiosisCovenantEnabled;

        public MechanoidMechanitorIdeologyAdaptationLevel ideologyAdaptationLevel =
            MechanoidMechanitorIdeologyAdaptationLevel.Full;

        public static MechanoidMechanitorStoryConfiguration CreateDefault()
        {
            MechanoidMechanitorStoryConfiguration configuration =
                new MechanoidMechanitorStoryConfiguration();

            // 剧情风格页草稿和无预设 Def 的后备配置：专用剧本默认 Full，
            // 明确处于普通剧本上下文时保留 Disabled 的历史后备行为。
            // 普通新游戏实际走 CreateForNewGame，使用独立模板的文化适配设置（默认 Full）。
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
                mechHiveRelationMode = mechHiveRelationMode,
                insectRelationMode = insectRelationMode,
                mechHiveNodeFrequency = mechHiveNodeFrequency,
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

            if (!context.HasMechHive || context.HasPursuingMechanoidsScenarioPart)
            {
                mechHiveRelationMode = MechanoidMechanitorMechHiveRelationMode.Default;
                purgeDirectiveEnabled = false;
            }

            if (!context.HasInsectFaction)
            {
                insectRelationMode = MechanoidMechanitorInsectRelationMode.Default;
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

            if (!FactionOutpostFactionUtility.HasAnyEligibleFaction())
            {
                factionOutpostFrequency = MechanoidMechanitorFactionOutpostFrequency.Off;
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
                ref mechHiveRelationMode,
                "mechHiveRelationMode",
                MechanoidMechanitorMechHiveRelationMode.Default);
            Scribe_Values.Look(
                ref insectRelationMode,
                "insectRelationMode",
                MechanoidMechanitorInsectRelationMode.Default);
            Scribe_Values.Look(
                ref mechHiveNodeFrequency,
                "mechHiveNodeFrequency",
                MechanoidMechanitorMechHiveNodeFrequency.Off);
            Scribe_Values.Look(ref purgeDirectiveEnabled, "purgeDirectiveEnabled", false);
            Scribe_Values.Look(ref symbiosisCovenantEnabled, "symbiosisCovenantEnabled", false);
            // Basic 是存档格式的兼容默认，并非新游戏默认。
            // 原版会省略等于默认值的字段；旧档缺 key 也可能表示玩家明确选择了 Basic，
            // 不能根据当前剧本或模板将缺失值改成 Full / Disabled。
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
