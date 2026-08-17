using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class MAPMechanitorModSettings : ModSettings
    {
        public bool addMechanoidMechanitorsToWorkTab = false;
        public bool enablePortraitDisplayForAllSaves = false;
        public bool enableMechanoidMechanitorBrainImplants = false;

        // 字段名已改为 enableMoonImplants；序列化 key 仍沿用旧 "enableLoverImplants" 以保证旧设置兼容。
        public bool enableMoonImplants = false;

        /// <summary>
        /// 默认关闭。开启后，不含机械族机械师剧本词条的普通剧本也会在新游戏流程中
        /// 显示机械族机械师剧情风格页面。该设置只控制新游戏页面入口，不控制已建立存档的运行状态。
        /// </summary>
        public bool enableStoryStylesForGeneralScenarios = false;

        /// <summary>
        /// 是否监听动态分配目标的征召状态切换，并在下一游戏刻立即刷新目标状态。
        /// 默认开启；关闭后继续依赖各目标原有的周期检查间隔。
        /// </summary>
        public bool enableImmediateDraftStateRefresh = true;

        /// <summary>
        /// 临时诊断开关，默认关闭。
        /// 仅用于诊断旧存档加载时机械族机械师突然死亡：开启后会输出较详细的加载顺序、
        /// Hediff 与调用栈日志，以帮助定位 Pawn 从存活变为死亡的具体方法与步骤。
        /// 不影响任何游戏逻辑；诊断完成后应关闭以避免日志堆积。
        /// </summary>
        public bool enableLoadDeathDiagnosticLogging = false;

        /// <summary>
        /// 是否在打开肃清指令机械主脑通讯 UI 时显示连接加载界面。
        /// 默认开启。
        /// </summary>
        public bool enablePurgeDirectiveUiLoadingScreen = true;

        /// <summary>
        /// 每级效能核心提供的机械族全局工作速度偏移百分比。
        /// </summary>
        public float productivityCoreWorkSpeedOffsetPercentPerLevel =
            ProductivityCoreUtility.DefaultWorkSpeedOffsetPercentPerLevel;

        /// <summary>
        /// 默认关闭。开启后，仿生孕育的子嗣在受孕时额外继承配偶全部异种基因。
        /// </summary>
        public bool syntheticOffspringInheritXenogenes = false;

        /// <summary>
        /// 默认关闭。开启后输出正义 BOSS 召唤、空投与落地处理的详细诊断日志。
        /// 此设置实时生效，不纳入单场 BOSS 战难度快照。
        /// </summary>
        public bool enableJusticeBossDiagnosticLogging = false;

        /// <summary>
        /// 默认开启。开启后，当当前存档的机械族机械师剧情配置将虫巢设为盟友时，
        /// 阻止普通虫灾、深钻虫袭和废料虫袭事件发生。关闭后，即使虫巢为盟友，
        /// 这些原版事件仍可正常发生。此设置不影响“永久中立”模式，也不会删除
        /// 自然生成、任务生成或已经存在的虫巢。
        /// </summary>
        public bool blockInfestationIncidentsWhenInsectsAllied = true;

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

        /// <summary>
        /// 每座符合条件的完整敌对普通派系前哨每日触发额外袭击的基础概率百分比。
        /// 全局设置，所有存档共享。
        /// </summary>
        public int factionOutpostRaidChancePercent =
            DefaultFactionOutpostRaidChancePercent;

        /// <summary>
        /// 每座符合条件的完整盟友普通派系前哨，在敌对袭击发生后提供援军的基础概率百分比。
        /// 全局设置，所有存档共享。
        /// </summary>
        public int factionOutpostSupportChancePercent =
            DefaultFactionOutpostSupportChancePercent;

        /// <summary>
        /// 每座符合条件的完整敌对机械巢节点每日触发额外袭击的基础概率百分比。
        /// 全局设置，所有存档共享。
        /// </summary>
        public int mechHiveNodeRaidChancePercent =
            DefaultMechHiveNodeRaidChancePercent;

        /// <summary>
        /// 每座符合条件的完整盟友机械巢节点，在敌对袭击发生后提供援军的基础概率百分比。
        /// 全局设置，所有存档共享。
        /// </summary>
        public int mechHiveNodeSupportChancePercent =
            DefaultMechHiveNodeSupportChancePercent;

        public const int DefaultFactionOutpostRaidChancePercent = 1;
        public const int DefaultFactionOutpostSupportChancePercent = 10;
        public const int DefaultMechHiveNodeRaidChancePercent = 1;
        public const int DefaultMechHiveNodeSupportChancePercent = 10;

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
                ref enableMoonImplants,
                "enableLoverImplants",
                false);
            Scribe_Values.Look(
                ref enableStoryStylesForGeneralScenarios,
                "enableStoryStylesForGeneralScenarios",
                false);
            Scribe_Values.Look(
                ref enableImmediateDraftStateRefresh,
                "enableImmediateDraftStateRefresh",
                true);
            Scribe_Values.Look(
                ref enableLoadDeathDiagnosticLogging,
                "enableLoadDeathDiagnosticLogging",
                false);
            Scribe_Values.Look(
                ref enablePurgeDirectiveUiLoadingScreen,
                "enablePurgeDirectiveUiLoadingScreen",
                true);
            Scribe_Values.Look(
                ref productivityCoreWorkSpeedOffsetPercentPerLevel,
                "productivityCoreWorkSpeedOffsetPercentPerLevel",
                ProductivityCoreUtility.DefaultWorkSpeedOffsetPercentPerLevel);
            Scribe_Values.Look(
                ref syntheticOffspringInheritXenogenes,
                "syntheticOffspringInheritXenogenes",
                false);
            Scribe_Values.Look(
                ref enableJusticeBossDiagnosticLogging,
                "enableJusticeBossDiagnosticLogging",
                false);

            Scribe_Values.Look(
                ref blockInfestationIncidentsWhenInsectsAllied,
                "blockInfestationIncidentsWhenInsectsAllied",
                true);

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

            Scribe_Values.Look(
                ref factionOutpostRaidChancePercent,
                "factionOutpostRaidChancePercent",
                DefaultFactionOutpostRaidChancePercent);
            Scribe_Values.Look(
                ref factionOutpostSupportChancePercent,
                "factionOutpostSupportChancePercent",
                DefaultFactionOutpostSupportChancePercent);
            Scribe_Values.Look(
                ref mechHiveNodeRaidChancePercent,
                "mechHiveNodeRaidChancePercent",
                DefaultMechHiveNodeRaidChancePercent);
            Scribe_Values.Look(
                ref mechHiveNodeSupportChancePercent,
                "mechHiveNodeSupportChancePercent",
                DefaultMechHiveNodeSupportChancePercent);

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

                factionOutpostRaidChancePercent =
                    Mathf.Clamp(factionOutpostRaidChancePercent, 0, 100);
                factionOutpostSupportChancePercent =
                    Mathf.Clamp(factionOutpostSupportChancePercent, 0, 100);
                mechHiveNodeRaidChancePercent =
                    Mathf.Clamp(mechHiveNodeRaidChancePercent, 0, 100);
                mechHiveNodeSupportChancePercent =
                    Mathf.Clamp(mechHiveNodeSupportChancePercent, 0, 100);
            }
        }
    }
}
