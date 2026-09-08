using UnityEngine;
using Verse;
using MAP_MechanoidMechanitor.Scenarios;

namespace MAP_MechanoidMechanitor
{
    public class MAPMechanitorModSettings : ModSettings
    {
        public bool addMechanoidMechanitorsToWorkTab = false;

        /// <summary>
        /// 默认关闭。开启后，玩家所属的机械族可以通过原版右键菜单中的 WorkGivers
        /// 执行「优先工作」指令。本设置只解除右键菜单层面的禁用，不会赋予机械族
        /// 新的工作类型或工作能力，也不会影响其它机械族控制逻辑。
        /// </summary>
        public bool enableMechanoidPrioritizedWorkOrders = false;

        public bool enablePortraitDisplayForAllSaves = false;
        public bool enableMechanoidMechanitorBrainImplants = false;

        // 字段名已改为 enableMoonImplants；序列化 key 仍沿用旧 "enableLoverImplants" 以保证旧设置兼容。
        public bool enableMoonImplants = true;

        /// <summary>
        /// 默认关闭。开启后，不含机械族机械师剧本词条的普通剧本也会在新游戏流程中
        /// 显示机械族机械师剧情风格页面。该设置只控制新游戏页面入口，不控制已建立存档的运行状态。
        /// </summary>
        public bool enableStoryStylesForGeneralScenarios = false;

        /// <summary>
        /// 普通剧本未启用剧情风格选择页时，新游戏静默配置使用的普通派系前哨生成频率。
        /// 初始值按当前 Classic 预设设为 Low；保存后与 Classic 完全解耦。
        /// </summary>
        public MechanoidMechanitorFactionOutpostFrequency generalScenarioFactionOutpostFrequency =
            DefaultGeneralScenarioFactionOutpostFrequency;

        /// <summary>
        /// 普通剧本未启用剧情风格选择页时，新游戏静默配置使用的机械巢节点生成频率。
        /// 初始值按当前 Classic 预设设为 Low；保存后与 Classic 完全解耦。
        /// </summary>
        public MechanoidMechanitorMechHiveNodeFrequency generalScenarioMechHiveNodeFrequency =
            DefaultGeneralScenarioMechHiveNodeFrequency;

        public const MechanoidMechanitorFactionOutpostFrequency
            DefaultGeneralScenarioFactionOutpostFrequency =
                MechanoidMechanitorFactionOutpostFrequency.Low;

        public const MechanoidMechanitorMechHiveNodeFrequency
            DefaultGeneralScenarioMechHiveNodeFrequency =
                MechanoidMechanitorMechHiveNodeFrequency.Low;

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
        /// 默认关闭。开启后，仅在第三方兼容补丁加载失败时构造并输出详细诊断，
        /// 包括失败阶段、反射目标、异常链，以及已知 Harmony 补丁与其可能所属 MOD。
        /// 关闭时失败日志只保留补丁名称。
        /// </summary>
        public bool enableCompatibilityDetailedLogging = false;

        /// <summary>
        /// 默认开启。开启后，阻止加载存档期间原本存活的机械族机械师意外死亡。
        /// 该保护仅作用于存档加载及现有读档安全恢复流程完成之前，不依赖 Hediff、
        /// 不改变意识值、不赋予真正的不死状态、不修改存档 Pawn 数据、不主动复活已死亡
        /// Pawn、不影响正常游戏，也不阻止带有真实伤害来源的死亡，仅作为旧存档加载期间的
        /// 防误杀保险层。
        /// </summary>
        public bool preventMechanoidMechanitorDeathDuringLoad = true;

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
        /// 默认开启。开启后，「正义」会为其召唤的守卫和波次机械族添加机动作战。
        /// 关闭后，正义 BOSS 不会主动添加该状态，但不会移除单位通过其他机制已经获得的机动作战。
        /// 该参数会在每场正义 BOSS 战开始时锁定到本场难度快照。
        /// </summary>
        public bool justiceBossApplyMobileCombatToSummons =
            JusticeBossDifficultyValues.DefaultApplyMobileCombatToSummons;

        // ===== 机械主脑（CerebrexCore）额外 BOSS 技能全局设置（所有存档共享） =====

