using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.HumanoidAlienRaces
{
    /// <summary>只读取 HAR 的单一遗传来源出生配置，不接管其全局分娩或混种逻辑。</summary>
    internal sealed class HarSyntheticOffspringCompatibility : IThirdPartyCompatibilityModule
    {
        internal const string StablePackageId = "erdelf.HumanoidAlienRaces";
        internal const string DevPackageId = "erdelf.HumanoidAlienRaces.dev";
        private const string RaceTypeName = "AlienRace.ThingDef_AlienRace";
        private const BindingFlags Fields = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        private static Type? raceType;
        private static FieldInfo? alienRaceField;
        private static FieldInfo? generalSettingsField;
        private static FieldInfo? reproductionField;
        private static FieldInfo? childKindField;
        private static string unavailableReason = "HAR 出生配置兼容尚未就绪。";

        public string ModuleId => "HumanoidAlienRaces.SyntheticOffspring";
        public string DisplayName => "Humanoid Alien Races：仿生伴侣后代种族";
        public string PackageId => StablePackageId;

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            raceType = null;
            alienRaceField = generalSettingsField = reproductionField = childKindField = null;
            unavailableReason = "HAR 未加载或出生配置兼容尚未就绪。";
            ModContentPack? stable = ThirdPartyCompatibilityTargetResolver.FindRunningMod(StablePackageId);
            ModContentPack? dev = ThirdPartyCompatibilityTargetResolver.FindRunningMod(DevPackageId);
            if (stable == null && dev == null)
                return ThirdPartyCompatibilityResult.CreateInactive(ModuleId, DisplayName, PackageId);

            string packageId = dev != null ? DevPackageId : StablePackageId;
            if (stable != null && dev != null)
            {
                unavailableReason = "HAR 稳定版与 Dev 同时加载，拒绝选择存在歧义的出生配置。";
                return ThirdPartyCompatibilityResult.CreateTargetChanged(ModuleId, DisplayName, packageId, unavailableReason);
            }

            try
            {
                ModContentPack mod = (stable ?? dev)!;
                if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(
                        mod, RaceTypeName, out Type? type, out unavailableReason)
                    || !typeof(ThingDef).IsAssignableFrom(type)
                    || !TryResolveFields(type!, out unavailableReason))
                {
                    if (string.IsNullOrEmpty(unavailableReason))
                        unavailableReason = "HAR 种族类型不再继承 ThingDef。";
                    return ThirdPartyCompatibilityResult.CreateTargetChanged(ModuleId, DisplayName, packageId, unavailableReason);
                }

                // 全部成员验证完成后才发布可用类型。无需安装任何 Harmony 补丁。
                raceType = type;
                return ThirdPartyCompatibilityResult.CreateApplied(
                    ModuleId, DisplayName, packageId, "已验证 HAR 出生配置字段，仿生后代按遗传来源读取 childKindDef。");
            }
            catch (Exception exception)
            {
                raceType = null;
                unavailableReason = $"HAR 出生配置成员解析异常：{exception.GetType().Name}：{exception.Message}";
                return ThirdPartyCompatibilityResult.CreateFailed(
                    ModuleId, DisplayName, packageId, unavailableReason, exception);
            }
        }

        private static bool TryResolveFields(Type type, out string failure)
        {
            return TryResolveField(type, "alienRace", "AlienRace.ThingDef_AlienRace+AlienSettings", out alienRaceField, out failure)
                && TryResolveField(alienRaceField!.FieldType, "generalSettings", "AlienRace.GeneralSettings", out generalSettingsField, out failure)
                && TryResolveField(generalSettingsField!.FieldType, "reproduction", "AlienRace.ReproductionSettings", out reproductionField, out failure)
                && ThirdPartyCompatibilityTargetResolver.TryResolveUniqueField(
                    reproductionField!.FieldType, "childKindDef", typeof(PawnKindDef), Fields, out childKindField, out failure);
        }

        private static bool TryResolveField(Type owner, string name, string fieldTypeName, out FieldInfo? field, out string failure)
        {
            Type? expected = owner.Assembly.GetType(fieldTypeName, false, false);
            field = null;
            failure = $"HAR 字段类型 {fieldTypeName} 缺失。";
            return expected != null && ThirdPartyCompatibilityTargetResolver.TryResolveUniqueField(
                owner, name, expected, Fields, out field, out failure);
        }

        internal static bool IsAlienRace(ThingDef? race)
        {
            // 即使成员结构改变也能识别 HAR 来源并安全阻止猜测；不退回错误的智人 kind。
            for (Type? type = race?.GetType(); type != null; type = type.BaseType)
                if (type.FullName == RaceTypeName)
                    return true;
            return false;
        }

        internal static bool TryReadChildKind(ThingDef race, out PawnKindDef? kind, out string failure)
        {
            kind = null;
            failure = unavailableReason;
            if (raceType == null || !raceType.IsInstanceOfType(race))
                return false;

            try
            {
                object? alien = alienRaceField!.GetValue(race);
                object? general = alien == null ? null : generalSettingsField!.GetValue(alien);
                object? reproduction = general == null ? null : reproductionField!.GetValue(general);
                if (reproduction == null)
                {
                    failure = $"HAR 种族 {race.defName} 的 alienRace/generalSettings/reproduction 为空。";
                    return false;
                }

                kind = (PawnKindDef?)childKindField!.GetValue(reproduction);
                failure = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                failure = $"读取 HAR 种族 {race.defName} 的 childKindDef 失败：{exception.GetType().Name}：{exception.Message}";
                return false;
            }
        }
    }
}
