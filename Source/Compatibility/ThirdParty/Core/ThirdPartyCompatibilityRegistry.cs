using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using MAP_MechanoidMechanitor.Compatibility.ThirdParty.DeadManSwitch;
using MAP_MechanoidMechanitor.Compatibility.ThirdParty.FamilyRelationsAdoption;
using MAP_MechanoidMechanitor.Compatibility.ThirdParty.Forgenest;
using MAP_MechanoidMechanitor.Compatibility.ThirdParty.MobileDragoon;
using MAP_MechanoidMechanitor.Compatibility.ThirdParty.MiliraRace;
using MAP_MechanoidMechanitor.Compatibility.ThirdParty.MilianModification;
using MAP_MechanoidMechanitor.Compatibility.ThirdParty.NewRatkin;
using MAP_MechanoidMechanitor.Compatibility.ThirdParty.PerspectiveShift;
using MAP_MechanoidMechanitor.Compatibility.ThirdParty.GlitterworldDestroyer5Expansion;
using MAP_MechanoidMechanitor.Compatibility.ThirdParty.GlitterworldDestroyer5ExpansionMechFusion;
using MAP_MechanoidMechanitor.Compatibility.ThirdParty.ProgressionEducation;
using MAP_MechanoidMechanitor.Compatibility.ThirdParty.QualityBuilder;
using MAP_MechanoidMechanitor.Compatibility.ThirdParty.RimHUD;
using MAP_MechanoidMechanitor.Compatibility.ThirdParty.RimTalk;
using MAP_MechanoidMechanitor.Compatibility.ThirdParty.RPGStyleInventory;
using MAP_MechanoidMechanitor.Compatibility.ThirdParty.RimSkyBlock;
using MAP_MechanoidMechanitor.Compatibility.ThirdParty.SRTSExpanded;
using MAP_MechanoidMechanitor.Compatibility.ThirdParty.VanillaGravshipExpanded;
using MAP_MechanoidMechanitor.Compatibility.ThirdParty.VanillaPsycastsExpanded;
using MAP_MechanoidMechanitor.Compatibility.ThirdParty.WorkTab;
using MAP_MechanoidMechanitor.Compatibility.ThirdParty.YetAnotherOptimizer;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty
{
    internal static class ThirdPartyCompatibilityRegistry
    {
        private const string LogPrefix = "[MAP-机械族机械师]";

        private static readonly IThirdPartyCompatibilityModule[] Modules =
        {
            new GlitterworldDestroyer5ExpansionCompatibility(),
            new GlitterworldDestroyer5ExpansionMechFusionCompatibility(),
            new MechFusionAbilityGrantCompatibility(),
            new MechFusionCastEligibilityCompatibility(),
            new ProgressionEducationCompatibility(),
            new QualityBuilderCompatibility(),
            new DeadManSwitchCompatibility(),
            new FamilyRelationsAdoptionCompatibility(),
            new ForgenestCompatibility(),
            new MobileDragoonCompatibility(),
            new MiliraEquipmentCompatibility(),
            new MilianModificationCompatibility(MilianModificationCompatibility.Feature.TransitionAnchor),
            new MilianModificationCompatibility(MilianModificationCompatibility.Feature.SelfInstallation),
            new MilianModificationCompatibility(MilianModificationCompatibility.Feature.AbilityCooldown),
            new NewRatkinWanderingTraderInteractionCompatibility(),
            new NewRatkinWanderingTraderIncidentCompatibility(),
            new PerspectiveShiftControlCompatibility(),
            new PerspectiveShiftEnergySafetyCompatibility(),
            new PerspectiveShiftFlightCompatibility(),
            new PerspectiveShiftFireAtWillCompatibility(),
            new PerspectiveShiftScenarioCompatibility(),
            new RimTalkCompatibility(),
            new RimHUDRechargeButtonCompatibility(),
            new RimHUDPawnDisplayCompatibility(RimHUDPawnDisplayCompatibility.Feature.CompInfo),
            new RimHUDPawnDisplayCompatibility(RimHUDPawnDisplayCompatibility.Feature.Inspiration),
            new RimHUDPawnDisplayCompatibility(RimHUDPawnDisplayCompatibility.Feature.CapabilityLayout),
            new RimHUDPawnDisplayCompatibility(RimHUDPawnDisplayCompatibility.Feature.ServiceEnergy),
            new RPGInventoryCompatibility(revamped: false),
            new RPGInventoryCompatibility(revamped: true),
            new RPGInventoryCompatibility(revamped: false, layout: true),
            new RPGInventoryCompatibility(revamped: true, layout: true),
            new RPGInventoryCECompatibility(),
            new RimSkyBlockImperialTaskCompatibility(),
            new RimSkyBlockCoronationCompatibility(),
            new SRTSExpandedCompatibility(),
            new VanillaGravshipLaunchStateCompatibility(),
            new VanillaGravshipPilotCompatibility(),
            new VanillaPsycastsExpandedCompatibility(),
            new WorkTabCompatibility(),
            new WorkTabPawnListCompatibility(),
            new YetAnotherOptimizerColonistBarCompatibility()
        };

        public static void ApplyAll(Harmony harmony)
        {
            List<ThirdPartyCompatibilityResult> applied =
                new List<ThirdPartyCompatibilityResult>();
            List<ThirdPartyCompatibilityResult> failed =
                new List<ThirdPartyCompatibilityResult>();

            for (int i = 0; i < Modules.Length; i++)
            {
                IThirdPartyCompatibilityModule module = Modules[i];
                ThirdPartyCompatibilityResult result;

                try
                {
                    result = module.Apply(harmony);
                }
                catch (Exception ex)
                {
                    result = ThirdPartyCompatibilityResult.CreateFailed(
                        module.ModuleId,
                        module.DisplayName,
                        module.PackageId,
                        "应用兼容模块时发生未预期异常。",
                        ex);
                }

                switch (result.Status)
                {
                    case ThirdPartyCompatibilityStatus.Applied:
                        applied.Add(result);
                        break;

                    case ThirdPartyCompatibilityStatus.TargetChanged:
                    case ThirdPartyCompatibilityStatus.Failed:
                        failed.Add(result);
                        break;
                }
            }

            LogAppliedSummary(applied);

            for (int i = 0; i < failed.Count; i++)
            {
                LogFailure(failed[i]);
            }
        }

        private static void LogAppliedSummary(
            List<ThirdPartyCompatibilityResult> applied)
        {
            if (applied.Count == 0
                || MAPMechanitorMod.Settings?.enableStartupDetailedLogging != true)
            {
                return;
            }

            StringBuilder builder = new StringBuilder();
            builder.Append(
                LogPrefix
                + "MOD兼容层已启用。成功加载的兼容补丁：");

            for (int i = 0; i < applied.Count; i++)
            {
                builder.Append("\n-");
                builder.Append(applied[i].DisplayName);
            }

            Log.Message(builder.ToString());
        }

        private static void LogFailure(ThirdPartyCompatibilityResult result)
        {
            string brief = LogPrefix + result.DisplayName + "加载失败。";
            bool detailed =
                MAPMechanitorMod.Settings?.enableCompatibilityDetailedLogging
                ?? false;

            if (!detailed)
            {
                Log.Error(brief);
                return;
            }

            Log.Error(
                brief
                + "\n"
                + ThirdPartyCompatibilityDiagnostics.BuildFailureReport(result));
        }
    }
}