        /// <summary>
        /// 默认开启。机械主脑额外 BOSS 技能总开关。关闭后，主脑不会自动发动召唤、EMP、
        /// 带宽干扰等额外技能（已在进行中的整轮技能仍会自然结束）。战斗中修改仅影响下一场战斗。
        /// </summary>
        public bool cerebrexBossEnableExtraSkills =
            CerebrexBossDifficultyValues.DefaultEnableExtraSkills;

        public bool cerebrexBossEnableSummoning =
            CerebrexBossDifficultyValues.DefaultEnableSummoning;

        public int cerebrexBossSummonIntervalTicks =
            CerebrexBossDifficultyValues.DefaultSummonIntervalTicks;

        public int cerebrexBossMechsPerWave =
            CerebrexBossDifficultyValues.DefaultMechsPerWave;

        public int cerebrexBossMaxLivingSummonedMechs =
            CerebrexBossDifficultyValues.DefaultMaxLivingSummonedMechs;

        /// <summary>
        /// 默认开启。开启后，主脑召唤的机械族会施加机动作战 Hediff。
        /// 此设置实时影响每场战斗开始时锁定的快照，不纳入单场战斗中途变更。
        /// </summary>
        public bool cerebrexBossApplyMobileCombatToSummons =
            CerebrexBossDifficultyValues.DefaultApplyMobileCombatToSummons;

        public bool cerebrexBossEnableBandwidthInterference =
            CerebrexBossDifficultyValues.DefaultEnableBandwidthInterference;

        public int cerebrexBossBandwidthCooldownMinTicks =
            CerebrexBossDifficultyValues.DefaultBandwidthCooldownMinTicks;

        public int cerebrexBossBandwidthCooldownMaxTicks =
            CerebrexBossDifficultyValues.DefaultBandwidthCooldownMaxTicks;

        public int cerebrexBossBandwidthDurationTicks =
            CerebrexBossDifficultyValues.DefaultBandwidthDurationTicks;

        public int cerebrexBossBandwidthMaxTargets =
            CerebrexBossDifficultyValues.DefaultBandwidthMaxTargets;

        public bool cerebrexBossEnableEmp =
            CerebrexBossDifficultyValues.DefaultEnableEmp;

        public int cerebrexBossEmpCooldownMinTicks =
            CerebrexBossDifficultyValues.DefaultEmpCooldownMinTicks;

        public int cerebrexBossEmpCooldownMaxTicks =
            CerebrexBossDifficultyValues.DefaultEmpCooldownMaxTicks;

        public int cerebrexBossEmpBaseDurationTicks =
            CerebrexBossDifficultyValues.DefaultEmpBaseDurationTicks;

        public float cerebrexBossEmpRadius =
            CerebrexBossDifficultyValues.DefaultEmpRadius;

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
        /// 普通派系前哨防卫强度倍率百分比（10～500，步进 10，默认 100）。
        /// 仅影响之后新创建的前哨：前哨创建时按来源殖民地当前原版威胁点数乘以此倍率
        /// 保存守军预算快照；已存在的前哨不会因调整本设置而改变。
        /// 联合军事行动以此类前哨为目标时，也会读取该前哨保存的防卫预算。
        /// </summary>
        public int factionOutpostGarrisonThreatScalePercent =
            FactionOutpostThreatPointsUtility.DefaultScalePercent;

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

        /// <summary>
        /// 默认开启。开启后，机体骇入禁止以除炼狱魔王(Mech_Diabolus)、战争女皇(Mech_Warqueen)
        /// 之外的机械族 BOSS 为目标。关闭后，恢复原有骇入目标规则，不再因通用 isBoss 规则
        /// 拦截其他 BOSS。
        /// 注意：「正义」-重装指挥单元(MAP_Mech_JusticeBOSS)始终受现有永久限制，不受此开关影响。
        /// </summary>
        public bool restrictMechHackBossTargets = true;

        /// <summary>
        /// 默认开启。开启后，安装了满级心灵中枢的角色每 600 游戏刻（10 秒）将尝试结束
        /// 当前精神状态。该设置只控制「定期结束已存在的精神状态」，不影响心灵中枢基础/
        /// 活化精神力恢复，也不影响满级阶段原有的防止常规精神崩溃（blocksMentalBreaks）。
        /// 在游戏中实时生效，无需重启或重新安装植入体。
        /// </summary>
        public bool enableMaxLevelPsychicCoreMentalStateRecovery = true;

