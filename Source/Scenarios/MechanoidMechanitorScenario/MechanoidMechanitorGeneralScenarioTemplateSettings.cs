using HarmonyLib;
using MAP_MechanoidMechanitor;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 普通剧本未启用剧情风格选择页时使用的独立默认剧情模板设置。
    /// 两个生成频率继续使用 MAPMechanitorModSettings 中已有字段；其余不依赖具体世界
    /// Faction 实例的字段保存在本 Settings 对象中。普通派系逐派系 Custom 模式明确不支持。
    /// </summary>
    public sealed class MechanoidMechanitorGeneralScenarioTemplateModSettings : ModSettings
    {
        public const MechanoidMechanitorOrdinaryFactionRelationsMode
            DefaultGeneralScenarioOrdinaryFactionRelationsMode =
                MechanoidMechanitorOrdinaryFactionRelationsMode.Default;

        public const int DefaultGeneralScenarioHostileFactionOutpostWeight = 100;
        public const int DefaultGeneralScenarioAllyFactionOutpostWeight = 100;
        public const int DefaultGeneralScenarioNeutralFactionOutpostWeight = 100;

        public const MechanoidMechanitorMechHiveRelationMode
            DefaultGeneralScenarioMechHiveRelationMode =
                MechanoidMechanitorMechHiveRelationMode.Default;

        public const MechanoidMechanitorInsectRelationMode
            DefaultGeneralScenarioInsectRelationMode =
                MechanoidMechanitorInsectRelationMode.Default;

        public const bool DefaultGeneralScenarioPurgeDirectiveEnabled = false;
        public const bool DefaultGeneralScenarioSymbiosisCovenantEnabled = false;

        public const MechanoidMechanitorIdeologyAdaptationLevel
            DefaultGeneralScenarioIdeologyAdaptationLevel =
                MechanoidMechanitorIdeologyAdaptationLevel.Full;

        public MechanoidMechanitorOrdinaryFactionRelationsMode
            generalScenarioOrdinaryFactionRelationsMode =
                DefaultGeneralScenarioOrdinaryFactionRelationsMode;

        public int generalScenarioHostileFactionOutpostWeight =
            DefaultGeneralScenarioHostileFactionOutpostWeight;

        public int generalScenarioAllyFactionOutpostWeight =
            DefaultGeneralScenarioAllyFactionOutpostWeight;

        public int generalScenarioNeutralFactionOutpostWeight =
            DefaultGeneralScenarioNeutralFactionOutpostWeight;

        public MechanoidMechanitorMechHiveRelationMode generalScenarioMechHiveRelationMode =
            DefaultGeneralScenarioMechHiveRelationMode;

        public MechanoidMechanitorInsectRelationMode generalScenarioInsectRelationMode =
            DefaultGeneralScenarioInsectRelationMode;

        public bool generalScenarioPurgeDirectiveEnabled =
            DefaultGeneralScenarioPurgeDirectiveEnabled;

        public bool generalScenarioSymbiosisCovenantEnabled =
            DefaultGeneralScenarioSymbiosisCovenantEnabled;

        public MechanoidMechanitorIdeologyAdaptationLevel generalScenarioIdeologyAdaptationLevel =
            DefaultGeneralScenarioIdeologyAdaptationLevel;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(
                ref generalScenarioOrdinaryFactionRelationsMode,
                "generalScenarioOrdinaryFactionRelationsMode",
                DefaultGeneralScenarioOrdinaryFactionRelationsMode);
            Scribe_Values.Look(
                ref generalScenarioHostileFactionOutpostWeight,
                "generalScenarioHostileFactionOutpostWeight",
                DefaultGeneralScenarioHostileFactionOutpostWeight);
            Scribe_Values.Look(
                ref generalScenarioAllyFactionOutpostWeight,
                "generalScenarioAllyFactionOutpostWeight",
                DefaultGeneralScenarioAllyFactionOutpostWeight);
            Scribe_Values.Look(
                ref generalScenarioNeutralFactionOutpostWeight,
                "generalScenarioNeutralFactionOutpostWeight",
                DefaultGeneralScenarioNeutralFactionOutpostWeight);
            Scribe_Values.Look(
                ref generalScenarioMechHiveRelationMode,
                "generalScenarioMechHiveRelationMode",
                DefaultGeneralScenarioMechHiveRelationMode);
            Scribe_Values.Look(
                ref generalScenarioInsectRelationMode,
                "generalScenarioInsectRelationMode",
                DefaultGeneralScenarioInsectRelationMode);
            Scribe_Values.Look(
                ref generalScenarioPurgeDirectiveEnabled,
                "generalScenarioPurgeDirectiveEnabled",
                DefaultGeneralScenarioPurgeDirectiveEnabled);
            Scribe_Values.Look(
                ref generalScenarioSymbiosisCovenantEnabled,
                "generalScenarioSymbiosisCovenantEnabled",
                DefaultGeneralScenarioSymbiosisCovenantEnabled);
            Scribe_Values.Look(
                ref generalScenarioIdeologyAdaptationLevel,
                "generalScenarioIdeologyAdaptationLevel",
                DefaultGeneralScenarioIdeologyAdaptationLevel);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                MechanoidMechanitorGeneralScenarioTemplateSettings.Normalize(
                    MAPMechanitorMod.Settings,
                    this);
            }
        }
    }

    /// <summary>
    /// 负责独立模板 Settings 的原生读取、保存和模板内部归一化。
    /// 使用单独的 settings handle，避免与 MAPMechanitorMod 的主 Settings 类型冲突。
    /// </summary>
    public static class MechanoidMechanitorGeneralScenarioTemplateSettings
    {
        private const string SettingsHandleName =
            "MAPMechanitorGeneralScenarioTemplate";

        private static MechanoidMechanitorGeneralScenarioTemplateModSettings? current;
        private static readonly MechanoidMechanitorGeneralScenarioTemplateModSettings Fallback =
            new MechanoidMechanitorGeneralScenarioTemplateModSettings();

        public static MechanoidMechanitorGeneralScenarioTemplateModSettings Current
        {
            get
            {
                EnsureLoaded();
                return current ?? Fallback;
            }
        }

        public static void EnsureLoaded()
        {
            if (current != null)
            {
                return;
            }

            MAPMechanitorMod? mod = LoadedModManager.GetMod<MAPMechanitorMod>();
            if (mod == null)
            {
                return;
            }

            current =
                LoadedModManager.ReadModSettings<
                    MechanoidMechanitorGeneralScenarioTemplateModSettings>(
                    mod.Content.FolderName,
                    SettingsHandleName);
            Normalize(MAPMechanitorMod.Settings, current);
        }

        public static void Save()
        {
            EnsureLoaded();
            if (current == null)
            {
                return;
            }

            MAPMechanitorMod? mod = LoadedModManager.GetMod<MAPMechanitorMod>();
            if (mod == null)
            {
                return;
            }

            Normalize(MAPMechanitorMod.Settings, current);
            LoadedModManager.WriteModSettings(
                mod.Content.FolderName,
                SettingsHandleName,
                current);
        }

        /// <summary>
        /// 只校正模板自身即可确定的合法性，不查询当前世界、FactionManager、机械巢或虫族。
        /// 具体世界是否拥有对应内容，继续由新游戏构造完成后的 StoryConfiguration.Normalize 处理。
        /// </summary>
        public static void Normalize(
            MAPMechanitorModSettings? mainSettings,
            MechanoidMechanitorGeneralScenarioTemplateModSettings template)
        {
            mainSettings?.NormalizeGeneralScenarioDefaultSettings();

            if (!IsValidOrdinaryFactionRelationsMode(
                    template.generalScenarioOrdinaryFactionRelationsMode))
            {
                template.generalScenarioOrdinaryFactionRelationsMode =
                    MechanoidMechanitorGeneralScenarioTemplateModSettings
                        .DefaultGeneralScenarioOrdinaryFactionRelationsMode;
            }

            template.generalScenarioHostileFactionOutpostWeight = Mathf.Clamp(
                template.generalScenarioHostileFactionOutpostWeight,
                0,
                100);
            template.generalScenarioAllyFactionOutpostWeight = Mathf.Clamp(
                template.generalScenarioAllyFactionOutpostWeight,
                0,
                100);
            template.generalScenarioNeutralFactionOutpostWeight = Mathf.Clamp(
                template.generalScenarioNeutralFactionOutpostWeight,
                0,
                100);

            if (!IsValidMechHiveRelationMode(template.generalScenarioMechHiveRelationMode))
            {
                template.generalScenarioMechHiveRelationMode =
                    MechanoidMechanitorGeneralScenarioTemplateModSettings
                        .DefaultGeneralScenarioMechHiveRelationMode;
            }

            if (!IsValidInsectRelationMode(template.generalScenarioInsectRelationMode))
            {
                template.generalScenarioInsectRelationMode =
                    MechanoidMechanitorGeneralScenarioTemplateModSettings
                        .DefaultGeneralScenarioInsectRelationMode;
            }

            if (!IsValidIdeologyAdaptationLevel(template.generalScenarioIdeologyAdaptationLevel))
            {
                template.generalScenarioIdeologyAdaptationLevel =
                    MechanoidMechanitorGeneralScenarioTemplateModSettings
                        .DefaultGeneralScenarioIdeologyAdaptationLevel;
            }

            if (template.generalScenarioMechHiveRelationMode
                != MechanoidMechanitorMechHiveRelationMode.Ally)
            {
                template.generalScenarioPurgeDirectiveEnabled = false;
            }

            if (!CanEnableSymbiosisCovenant(
                    template.generalScenarioOrdinaryFactionRelationsMode))
            {
                template.generalScenarioSymbiosisCovenantEnabled = false;
            }

            // 与 StoryConfiguration.Normalize 的既有互斥优先级一致：异常状态下肃清优先。
            if (template.generalScenarioPurgeDirectiveEnabled)
            {
                template.generalScenarioSymbiosisCovenantEnabled = false;
            }
        }

        public static bool CanEnablePurgeDirective(
            MechanoidMechanitorGeneralScenarioTemplateModSettings template)
        {
            return template.generalScenarioMechHiveRelationMode
                == MechanoidMechanitorMechHiveRelationMode.Ally;
        }

        public static bool CanEnableSymbiosisCovenant(
            MechanoidMechanitorOrdinaryFactionRelationsMode mode)
        {
            return mode == MechanoidMechanitorOrdinaryFactionRelationsMode.Default
                || mode == MechanoidMechanitorOrdinaryFactionRelationsMode.AllHostile
                || mode == MechanoidMechanitorOrdinaryFactionRelationsMode.AllAlly;
        }

        private static bool IsValidOrdinaryFactionRelationsMode(
            MechanoidMechanitorOrdinaryFactionRelationsMode mode)
        {
            // Custom 明确非法：主菜单 MOD 设置阶段没有可逐一绑定的实际 Faction 实例。
            return mode == MechanoidMechanitorOrdinaryFactionRelationsMode.Default
                || mode == MechanoidMechanitorOrdinaryFactionRelationsMode.AllHostile
                || mode == MechanoidMechanitorOrdinaryFactionRelationsMode.AllPermanentHostile
                || mode == MechanoidMechanitorOrdinaryFactionRelationsMode.AllPermanentNeutral
                || mode == MechanoidMechanitorOrdinaryFactionRelationsMode.AllAlly
                || mode == MechanoidMechanitorOrdinaryFactionRelationsMode.AllPermanentAlly;
        }

        private static bool IsValidMechHiveRelationMode(
            MechanoidMechanitorMechHiveRelationMode mode)
        {
            return mode == MechanoidMechanitorMechHiveRelationMode.Default
                || mode == MechanoidMechanitorMechHiveRelationMode.Neutral
                || mode == MechanoidMechanitorMechHiveRelationMode.PermanentNeutral
                || mode == MechanoidMechanitorMechHiveRelationMode.Ally;
        }

        private static bool IsValidInsectRelationMode(
            MechanoidMechanitorInsectRelationMode mode)
        {
            return mode == MechanoidMechanitorInsectRelationMode.Default
                || mode == MechanoidMechanitorInsectRelationMode.PermanentNeutral
                || mode == MechanoidMechanitorInsectRelationMode.Ally
                || mode == MechanoidMechanitorInsectRelationMode.Pursuit;
        }

        private static bool IsValidIdeologyAdaptationLevel(
            MechanoidMechanitorIdeologyAdaptationLevel level)
        {
            return level == MechanoidMechanitorIdeologyAdaptationLevel.Disabled
                || level == MechanoidMechanitorIdeologyAdaptationLevel.Basic
                || level == MechanoidMechanitorIdeologyAdaptationLevel.Partial
                || level == MechanoidMechanitorIdeologyAdaptationLevel.Full;
        }
    }

    [HarmonyPatch(typeof(MAPMechanitorMod), nameof(MAPMechanitorMod.WriteSettings))]
    public static class MechanoidMechanitorGeneralScenarioTemplateSettings_WriteSettings_Patch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            MechanoidMechanitorGeneralScenarioTemplateSettings.Save();
        }
    }
}
