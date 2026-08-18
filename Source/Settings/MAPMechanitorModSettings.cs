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
        /// 阻止普通虫灾、深钻虫袭、废料虫袭，以及原版任务系统通过
        /// QuestNode_Infestation / QuestPart_Infestation 生成的标准任务型虫灾。
        /// 此设置不影响“永久中立”模式，不删除自然生成、任务之外生成或已经存在的虫巢。
        /// 被本功能阻止的调用会向日志输出黄色警告。
        /// </summary>
        public bool blockInfestationIncidentsWhenInsectsAllied = true;

        // ===== 虫巢追杀（Pursuit）模式全局设置（所有存档共享） =====

        /// <summary>
        /// 默认开启。开启后，追杀模式中标准普通虫灾优先使用原版虫灾落点；
        /// 只有原版找不到合法位置时，才允许在殖民地附近使用不要求厚岩顶的备用位置。
        /// 此设置不会修改默认 / 永久中立 / 盟友模式，也不会改变深钻虫袭、废料虫袭、
        /// 虫胶事件或其他特殊虫灾。
        /// </summary>
        public bool pursuitAllowInfestationWithoutThickRoof = true;

        /// <summary>
        /// 开局保护期（游戏日）。0～30。0 表示不提供开局保护。
        /// </summary>
        public int pursuitGracePeriodDays = DefaultPursuitGracePeriodDays;

        /// <summary>
        /// 虫族追猎每日触发概率百分比。0～100。
        /// </summary>
        public int pursuitHuntDailyChancePercent = DefaultPursuitHuntDailyChancePercent;

        /// <summary>
        /// 额外虫灾每日触发概率百分比。0～100。
        /// </summary>
        public int pursuitExtraInfestationDailyChancePercent =
            DefaultPursuitExtraInfestationDailyChancePercent;

        /// <summary>
        /// 追杀事件重复触发间隔（游戏日）。0～15。两种专属事件共用同一冷却。
        /// </summary>
        public int pursuitSharedCooldownDays = DefaultPursuitSharedCooldownDays;

        /// <summary>
        /// 虫族追猎地面虫群点数倍率百分比。0～1000。0% 表示不生成地面路线。
        /// </summary>
        public int pursuitHuntSurfacePointsPercent = DefaultPursuitHuntSurfacePointsPercent;

        /// <summary>
        /// 虫族追猎地下虫灾点数倍率百分比。0～1000。0% 表示不生成地下路线。
        /// </summary>
        public int pursuitHuntInfestationPointsPercent =
            DefaultPursuitHuntInfestationPointsPercent;

        public const int DefaultPursuitGracePeriodDays = 15;
        public const int DefaultPursuitHuntDailyChancePercent = 10;
        public const int DefaultPursuitExtraInfestationDailyChancePercent = 10;
        public const int DefaultPursuitSharedCooldownDays = 5;
        public const int DefaultPursuitHuntSurfacePointsPercent = 30;
        public const int DefaultPursuitHuntInfestationPointsPercent = 30;

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

        public const int DefaultMechanoidMechanitorIdleRecreationChancePercent = 25;
        public const int DefaultMechanoidMechanitorInspirationChancePercent = 5;

        public bool enableMechanoidMechanitorRecreation = true;

        public int mechanoidMechanitorIdleRecreationChancePercent =
            DefaultMechanoidMechanitorIdleRecreationChancePercent;

        public int mechanoidMechanitorInspirationChancePercent =
            DefaultMechanoidMechanitorInspirationChancePercent;

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
                ref pursuitAllowInfestationWithoutThickRoof,
                "pursuitAllowInfestationWithoutThickRoof",
                true);
            Scribe_Values.Look(
                ref pursuitGracePeriodDays,
                "pursuitGracePeriodDays",
                DefaultPursuitGracePeriodDays);
            Scribe_Values.Look(
                ref pursuitHuntDailyChancePercent,
                "pursuitHuntDailyChancePercent",
                DefaultPursuitHuntDailyChancePercent);
            Scribe_Values.Look(
                ref pursuitExtraInfestationDailyChancePercent,
                "pursuitExtraInfestationDailyChancePercent",
                DefaultPursuitExtraInfestationDailyChancePercent);
            Scribe_Values.Look(
                ref pursuitSharedCooldownDays,
                "pursuitSharedCooldownDays",
                DefaultPursuitSharedCooldownDays);
            Scribe_Values.Look(
                ref pursuitHuntSurfacePointsPercent,
                "pursuitHuntSurfacePointsPercent",
                DefaultPursuitHuntSurfacePointsPercent);
            Scribe_Values.Look(
                ref pursuitHuntInfestationPointsPercent,
                "pursuitHuntInfestationPointsPercent",
                DefaultPursuitHuntInfestationPointsPercent);

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

            Scribe_Values.Look(
                ref enableMechanoidMechanitorRecreation,
                "enableMechanoidMechanitorRecreation",
                true);

            Scribe_Values.Look(
                ref mechanoidMechanitorIdleRecreationChancePercent,
                "mechanoidMechanitorIdleRecreationChancePercent",
                DefaultMechanoidMechanitorIdleRecreationChancePercent);

            Scribe_Values.Look(
                ref mechanoidMechanitorInspirationChancePercent,
                "mechanoidMechanitorInspirationChancePercent",
                DefaultMechanoidMechanitorInspirationChancePercent);

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

                mechanoidMechanitorIdleRecreationChancePercent =
                    Mathf.Clamp(
                        mechanoidMechanitorIdleRecreationChancePercent,
                        0,
                        100);

                mechanoidMechanitorInspirationChancePercent =
                    Mathf.Clamp(
                        mechanoidMechanitorInspirationChancePercent,
                        0,
                        100);

                pursuitGracePeriodDays =
                    Mathf.Clamp(pursuitGracePeriodDays, 0, 30);
                pursuitHuntDailyChancePercent =
                    Mathf.Clamp(pursuitHuntDailyChancePercent, 0, 100);
                pursuitExtraInfestationDailyChancePercent =
                    Mathf.Clamp(pursuitExtraInfestationDailyChancePercent, 0, 100);
                pursuitSharedCooldownDays =
                    Mathf.Clamp(pursuitSharedCooldownDays, 0, 15);
                pursuitHuntSurfacePointsPercent =
                    Mathf.Clamp(pursuitHuntSurfacePointsPercent, 0, 1000);
                pursuitHuntInfestationPointsPercent =
                    Mathf.Clamp(pursuitHuntInfestationPointsPercent, 0, 1000);
            }
        }
    }
}