        // ===== 共生盟约全局设置（所有存档共享） =====

        /// <summary>
        /// 共生盟约中信任度与团结度正负变化的成长倍率档位（tenths）。
        /// 5 = ×0.5，10 = ×1.0，20 = ×2.0。
        /// 保存整数档位而不是 float，避免浮点滑块留下 1.0999999 一类无法稳定复现的值。
        /// 只影响之后发生的变化，不追溯修改已有的信任度与团结度。
        /// </summary>
        public int symbiosisCovenantGrowthMultiplierTenths =
            DefaultSymbiosisCovenantGrowthMultiplierTenths;

        public const int MinSymbiosisCovenantGrowthMultiplierTenths = 5;
        public const int MaxSymbiosisCovenantGrowthMultiplierTenths = 20;
        public const int DefaultSymbiosisCovenantGrowthMultiplierTenths = 10;

        /// <summary>
        /// 统一校正普通剧本默认世界内容设置。Classic 只决定这些字段最初采用 Low，
        /// 后续不从 Classic Def 重新读取或同步。
        /// </summary>
        public void NormalizeGeneralScenarioDefaultSettings()
        {
            if ((byte)generalScenarioFactionOutpostFrequency
                > (byte)MechanoidMechanitorFactionOutpostFrequency.High)
            {
                generalScenarioFactionOutpostFrequency =
                    DefaultGeneralScenarioFactionOutpostFrequency;
            }

            if ((byte)generalScenarioMechHiveNodeFrequency
                > (byte)MechanoidMechanitorMechHiveNodeFrequency.High)
            {
                generalScenarioMechHiveNodeFrequency =
                    DefaultGeneralScenarioMechHiveNodeFrequency;
            }
        }

        /// <summary>
        /// 统一钳制共生盟约设置：供 ExposeData 的 PostLoadInit 与
        /// MAPMechanitorMod.WriteSettings 共同调用，确保读取、界面、运行期三层一致。
        /// 这里刻意不重置 goodwill / trade 来源窗口：
        /// 否则玩家可以通过反复切换倍率刷新窗口上限来刷信任。
        /// 从高倍率切到低倍率时，当前窗口已累计的实际值可能暂时超过新上限，
        /// 属于预期行为，等待窗口自然刷新即可。
        /// </summary>
        public void NormalizeSymbiosisCovenantSettings()
        {
            symbiosisCovenantGrowthMultiplierTenths = Mathf.Clamp(
                symbiosisCovenantGrowthMultiplierTenths,
                MinSymbiosisCovenantGrowthMultiplierTenths,
                MaxSymbiosisCovenantGrowthMultiplierTenths);
        }

