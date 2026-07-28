using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class MAPMechanitorModSettings : ModSettings
    {
        public bool addMechanoidMechanitorsToWorkTab = false;
        public bool enablePortraitDisplayForAllSaves = false;
        public bool enableMechanoidMechanitorBrainImplants = false;
        public bool enableLoverImplants = false;

        /// <summary>
        /// 每级效能核心提供的机械族全局工作速度偏移百分比。
        /// </summary>
        public float productivityCoreWorkSpeedOffsetPercentPerLevel =
            ProductivityCoreUtility.DefaultWorkSpeedOffsetPercentPerLevel;

        /// <summary>
        /// 默认关闭。开启后，仿生孕育的子嗣在受孕时额外继承配偶全部异种基因。
        /// </summary>
        public bool syntheticOffspringInheritXenogenes = false;

        public bool justiceBossEnableMortarShield =
            JusticeBossDifficultyValues.DefaultEnableMortarShield;

        public bool justiceBossEnableBulletShield =
            JusticeBossDifficultyValues.DefaultEnableBulletShield;

        public int justiceBossAutoMortarCount =
            JusticeBossDifficultyValues.DefaultAutoMortarCount;

        public int justiceBossAutoChargeBlasterCount =
            JusticeBossDifficultyValues.DefaultAutoChargeBlasterCount;

        public int justiceBossAutoInfernoCount =
            JusticeBossDifficultyValues.DefaultAutoInfernoCount;

        public int justiceBossTotalWaves =
            JusticeBossDifficultyValues.DefaultTotalWaves;

        public int justiceBossWaveIntervalTicks =
            JusticeBossDifficultyValues.DefaultWaveIntervalTicks;

        public int justiceBossMechsPerWave =
            JusticeBossDifficultyValues.DefaultMechsPerWave;

        public bool justiceBossAllowBossReplacement =
            JusticeBossDifficultyValues.DefaultAllowBossReplacement;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(
                ref addMechanoidMechanitorsToWorkTab,
                "addMechanoidMechanitorsToWorkTab",
                false);
            Scribe_Values.Look(
                ref enablePortraitDisplayForAllSaves,
                "enablePortraitDisplayForAllSaves",
                false);
            Scribe_Values.Look(
                ref enableMechanoidMechanitorBrainImplants,
                "enableMechanoidMechanitorBrainImplants",
                false);
            Scribe_Values.Look(
                ref enableLoverImplants,
                "enableLoverImplants",
                false);
            Scribe_Values.Look(
                ref productivityCoreWorkSpeedOffsetPercentPerLevel,
                "productivityCoreWorkSpeedOffsetPercentPerLevel",
                ProductivityCoreUtility.DefaultWorkSpeedOffsetPercentPerLevel);
            Scribe_Values.Look(
                ref syntheticOffspringInheritXenogenes,
                "syntheticOffspringInheritXenogenes",
                false);

            Scribe_Values.Look(
                ref justiceBossEnableMortarShield,
                "justiceBossEnableMortarShield",
                JusticeBossDifficultyValues.DefaultEnableMortarShield);

            Scribe_Values.Look(
                ref justiceBossEnableBulletShield,
                "justiceBossEnableBulletShield",
                JusticeBossDifficultyValues.DefaultEnableBulletShield);

            Scribe_Values.Look(
                ref justiceBossAutoMortarCount,
                "justiceBossAutoMortarCount",
                JusticeBossDifficultyValues.DefaultAutoMortarCount);

            Scribe_Values.Look(
                ref justiceBossAutoChargeBlasterCount,
                "justiceBossAutoChargeBlasterCount",
                JusticeBossDifficultyValues.DefaultAutoChargeBlasterCount);

            Scribe_Values.Look(
                ref justiceBossAutoInfernoCount,
                "justiceBossAutoInfernoCount",
                JusticeBossDifficultyValues.DefaultAutoInfernoCount);

            Scribe_Values.Look(
                ref justiceBossTotalWaves,
                "justiceBossTotalWaves",
                JusticeBossDifficultyValues.DefaultTotalWaves);

            Scribe_Values.Look(
                ref justiceBossWaveIntervalTicks,
                "justiceBossWaveIntervalTicks",
                JusticeBossDifficultyValues.DefaultWaveIntervalTicks);

            Scribe_Values.Look(
                ref justiceBossMechsPerWave,
                "justiceBossMechsPerWave",
                JusticeBossDifficultyValues.DefaultMechsPerWave);

            Scribe_Values.Look(
                ref justiceBossAllowBossReplacement,
                "justiceBossAllowBossReplacement",
                JusticeBossDifficultyValues.DefaultAllowBossReplacement);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                productivityCoreWorkSpeedOffsetPercentPerLevel = Mathf.Clamp(
                    productivityCoreWorkSpeedOffsetPercentPerLevel,
                    ProductivityCoreUtility.MinWorkSpeedOffsetPercentPerLevel,
                    ProductivityCoreUtility.MaxWorkSpeedOffsetPercentPerLevel);

                justiceBossAutoMortarCount =
                    JusticeBossDifficultyValues.ClampTurretCount(
                        justiceBossAutoMortarCount);

                justiceBossAutoChargeBlasterCount =
                    JusticeBossDifficultyValues.ClampTurretCount(
                        justiceBossAutoChargeBlasterCount);

                justiceBossAutoInfernoCount =
                    JusticeBossDifficultyValues.ClampTurretCount(
                        justiceBossAutoInfernoCount);

                justiceBossTotalWaves =
                    JusticeBossDifficultyValues.ClampTotalWaves(
                        justiceBossTotalWaves);

                justiceBossWaveIntervalTicks =
                    JusticeBossDifficultyValues.ClampWaveIntervalTicks(
                        justiceBossWaveIntervalTicks);

                justiceBossMechsPerWave =
                    JusticeBossDifficultyValues.ClampMechsPerWave(
                        justiceBossMechsPerWave);
            }
        }
    }
}
