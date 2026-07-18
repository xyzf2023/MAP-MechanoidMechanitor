using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidMechanitorStoryConfiguration : IExposable
    {
        public MechanoidMechanitorOrdinaryFactionRelationsMode ordinaryFactionRelationsMode =
            MechanoidMechanitorOrdinaryFactionRelationsMode.Default;

        public List<MechanoidMechanitorFactionRelationSetting> ordinaryFactionRelationSettings =
            new List<MechanoidMechanitorFactionRelationSetting>();

        public MechanoidMechanitorMechHiveRelationMode mechHiveRelationMode =
            MechanoidMechanitorMechHiveRelationMode.Default;

        public bool purgeDirectiveEnabled;

        public bool gainTrustRouteEnabled;

        public static MechanoidMechanitorStoryConfiguration CreateDefault()
        {
            return new MechanoidMechanitorStoryConfiguration();
        }

        public MechanoidMechanitorStoryConfiguration CreateCopy()
        {
            MechanoidMechanitorStoryConfiguration copy = new MechanoidMechanitorStoryConfiguration
            {
                ordinaryFactionRelationsMode = ordinaryFactionRelationsMode,
                mechHiveRelationMode = mechHiveRelationMode,
                purgeDirectiveEnabled = purgeDirectiveEnabled,
                gainTrustRouteEnabled = gainTrustRouteEnabled
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

        public bool IsGainTrustAvailable(
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
                case MechanoidMechanitorOrdinaryFactionRelationsMode.AllNeutral:
                    return true;

                case MechanoidMechanitorOrdinaryFactionRelationsMode.AllPermanentHostile:
                case MechanoidMechanitorOrdinaryFactionRelationsMode.AllPermanentNeutral:
                case MechanoidMechanitorOrdinaryFactionRelationsMode.AllAlly:
                case MechanoidMechanitorOrdinaryFactionRelationsMode.AllPermanentAlly:
                    return false;

                case MechanoidMechanitorOrdinaryFactionRelationsMode.Custom:
                    return HasAnyGainTrustSpaceInCustomSettings(context);

                default:
                    return false;
            }
        }

        public void Normalize(MechanoidMechanitorStoryConfigurationContext context)
        {
            RemoveInvalidOrdinaryFactionEntries(context);
            EnsureMissingOrdinaryFactionEntries(context);

            if (!context.HasMechHive)
            {
                mechHiveRelationMode = MechanoidMechanitorMechHiveRelationMode.Default;
                purgeDirectiveEnabled = false;
            }
            else if (mechHiveRelationMode != MechanoidMechanitorMechHiveRelationMode.Ally)
            {
                purgeDirectiveEnabled = false;
            }

            if (!context.HasOrdinaryFactions)
            {
                ordinaryFactionRelationsMode =
                    MechanoidMechanitorOrdinaryFactionRelationsMode.Default;
                gainTrustRouteEnabled = false;
            }
            else if (!IsGainTrustAvailable(context))
            {
                gainTrustRouteEnabled = false;
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
                ref mechHiveRelationMode,
                "mechHiveRelationMode",
                MechanoidMechanitorMechHiveRelationMode.Default);
            Scribe_Values.Look(ref purgeDirectiveEnabled, "purgeDirectiveEnabled", false);
            Scribe_Values.Look(ref gainTrustRouteEnabled, "gainTrustRouteEnabled", false);

            if (Scribe.mode == LoadSaveMode.LoadingVars
                && ordinaryFactionRelationSettings == null)
            {
                ordinaryFactionRelationSettings =
                    new List<MechanoidMechanitorFactionRelationSetting>();
            }
        }

        private bool HasAnyGainTrustSpaceInCustomSettings(
            MechanoidMechanitorStoryConfigurationContext context)
        {
            IReadOnlyList<Faction> ordinaryFactions = context.OrdinaryFactions;
            for (int i = 0; i < ordinaryFactions.Count; i++)
            {
                if (AllowsGainTrustSpace(GetRelationOptionFor(ordinaryFactions[i])))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AllowsGainTrustSpace(
            MechanoidMechanitorFactionRelationOption option)
        {
            return option == MechanoidMechanitorFactionRelationOption.Default
                || option == MechanoidMechanitorFactionRelationOption.Hostile
                || option == MechanoidMechanitorFactionRelationOption.Neutral;
        }
    }
}