        /// <summary>
        /// 统一钳制正义 BOSS 难度设置：供 ExposeData 的 PostLoadInit 与
        /// MAPMechanitorMod.WriteSettings 共同调用，确保读取、界面、战斗快照三层一致。
        /// </summary>
        public void NormalizeJusticeBossSettings()
        {
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

        /// <summary>
        /// 统一钳制机械主脑 BOSS 难度设置：供 ExposeData 的 PostLoadInit 与
        /// MAPMechanitorMod.WriteSettings 共同调用，确保读取、界面、战斗快照三层一致。
        /// </summary>
        public void NormalizeCerebrexBossSettings()
        {
            cerebrexBossSummonIntervalTicks =
                CerebrexBossDifficultyValues.ClampSummonIntervalTicks(
                    cerebrexBossSummonIntervalTicks);
            cerebrexBossMechsPerWave =
                CerebrexBossDifficultyValues.ClampMechsPerWave(
                    cerebrexBossMechsPerWave);
            cerebrexBossMaxLivingSummonedMechs =
                CerebrexBossDifficultyValues.ClampMaxLivingSummonedMechs(
                    cerebrexBossMaxLivingSummonedMechs);
            (cerebrexBossBandwidthCooldownMinTicks, cerebrexBossBandwidthCooldownMaxTicks) =
                CerebrexBossDifficultyValues.ClampBandwidthCooldownRange(
                    cerebrexBossBandwidthCooldownMinTicks,
                    cerebrexBossBandwidthCooldownMaxTicks);
            cerebrexBossBandwidthDurationTicks =
                CerebrexBossDifficultyValues.ClampBandwidthDurationTicks(
                    cerebrexBossBandwidthDurationTicks);
            cerebrexBossBandwidthMaxTargets =
                CerebrexBossDifficultyValues.ClampBandwidthMaxTargets(
                    cerebrexBossBandwidthMaxTargets);
            (cerebrexBossEmpCooldownMinTicks, cerebrexBossEmpCooldownMaxTicks) =
                CerebrexBossDifficultyValues.ClampEmpCooldownRange(
                    cerebrexBossEmpCooldownMinTicks,
                    cerebrexBossEmpCooldownMaxTicks);
            cerebrexBossEmpBaseDurationTicks =
                CerebrexBossDifficultyValues.ClampEmpBaseDurationTicks(
                    cerebrexBossEmpBaseDurationTicks);
            cerebrexBossEmpRadius =
                CerebrexBossDifficultyValues.ClampEmpRadius(
                    cerebrexBossEmpRadius);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(
                ref addMechanoidMechanitorsToWorkTab,
                "addMechanoidMechanitorsToWorkTab",
                false);
            Scribe_Values.Look(
                ref enableMechanoidPrioritizedWorkOrders,
                "enableMechanoidPrioritizedWorkOrders",
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
                true);
            Scribe_Values.Look(
                ref enableStoryStylesForGeneralScenarios,
                "enableStoryStylesForGeneralScenarios",
                false);
            Scribe_Values.Look(
                ref generalScenarioFactionOutpostFrequency,
                "generalScenarioFactionOutpostFrequency",
                DefaultGeneralScenarioFactionOutpostFrequency);
            Scribe_Values.Look(
                ref generalScenarioMechHiveNodeFrequency,
                "generalScenarioMechHiveNodeFrequency",
                DefaultGeneralScenarioMechHiveNodeFrequency);
            Scribe_Values.Look(
                ref enableImmediateDraftStateRefresh,
                "enableImmediateDraftStateRefresh",
                true);
            Scribe_Values.Look(
                ref enableLoadDeathDiagnosticLogging,
                "enableLoadDeathDiagnosticLogging",
                false);
            Scribe_Values.Look(
                ref enableCompatibilityDetailedLogging,
                "enableCompatibilityDetailedLogging",
                false);
            Scribe_Values.Look(
                ref preventMechanoidMechanitorDeathDuringLoad,
                "preventMechanoidMechanitorDeathDuringLoad",
                true);
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
                ref justiceBossApplyMobileCombatToSummons,
                "justiceBossApplyMobileCombatToSummons",
                JusticeBossDifficultyValues.DefaultApplyMobileCombatToSummons);

            Scribe_Values.Look(
                ref cerebrexBossEnableExtraSkills,
                "cerebrexBossEnableExtraSkills",
                CerebrexBossDifficultyValues.DefaultEnableExtraSkills);
            Scribe_Values.Look(
                ref cerebrexBossEnableSummoning,
                "cerebrexBossEnableSummoning",
                CerebrexBossDifficultyValues.DefaultEnableSummoning);
            Scribe_Values.Look(
                ref cerebrexBossSummonIntervalTicks,
                "cerebrexBossSummonIntervalTicks",
                CerebrexBossDifficultyValues.DefaultSummonIntervalTicks);
            Scribe_Values.Look(
                ref cerebrexBossMechsPerWave,
                "cerebrexBossMechsPerWave",
                CerebrexBossDifficultyValues.DefaultMechsPerWave);
            Scribe_Values.Look(
                ref cerebrexBossMaxLivingSummonedMechs,
                "cerebrexBossMaxLivingSummonedMechs",
                CerebrexBossDifficultyValues.DefaultMaxLivingSummonedMechs);
            Scribe_Values.Look(
                ref cerebrexBossApplyMobileCombatToSummons,
                "cerebrexBossApplyMobileCombatToSummons",
                CerebrexBossDifficultyValues.DefaultApplyMobileCombatToSummons);
            Scribe_Values.Look(
                ref cerebrexBossEnableBandwidthInterference,
                "cerebrexBossEnableBandwidthInterference",
                CerebrexBossDifficultyValues.DefaultEnableBandwidthInterference);
            Scribe_Values.Look(
                ref cerebrexBossBandwidthCooldownMinTicks,
                "cerebrexBossBandwidthCooldownMinTicks",
                CerebrexBossDifficultyValues.DefaultBandwidthCooldownMinTicks);
            Scribe_Values.Look(
                ref cerebrexBossBandwidthCooldownMaxTicks,
                "cerebrexBossBandwidthCooldownMaxTicks",
                CerebrexBossDifficultyValues.DefaultBandwidthCooldownMaxTicks);
            Scribe_Values.Look(
                ref cerebrexBossBandwidthDurationTicks,
                "cerebrexBossBandwidthDurationTicks",
                CerebrexBossDifficultyValues.DefaultBandwidthDurationTicks);
            Scribe_Values.Look(
                ref cerebrexBossBandwidthMaxTargets,
                "cerebrexBossBandwidthMaxTargets",
                CerebrexBossDifficultyValues.DefaultBandwidthMaxTargets);
            Scribe_Values.Look(
                ref cerebrexBossEnableEmp,
                "cerebrexBossEnableEmp",
                CerebrexBossDifficultyValues.DefaultEnableEmp);
            Scribe_Values.Look(
                ref cerebrexBossEmpCooldownMinTicks,
                "cerebrexBossEmpCooldownMinTicks",
                CerebrexBossDifficultyValues.DefaultEmpCooldownMinTicks);
            Scribe_Values.Look(
                ref cerebrexBossEmpCooldownMaxTicks,
                "cerebrexBossEmpCooldownMaxTicks",
                CerebrexBossDifficultyValues.DefaultEmpCooldownMaxTicks);
            Scribe_Values.Look(
                ref cerebrexBossEmpBaseDurationTicks,
                "cerebrexBossEmpBaseDurationTicks",
                CerebrexBossDifficultyValues.DefaultEmpBaseDurationTicks);
            Scribe_Values.Look(
                ref cerebrexBossEmpRadius,
                "cerebrexBossEmpRadius",
                CerebrexBossDifficultyValues.DefaultEmpRadius);

            Scribe_Values.Look(
                ref factionOutpostRaidChancePercent,
                "factionOutpostRaidChancePercent",
                DefaultFactionOutpostRaidChancePercent);
            Scribe_Values.Look(
                ref factionOutpostSupportChancePercent,
                "factionOutpostSupportChancePercent",
                DefaultFactionOutpostSupportChancePercent);
            Scribe_Values.Look(
                ref factionOutpostGarrisonThreatScalePercent,
                "factionOutpostGarrisonThreatScalePercent",
                FactionOutpostThreatPointsUtility.DefaultScalePercent);
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

            Scribe_Values.Look(
                ref restrictMechHackBossTargets,
                "restrictMechHackBossTargets",
                true);

            Scribe_Values.Look(
                ref enableMaxLevelPsychicCoreMentalStateRecovery,
                "enableMaxLevelPsychicCoreMentalStateRecovery",
                true);

            Scribe_Values.Look(
                ref symbiosisCovenantGrowthMultiplierTenths,
                "symbiosisCovenantGrowthMultiplierTenths",
                DefaultSymbiosisCovenantGrowthMultiplierTenths);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                NormalizeGeneralScenarioDefaultSettings();
                NormalizeSymbiosisCovenantSettings();

                productivityCoreWorkSpeedOffsetPercentPerLevel = Mathf.Clamp(
                    productivityCoreWorkSpeedOffsetPercentPerLevel,
                    ProductivityCoreUtility.MinWorkSpeedOffsetPercentPerLevel,
                    ProductivityCoreUtility.MaxWorkSpeedOffsetPercentPerLevel);

                NormalizeJusticeBossSettings();
                NormalizeCerebrexBossSettings();

                factionOutpostRaidChancePercent =
                    Mathf.Clamp(factionOutpostRaidChancePercent, 0, 100);
                factionOutpostSupportChancePercent =
                    Mathf.Clamp(factionOutpostSupportChancePercent, 0, 100);
                factionOutpostGarrisonThreatScalePercent =
                    FactionOutpostThreatPointsUtility.ClampScalePercent(
                        factionOutpostGarrisonThreatScalePercent);
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
