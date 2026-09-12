using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_CerebrexBossController : CompProperties
    {
        public CompProperties_CerebrexBossController()
        {
            compClass = typeof(CompCerebrexBossController);
        }
    }

    /// <summary>
    /// 主脑（CerebrexCore）独立战斗控制器。所有战斗状态都由本组件保存在主脑建筑实例上，
    /// 不写入 MapComponent / GameComponent / WorldComponent / 任务组件。
    /// 对原版主脑的地图生成、稳定器、900 tick 互动、结局逻辑一律不修改。
    /// 额外战斗技能参数在新行为开始时读取当前 MOD 设置；已经排定的冷却、已经开始的
    /// 召唤批次、EMP 与带宽干扰保持各自的当前行为状态，不因中途修改设置而反向变化。
    /// </summary>
    public sealed class CompCerebrexBossController : ThingComp
    {
        public const int FirstSummonDelayTicks = 1200;

        public const int DropRetryDelayTicks = 250;
        public const int MaxDropRetryAttempts = 20;

        public const float EmpPropagationSpeed = 1f;
        public const float OriginalShockwaveRadius = 30.9f;

        public const int EmpWarningRiseTicks = 30;
        public const int EmpWarningHoldTicks = 60;
        public const int EmpWarningSlamTicks = 20;
        public const int EmpWarningRecoveryTicks = 20;
        public const float EmpWarningPeakZOffset = 2.65f;
        public const float EmpWarningImpactZOffset = 1.85f;
        public const float OriginalBrainBaseZOffset = 2f;
        public const float OriginalBrainBobHeight = 0.35f;
        public const int OriginalBrainBobPeriodTicks = 300;

        // 旧存档兼容标记。difficulty 字段不再代表整场 BOSS 战快照，
        // 仅作为最近一次从 ModSettings 刷新的合法值缓存。
        private bool cerebrexBossSettingsCaptured;
        private bool difficultyEnableExtraSkills;
        private bool difficultyEnableSummoning;
        private int difficultySummonIntervalTicks;
        private int difficultyMechsPerWave;
        private int difficultyMaxLivingSummonedMechs;
        private bool difficultyApplyMobileCombatToSummons;
        private bool difficultyEnableBandwidthInterference;
        private int difficultyBandwidthCooldownMinTicks;
        private int difficultyBandwidthCooldownMaxTicks;
        private int difficultyBandwidthDurationTicks;
        private int difficultyBandwidthMaxTargets;
        private bool difficultyEnableEmp;
        private int difficultyEmpCooldownMinTicks;
        private int difficultyEmpCooldownMaxTicks;
        private int difficultyEmpBaseDurationTicks;
        private float difficultyEmpRadius;

        private bool initialized;
        private bool stopped;

        // 玩家存在性检查缓存（仅运行时，不写存档）：最多每30 Tick 扫描一次。
        private int nextPlayerPresenceCheckTick;
        private bool cachedPlayerPawnPresent;

        private const int PlayerPresenceCheckIntervalTicks = 30;

        private int nextSummonTick;
        private int nextEmpTick;
        private int nextBandwidthTick;

        private List<Pawn> summonedMechs = new List<Pawn>();
        private List<PawnKindDef> pendingSummonKinds = new List<PawnKindDef>();
        private int summonDropRetryCount;
        private Lord? assaultLord;

        // 当前召唤批次局部锁定：同一批失败重试不能因设置中途变化而改变上限或机动作战。
        private int summonBatchMaxLivingSummonedMechs =
            CerebrexBossDifficultyValues.DefaultMaxLivingSummonedMechs;
        private bool summonBatchApplyMobileCombatToSummons =
            CerebrexBossDifficultyValues.DefaultApplyMobileCombatToSummons;
        private bool summonBatchSettingsCaptured;

        private List<PendingCerebrexEmpHit> pendingEmpHits = new List<PendingCerebrexEmpHit>();
        private Dictionary<Thing, int> disabledPowerBuildings = new Dictionary<Thing, int>();

        private bool empWarningActive;
        private int empWarningStartTick;
        private int empShockwaveReleaseTick;
        private int empWarningEndTick;
        private bool empShockwaveReleased;

        // 当前 EMP 局部锁定：从预警开始到释放都使用同一组半径/持续时间；
        // 冲击波释放后，每个 PendingCerebrexEmpHit 继续保存自己的释放参数。
        private float empCastRadius = CerebrexBossDifficultyValues.DefaultEmpRadius;
        private int empCastBaseDurationTicks = CerebrexBossDifficultyValues.DefaultEmpBaseDurationTicks;
        private bool empCastSettingsCaptured;

        // 仅运行时标记：PostLoadInit 纯数据规范阶段因 EMP 时间字段结构损坏取消了本轮预警，
        // 需要在主脑真正 Spawn 后补排下一发冷却。不写入存档，不改变 Scribe key。
        private bool empWarningInvalidatedOnLoad;

        // 多目标带宽干扰状态（本场战斗）
        private bool bandwidthInterferenceActive;
        private List<CerebrexBandwidthTargetRecord> bandwidthTargetRecords =
            new List<CerebrexBandwidthTargetRecord>();
        private List<CerebrexBandwidthBerserkRecord> bandwidthBerserkRecords =
            new List<CerebrexBandwidthBerserkRecord>();
        private List<Pawn> bandwidthTargetsUsedThisBattle = new List<Pawn>();
        private int bandwidthInterferenceEndTick;

        // 本轮带宽干扰曾经影响过的全部原监管者。整轮结束后统一清空；
        // 单个目标失效时不移除，不依赖当前目标仍存在来决定是否需要通知监管者。
        private List<Pawn> bandwidthAffectedOverseers = new List<Pawn>();

        // 旧存档单目标状态（仅用于读档迁移）。这些键仍通过 Scribe 写出（迁移后为空），
        // 读档时若旧存档携带有效值则迁移为多目标记录并清空。
        private Pawn? legacyBandwidthTarget;
        private Pawn? legacyBandwidthOverseer;
        private List<Pawn>? legacyBandwidthBerserkPawns;

        // 纯客户端瞬态视觉状态：不写入存档，不参与任何目标、狂暴或结束判定。
        private readonly CerebrexBandwidthVisuals bandwidthVisuals = new CerebrexBandwidthVisuals();

        public Lord? GetAssaultLord() => assaultLord;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            // 主脑生成/读档/换图后下一 Tick 立即重新计算玩家存在性。
            nextPlayerPresenceCheckTick = 0;
            cachedPlayerPawnPresent = false;

            if (!initialized)
            {
                InitializeTimers();
                initialized = true;
            }

            if (respawningAfterLoad)
            {
                ReconcileCoreCombatStateAfterLoad();
                ReconcileCombatStateAfterSpawn();
            }
        }

        private void ReconcileCoreCombatStateAfterLoad()
        {
            if (!ModsConfig.OdysseyActive || parent == null || parent.Destroyed)
            {
                return;
            }

            CompCerebrexCore? core = parent.TryGetComp<CompCerebrexCore>();
            if (core == null)
            {
                return;
            }

            AcceptanceReport canInteract = core.CanInteract();
            if (canInteract.Accepted)
            {
                Notify_CoreDefencesLowered();
            }
        }

        private void InitializeTimers()
        {
            RefreshDifficultySettings();

            int now = Find.TickManager.TicksGame;
            nextSummonTick = now + FirstSummonDelayTicks;
            nextEmpTick = now + Rand.RangeInclusive(
                difficultyEmpCooldownMinTicks,
                difficultyEmpCooldownMaxTicks);
            nextBandwidthTick = now + Rand.RangeInclusive(
                difficultyBandwidthCooldownMinTicks,
                difficultyBandwidthCooldownMaxTicks);
        }

        // ----------------------------------------------------------------
        // 实时难度设置
        // ----------------------------------------------------------------

        private void RefreshDifficultySettings()
        {
            MAPMechanitorModSettings? settings = MAPMechanitorMod.Settings;
            if (settings == null)
            {
                ApplyDefaultDifficultySettings();
                return;
            }

            difficultyEnableExtraSkills = settings.cerebrexBossEnableExtraSkills;
            difficultyEnableSummoning = settings.cerebrexBossEnableSummoning;
            difficultySummonIntervalTicks =
                CerebrexBossDifficultyValues.ClampSummonIntervalTicks(
                    settings.cerebrexBossSummonIntervalTicks);
            difficultyMechsPerWave =
                CerebrexBossDifficultyValues.ClampMechsPerWave(
                    settings.cerebrexBossMechsPerWave);
            difficultyMaxLivingSummonedMechs =
                CerebrexBossDifficultyValues.ClampMaxLivingSummonedMechs(
                    settings.cerebrexBossMaxLivingSummonedMechs);
            difficultyApplyMobileCombatToSummons =
                settings.cerebrexBossApplyMobileCombatToSummons;
            difficultyEnableBandwidthInterference =
                settings.cerebrexBossEnableBandwidthInterference;

            (int bwMin, int bwMax) = CerebrexBossDifficultyValues.ClampBandwidthCooldownRange(
                settings.cerebrexBossBandwidthCooldownMinTicks,
                settings.cerebrexBossBandwidthCooldownMaxTicks);
            difficultyBandwidthCooldownMinTicks = bwMin;
            difficultyBandwidthCooldownMaxTicks = bwMax;

            difficultyBandwidthDurationTicks =
                CerebrexBossDifficultyValues.ClampBandwidthDurationTicks(
                    settings.cerebrexBossBandwidthDurationTicks);
            difficultyBandwidthMaxTargets =
                CerebrexBossDifficultyValues.ClampBandwidthMaxTargets(
                    settings.cerebrexBossBandwidthMaxTargets);
            difficultyEnableEmp = settings.cerebrexBossEnableEmp;

            (int empMin, int empMax) = CerebrexBossDifficultyValues.ClampEmpCooldownRange(
                settings.cerebrexBossEmpCooldownMinTicks,
                settings.cerebrexBossEmpCooldownMaxTicks);
            difficultyEmpCooldownMinTicks = empMin;
            difficultyEmpCooldownMaxTicks = empMax;

            difficultyEmpBaseDurationTicks =
                CerebrexBossDifficultyValues.ClampEmpBaseDurationTicks(
                    settings.cerebrexBossEmpBaseDurationTicks);
            difficultyEmpRadius =
                CerebrexBossDifficultyValues.ClampEmpRadius(
                    settings.cerebrexBossEmpRadius);

            cerebrexBossSettingsCaptured = true;
        }

        private void ApplyDefaultDifficultySettings()
        {
            difficultyEnableExtraSkills =
                CerebrexBossDifficultyValues.DefaultEnableExtraSkills;
            difficultyEnableSummoning =
                CerebrexBossDifficultyValues.DefaultEnableSummoning;
            difficultySummonIntervalTicks =
                CerebrexBossDifficultyValues.DefaultSummonIntervalTicks;
            difficultyMechsPerWave =
                CerebrexBossDifficultyValues.DefaultMechsPerWave;
            difficultyMaxLivingSummonedMechs =
                CerebrexBossDifficultyValues.DefaultMaxLivingSummonedMechs;
            difficultyApplyMobileCombatToSummons =
                CerebrexBossDifficultyValues.DefaultApplyMobileCombatToSummons;
            difficultyEnableBandwidthInterference =
                CerebrexBossDifficultyValues.DefaultEnableBandwidthInterference;
            difficultyBandwidthCooldownMinTicks =
                CerebrexBossDifficultyValues.DefaultBandwidthCooldownMinTicks;
            difficultyBandwidthCooldownMaxTicks =
                CerebrexBossDifficultyValues.DefaultBandwidthCooldownMaxTicks;
            difficultyBandwidthDurationTicks =
                CerebrexBossDifficultyValues.DefaultBandwidthDurationTicks;
            difficultyBandwidthMaxTargets =
                CerebrexBossDifficultyValues.DefaultBandwidthMaxTargets;
            difficultyEnableEmp =
                CerebrexBossDifficultyValues.DefaultEnableEmp;
            difficultyEmpCooldownMinTicks =
                CerebrexBossDifficultyValues.DefaultEmpCooldownMinTicks;
            difficultyEmpCooldownMaxTicks =
                CerebrexBossDifficultyValues.DefaultEmpCooldownMaxTicks;
            difficultyEmpBaseDurationTicks =
                CerebrexBossDifficultyValues.DefaultEmpBaseDurationTicks;
            difficultyEmpRadius =
                CerebrexBossDifficultyValues.DefaultEmpRadius;

            cerebrexBossSettingsCaptured = true;
        }

        private void ClampDifficultyCache()
        {
            difficultySummonIntervalTicks =
                CerebrexBossDifficultyValues.ClampSummonIntervalTicks(
                    difficultySummonIntervalTicks);
            difficultyMechsPerWave =
                CerebrexBossDifficultyValues.ClampMechsPerWave(
                    difficultyMechsPerWave);
            difficultyMaxLivingSummonedMechs =
                CerebrexBossDifficultyValues.ClampMaxLivingSummonedMechs(
                    difficultyMaxLivingSummonedMechs);

            (int bwMin, int bwMax) = CerebrexBossDifficultyValues.ClampBandwidthCooldownRange(
                difficultyBandwidthCooldownMinTicks,
                difficultyBandwidthCooldownMaxTicks);
            difficultyBandwidthCooldownMinTicks = bwMin;
            difficultyBandwidthCooldownMaxTicks = bwMax;

            difficultyBandwidthDurationTicks =
                CerebrexBossDifficultyValues.ClampBandwidthDurationTicks(
                    difficultyBandwidthDurationTicks);
            difficultyBandwidthMaxTargets =
                CerebrexBossDifficultyValues.ClampBandwidthMaxTargets(
                    difficultyBandwidthMaxTargets);

            (int empMin, int empMax) = CerebrexBossDifficultyValues.ClampEmpCooldownRange(
                difficultyEmpCooldownMinTicks,
                difficultyEmpCooldownMaxTicks);
            difficultyEmpCooldownMinTicks = empMin;
            difficultyEmpCooldownMaxTicks = empMax;

            difficultyEmpBaseDurationTicks =
                CerebrexBossDifficultyValues.ClampEmpBaseDurationTicks(
                    difficultyEmpBaseDurationTicks);
            difficultyEmpRadius =
                CerebrexBossDifficultyValues.ClampEmpRadius(difficultyEmpRadius);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref initialized, "initialized", false);
            Scribe_Values.Look(ref stopped, "stopped", false);
            Scribe_Values.Look(ref nextSummonTick, "nextSummonTick", 0);
            Scribe_Values.Look(ref nextEmpTick, "nextEmpTick", 0);
            Scribe_Values.Look(ref nextBandwidthTick, "nextBandwidthTick", 0);
            Scribe_Collections.Look(ref summonedMechs, "summonedMechs", LookMode.Reference);
            Scribe_Collections.Look(ref pendingSummonKinds, "pendingSummonKinds", LookMode.Def);
            Scribe_Values.Look(ref summonDropRetryCount, "summonDropRetryCount", 0);
            Scribe_References.Look(ref assaultLord, "assaultLord");
            Scribe_Values.Look(
                ref summonBatchMaxLivingSummonedMechs,
                "summonBatchMaxLivingSummonedMechs",
                CerebrexBossDifficultyValues.DefaultMaxLivingSummonedMechs);
            Scribe_Values.Look(
                ref summonBatchApplyMobileCombatToSummons,
                "summonBatchApplyMobileCombatToSummons",
                CerebrexBossDifficultyValues.DefaultApplyMobileCombatToSummons);
            Scribe_Values.Look(ref summonBatchSettingsCaptured, "summonBatchSettingsCaptured", false);
            Scribe_Collections.Look(ref pendingEmpHits, "pendingEmpHits", LookMode.Deep);
            Scribe_Collections.Look(ref disabledPowerBuildings, "disabledPowerBuildings", LookMode.Reference, LookMode.Value);
            Scribe_Values.Look(ref empWarningActive, "empWarningActive", false);
            Scribe_Values.Look(ref empWarningStartTick, "empWarningStartTick", 0);
            Scribe_Values.Look(ref empShockwaveReleaseTick, "empShockwaveReleaseTick", 0);
            Scribe_Values.Look(ref empWarningEndTick, "empWarningEndTick", 0);
            Scribe_Values.Look(ref empShockwaveReleased, "empShockwaveReleased", false);
            Scribe_Values.Look(
                ref empCastRadius,
                "empCastRadius",
                CerebrexBossDifficultyValues.DefaultEmpRadius);
            Scribe_Values.Look(
                ref empCastBaseDurationTicks,
                "empCastBaseDurationTicks",
                CerebrexBossDifficultyValues.DefaultEmpBaseDurationTicks);
            Scribe_Values.Look(ref empCastSettingsCaptured, "empCastSettingsCaptured", false);

            // 旧存档单目标带宽状态（仅用于迁移；新存档不写入这些键）
            Scribe_Values.Look(ref bandwidthInterferenceActive, "bandwidthInterferenceActive", false);
            Scribe_References.Look(ref legacyBandwidthTarget, "bandwidthTarget");
            Scribe_References.Look(ref legacyBandwidthOverseer, "bandwidthOverseer");
            Scribe_Values.Look(ref bandwidthInterferenceEndTick, "bandwidthInterferenceEndTick", 0);
            Scribe_Collections.Look(ref legacyBandwidthBerserkPawns, "bandwidthBerserkPawns", LookMode.Reference);
            Scribe_Collections.Look(ref bandwidthTargetsUsedThisBattle, "bandwidthTargetsUsedThisBattle", LookMode.Reference);
            Scribe_Collections.Look(ref bandwidthAffectedOverseers, "bandwidthAffectedOverseers", LookMode.Reference);

            // 新多目标带宽状态
            Scribe_Collections.Look(ref bandwidthTargetRecords, "bandwidthTargetRecords", LookMode.Deep);
            Scribe_Collections.Look(ref bandwidthBerserkRecords, "bandwidthBerserkRecords", LookMode.Deep);

            // 保留旧 difficulty 键用于旧存档读取与回滚兼容；正常战斗会实时刷新这些缓存。
            Scribe_Values.Look(ref cerebrexBossSettingsCaptured, "cerebrexBossSettingsCaptured", false);
            Scribe_Values.Look(ref difficultyEnableExtraSkills, "difficultyEnableExtraSkills", CerebrexBossDifficultyValues.DefaultEnableExtraSkills);
            Scribe_Values.Look(ref difficultyEnableSummoning, "difficultyEnableSummoning", CerebrexBossDifficultyValues.DefaultEnableSummoning);
            Scribe_Values.Look(ref difficultySummonIntervalTicks, "difficultySummonIntervalTicks", CerebrexBossDifficultyValues.DefaultSummonIntervalTicks);
            Scribe_Values.Look(ref difficultyMechsPerWave, "difficultyMechsPerWave", CerebrexBossDifficultyValues.DefaultMechsPerWave);
            Scribe_Values.Look(ref difficultyMaxLivingSummonedMechs, "difficultyMaxLivingSummonedMechs", CerebrexBossDifficultyValues.DefaultMaxLivingSummonedMechs);
            Scribe_Values.Look(ref difficultyApplyMobileCombatToSummons, "difficultyApplyMobileCombatToSummons", CerebrexBossDifficultyValues.DefaultApplyMobileCombatToSummons);
            Scribe_Values.Look(ref difficultyEnableBandwidthInterference, "difficultyEnableBandwidthInterference", CerebrexBossDifficultyValues.DefaultEnableBandwidthInterference);
            Scribe_Values.Look(ref difficultyBandwidthCooldownMinTicks, "difficultyBandwidthCooldownMinTicks", CerebrexBossDifficultyValues.DefaultBandwidthCooldownMinTicks);
            Scribe_Values.Look(ref difficultyBandwidthCooldownMaxTicks, "difficultyBandwidthCooldownMaxTicks", CerebrexBossDifficultyValues.DefaultBandwidthCooldownMaxTicks);
            Scribe_Values.Look(ref difficultyBandwidthDurationTicks, "difficultyBandwidthDurationTicks", CerebrexBossDifficultyValues.DefaultBandwidthDurationTicks);
            Scribe_Values.Look(ref difficultyBandwidthMaxTargets, "difficultyBandwidthMaxTargets", CerebrexBossDifficultyValues.DefaultBandwidthMaxTargets);
            Scribe_Values.Look(ref difficultyEnableEmp, "difficultyEnableEmp", CerebrexBossDifficultyValues.DefaultEnableEmp);
            Scribe_Values.Look(ref difficultyEmpCooldownMinTicks, "difficultyEmpCooldownMinTicks", CerebrexBossDifficultyValues.DefaultEmpCooldownMinTicks);
            Scribe_Values.Look(ref difficultyEmpCooldownMaxTicks, "difficultyEmpCooldownMaxTicks", CerebrexBossDifficultyValues.DefaultEmpCooldownMaxTicks);
            Scribe_Values.Look(ref difficultyEmpBaseDurationTicks, "difficultyEmpBaseDurationTicks", CerebrexBossDifficultyValues.DefaultEmpBaseDurationTicks);
            Scribe_Values.Look(ref difficultyEmpRadius, "difficultyEmpRadius", CerebrexBossDifficultyValues.DefaultEmpRadius);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                NormalizeLoadedPersistentState();
            }
        }

        /// <summary>
        /// PostLoadInit 阶段：只做完全依赖存档数据自身的规范化（null 清理、旧数据迁移、
        /// 数值钳制、纯数学结构校验等）。此时地图 Thing 尚未 Spawn，parent.Spawned == false、
        /// parent.Map == null 是合法暂态，禁止据此判定 EMP / 带宽干扰损坏或提前结束。
        /// 依赖 Spawn / Map / Lord / Pawn 运行时状态的对账见 ReconcileCombatStateAfterSpawn()。
        /// </summary>
        private void NormalizeLoadedPersistentState()
        {
            empWarningInvalidatedOnLoad = false;

            summonedMechs ??= new List<Pawn>();
            pendingSummonKinds ??= new List<PawnKindDef>();
            pendingEmpHits ??= new List<PendingCerebrexEmpHit>();
            bandwidthTargetRecords ??= new List<CerebrexBandwidthTargetRecord>();
            bandwidthBerserkRecords ??= new List<CerebrexBandwidthBerserkRecord>();
            bandwidthTargetsUsedThisBattle ??= new List<Pawn>();
            disabledPowerBuildings ??= new Dictionary<Thing, int>();
            bandwidthVisuals.ResetTransientState();

            // 先读取并钳制旧存档保存的 difficulty 值，用于恢复已经开始的动作；
            // 随后再刷新当前 ModSettings，后续新行为全部使用实时设置。
            if (!cerebrexBossSettingsCaptured)
            {
                ApplyDefaultDifficultySettings();
            }
            else
            {
                ClampDifficultyCache();
            }

            if (pendingSummonKinds.Count > 0 && !summonBatchSettingsCaptured)
            {
                summonBatchMaxLivingSummonedMechs = difficultyMaxLivingSummonedMechs;
                summonBatchApplyMobileCombatToSummons = difficultyApplyMobileCombatToSummons;
                summonBatchSettingsCaptured = true;
            }
            else if (pendingSummonKinds.Count == 0)
            {
                summonBatchSettingsCaptured = false;
            }

            if (empWarningActive && !empCastSettingsCaptured)
            {
                empCastRadius = difficultyEmpRadius;
                empCastBaseDurationTicks = difficultyEmpBaseDurationTicks;
                empCastSettingsCaptured = true;
            }

            foreach (PendingCerebrexEmpHit? hit in pendingEmpHits)
            {
                if (hit == null)
                {
                    continue;
                }

                if (hit.radiusAtRelease <= 0f)
                {
                    hit.radiusAtRelease = difficultyEmpRadius;
                }

                if (hit.baseDurationTicksAtRelease <= 0)
                {
                    hit.baseDurationTicksAtRelease = difficultyEmpBaseDurationTicks;
                }
            }

            RefreshDifficultySettings();

            bandwidthAffectedOverseers ??= new List<Pawn>();

            // 旧存档单目标状态迁移为多目标记录。
            if (bandwidthInterferenceActive
                && bandwidthTargetRecords.Count == 0
                && legacyBandwidthTarget != null)
            {
                bandwidthTargetRecords.Add(
                    new CerebrexBandwidthTargetRecord(
                        legacyBandwidthTarget,
                        legacyBandwidthOverseer));
            }

            if (legacyBandwidthBerserkPawns != null)
            {
                foreach (Pawn p in legacyBandwidthBerserkPawns)
                {
                    if (p != null
                        && !bandwidthBerserkRecords.Any(r => r.pawn == p))
                    {
                        bandwidthBerserkRecords.Add(
                            new CerebrexBandwidthBerserkRecord(
                                p,
                                legacyBandwidthOverseer));
                    }
                }
            }

            legacyBandwidthTarget = null;
            legacyBandwidthOverseer = null;
            legacyBandwidthBerserkPawns = null;

            // 从迁移后的记录补全受影响监管者列表（旧单目标原监管者一并纳入），
            // 供整轮结束前统一通知，避免目标记录提前清理后丢失监管者。
            foreach (CerebrexBandwidthTargetRecord? rec in bandwidthTargetRecords)
            {
                if (rec?.originalOverseer != null
                    && !bandwidthAffectedOverseers.Contains(rec.originalOverseer))
                {
                    bandwidthAffectedOverseers.Add(rec.originalOverseer);
                }
            }

            foreach (CerebrexBandwidthBerserkRecord? rec in bandwidthBerserkRecords)
            {
                if (rec?.originalOverseer != null
                    && !bandwidthAffectedOverseers.Contains(rec.originalOverseer))
                {
                    bandwidthAffectedOverseers.Add(rec.originalOverseer);
                }
            }

            // 注意：bandwidthBerserkRecords / bandwidthTargetsUsedThisBattle 不得因 Pawn
            // 死亡、倒地或离开地图而在此处删除（死亡 Pawn 可能复活，目标死亡后
            // 仍属于“本场战斗已选择过”记录）。仅移除明确为 null 或永久 Discarded 的引用。
            summonedMechs.RemoveAll(p => p == null || p.Dead || p.Destroyed || p.Discarded);
            bandwidthTargetRecords.RemoveAll(r => r == null);
            bandwidthBerserkRecords.RemoveAll(r => r == null || r.pawn == null || r.pawn.Discarded);
            bandwidthAffectedOverseers.RemoveAll(p => p == null || p.Discarded);
            bandwidthTargetsUsedThisBattle.RemoveAll(p => p == null || p.Discarded);
            pendingEmpHits.RemoveAll(h => h == null || h.target == null || h.target.Destroyed || h.target.Discarded);

            int nowTicks = Find.TickManager.TicksGame;
            List<Thing> expired = new List<Thing>();
            foreach (KeyValuePair<Thing, int> kv in disabledPowerBuildings)
            {
                if (kv.Key == null || kv.Key.Destroyed)
                {
                    if (kv.Key != null)
                    {
                        expired.Add(kv.Key);
                    }

                    continue;
                }

                // 截止 Tick 已过：不再注册到运行时缓存，并从主脑自己的持久字典清理。
                if (kv.Value <= nowTicks)
                {
                    expired.Add(kv.Key);
                    continue;
                }

                CerebrexPowerDisruptionUtility.Register(kv.Key, kv.Value);
            }

            foreach (Thing t in expired)
            {
                disabledPowerBuildings.Remove(t);
            }

            disabledPowerBuildings.Remove(null!);

            // 纯存档数据的结构校验：只检查 EMP 时间字段自身是否自洽。
            // 此阶段地图 Thing 尚未 Spawn，禁止用 Spawned / Map / stopped 判断可否继续；
            // 运行时对账统一交给 PostSpawnSetup(respawningAfterLoad)。
            if (empWarningActive)
            {
                int expectedReleaseTick = empWarningStartTick
                    + EmpWarningRiseTicks
                    + EmpWarningHoldTicks
                    + EmpWarningSlamTicks;
                int expectedEndTick = expectedReleaseTick + EmpWarningRecoveryTicks;
                bool empStateStructurallyBroken = empWarningStartTick <= 0
                    || empShockwaveReleaseTick != expectedReleaseTick
                    || empWarningEndTick != expectedEndTick;

                if (empStateStructurallyBroken)
                {
                    // 结构损坏：仅做数据层取消，Spawn 后由对账补排冷却。
                    empWarningInvalidatedOnLoad = true;
                    ResetEmpWarningState();
                }
            }
            else
            {
                ResetEmpWarningState();
            }

            // 读档后发现没有待重试单位但重试计数残留，重置以免新一波继承旧计数。
            if (pendingSummonKinds.Count == 0)
            {
                if (summonDropRetryCount != 0)
                {
                    summonDropRetryCount = 0;
                }

                summonBatchSettingsCaptured = false;
            }
        }

        /// <summary>
        /// PostSpawnSetup(respawningAfterLoad) 阶段才允许执行的运行时对账：依赖
        /// parent.Spawned / parent.Map / Lord / 地图实体 / Pawn 当前状态。
        /// PostLoadInit 阶段绝对不得调用；非 respawning 生成也不调用。
        /// </summary>
        private void ReconcileCombatStateAfterSpawn()
        {
            if (parent == null || parent.Destroyed || !parent.Spawned || parent.Map == null)
            {
                return;
            }

            int nowTicks = Find.TickManager.TicksGame;

            // 1. EMP：存档中已经开始的整轮优先继续，只有真正不可继续（DLC 关闭 / 战斗停止）
            //    才取消；取消后补排下一发冷却，不重新随机本轮已经锁定的 EMP 参数。
            if (empWarningInvalidatedOnLoad)
            {
                empWarningInvalidatedOnLoad = false;
                if (!stopped && ModsConfig.OdysseyActive)
                {
                    RefreshDifficultySettings();
                    nextEmpTick = nowTicks + Rand.RangeInclusive(
                        difficultyEmpCooldownMinTicks,
                        difficultyEmpCooldownMaxTicks);
                }
            }

            if (empWarningActive && (!ModsConfig.OdysseyActive || stopped))
            {
                ResetEmpWarningState();
                if (!stopped && ModsConfig.OdysseyActive)
                {
                    RefreshDifficultySettings();
                    nextEmpTick = nowTicks + Rand.RangeInclusive(
                        difficultyEmpCooldownMinTicks,
                        difficultyEmpCooldownMaxTicks);
                }
            }

            // 2. 带宽干扰：Spawn 后只清理真正失效项，保留仍有效目标与当前轮结束 Tick；
            //    只有现有正常结束条件真正成立时才结束整轮，禁止因加载阶段无 Map 而结束。
            if (bandwidthInterferenceActive)
            {
                bool baseBroken = !ModsConfig.OdysseyActive
                    || stopped
                    || bandwidthTargetRecords.Count == 0;

                if (baseBroken)
                {
                    // 基础条件不满足：直接结束整轮，不启动新冷却。
                    EndBandwidthInterference(startCooldown: false, reason: "loadfix");
                }
                else
                {
                    // 与正常 Tick 共用的逐目标清理：保留其他有效目标，仅清理失效目标并通知其监管者；
                    // 不得因为单个目标失效而结束全部目标。
                    CleanupInvalidTargetRecords();

                    // 清理不可恢复的 null / Discarded 狂暴记录（不通知）。
                    bandwidthBerserkRecords.RemoveAll(r => r == null || r.pawn == null || r.pawn.Discarded);

                    if (ShouldEndBandwidthInterferenceForPawnState())
                    {
                        // 全部目标与本轮狂暴单位均无行动能力：正常结束并进入冷却。
                        EndBandwidthInterference(startCooldown: true, reason: "loadfix");
                    }
                    else
                    {
                        // 仍有有效状态：保持整轮运行，瞬态视觉将在 Tick 中重建。
                    }
                }
            }

            if (!bandwidthInterferenceActive)
            {
                // 残留清理：移除可能的干扰 Hediff 与隐藏狂暴标记，尝试恢复本 MOD 技能导致的狂暴，
                // 然后去重通知所有相关原监管者刷新带宽。不得播放恢复视觉、不得启动冷却。
                HediffDef? leftoverDef = DefDatabase<HediffDef>.GetNamedSilentFail("MAP_CerebrexBandwidthInterference");
                HediffDef? leftoverMarker = DefDatabase<HediffDef>.GetNamedSilentFail("MAP_CerebrexBandwidthBerserkMarker");

                // 1/2. 安全移除目标记录的干扰 Hediff（避免访问 null / Destroyed / 无 health 的 Pawn）。
                foreach (CerebrexBandwidthTargetRecord? rec in bandwidthTargetRecords)
                {
                    Pawn? p = rec?.target;
                    if (p != null && !p.Destroyed && p.health != null && leftoverDef != null)
                    {
                        Hediff? h = p.health.hediffSet.GetFirstHediffOfDef(leftoverDef);
                        if (h != null)
                        {
                            p.health.RemoveHediff(h);
                        }
                    }
                }

                // 3. 只对带本 MOD 隐藏标记、且当前确为 BerserkMechanoid 的机械族恢复精神状态，随后移除标记。
                foreach (CerebrexBandwidthBerserkRecord? rec in bandwidthBerserkRecords)
                {
                    Pawn? p = rec?.pawn;
                    if (p == null || p.Destroyed || p.health == null || leftoverMarker == null)
                    {
                        continue;
                    }

                    Hediff? m = p.health.hediffSet.GetFirstHediffOfDef(leftoverMarker);
                    if (m == null)
                    {
                        // 没有本 MOD 隐藏标记：不处理，避免误伤普通狂暴。
                        continue;
                    }

                    MentalState? curState = p.mindState?.mentalStateHandler?.CurState;
                    if (curState?.def == MentalStateDefOf.BerserkMechanoid)
                    {
                        curState.RecoverFromState();
                    }

                    p.health.RemoveHediff(m);
                }

                // 4. 收集不同的原监管者（来源：受影响监管者、目标记录、狂暴记录），去重通知带宽变化。
                HashSet<Pawn> cleanupOverseers = new HashSet<Pawn>();
                if (bandwidthAffectedOverseers != null)
                {
                    foreach (Pawn ov in bandwidthAffectedOverseers)
                    {
                        if (ov != null && ov.mechanitor != null)
                        {
                            cleanupOverseers.Add(ov);
                        }
                    }
                }

                foreach (CerebrexBandwidthTargetRecord? rec in bandwidthTargetRecords)
                {
                    Pawn? ov = rec?.originalOverseer;
                    if (ov != null && ov.mechanitor != null)
                    {
                        cleanupOverseers.Add(ov);
                    }
                }

                foreach (CerebrexBandwidthBerserkRecord? rec in bandwidthBerserkRecords)
                {
                    Pawn? ov = rec?.originalOverseer;
                    if (ov != null && ov.mechanitor != null)
                    {
                        cleanupOverseers.Add(ov);
                    }
                }

                foreach (Pawn ov in cleanupOverseers)
                {
                    ov.mechanitor?.Notify_BandwidthChanged();
                }

                // 5. 最后清空记录；保留本场已使用目标的去脏版本（不在此处清空有效记录）。
                bandwidthTargetRecords.Clear();
                bandwidthBerserkRecords.Clear();
                bandwidthAffectedOverseers?.Clear();
                bandwidthTargetsUsedThisBattle.RemoveAll(p => p == null || p.Discarded);
            }

            // 读档发现召唤单位仍存在但没有 Lord：Spawn 并确认可自动运行后补建。
            if (summonedMechs.Count > 0 && assaultLord == null && CanRunAutomatically())
            {
                EnsureAssaultLord();
            }
        }

        public override void CompTick()
        {
            if (!initialized || !ModsConfig.OdysseyActive || parent.Map == null)
            {
                return;
            }

            // 1. 处理待命中的 EMP 命中（即使关系变化，已释放的冲击波仍可完成）。
            ProcessPendingEmpHits();

            // 2. 清理过期的建筑瘫痪记录。
            CleanExpiredDisabledPowerBuildings();

            // 3. 检查正在进行的带宽干扰（即使总开关/技能开关在战斗中关闭，
            //    已开始的整轮状态仍由这里维护到自然结束）。
            if (bandwidthInterferenceActive)
            {
                TickBandwidthInterference();
            }

            // 4. 已开始的 EMP 预警不因敌对关系中途变化而中断；主脑失效时由状态方法取消。
            if (empWarningActive)
            {
                TickEmpWarning();
            }

            // 5. 若主脑已停止，返回。
            if (stopped)
            {
                return;
            }

            // 已进入重试的增援批次必须使用最新玩家存在性，不受30 Tick缓存暂停。
            if (pendingSummonKinds.Count > 0)
            {
                nextPlayerPresenceCheckTick = 0;
            }

            // 6. 检查自动运行条件。
            if (!CanRunAutomatically())
            {
                return;
            }

            // 新行为门控始终读取当前 ModSettings；已经排定的 nextXXXTick 不倒推重算。
            RefreshDifficultySettings();

            // 7. 检查增援计时（受总开关 + 召唤技能开关门控）。关闭期间保留已排定 Tick，
            //    重新开启后若冷却早已到期，则下一 Tick 即可发动。
            int now = Find.TickManager.TicksGame;
            if (now >= nextSummonTick
                && difficultyEnableExtraSkills
                && difficultyEnableSummoning)
            {
                TrySummonWave(force: false);
            }

            // 8. 检查 EMP 计时（受总开关 + EMP 开关门控）。
            if (!empWarningActive
                && now >= nextEmpTick
                && difficultyEnableExtraSkills
                && difficultyEnableEmp)
            {
                StartEmpWarning(force: false);
            }

            // 9. 检查带宽干扰计时（受总开关 + 带宽技能开关门控）。
            if (!bandwidthInterferenceActive
                && now >= nextBandwidthTick
                && difficultyEnableExtraSkills
                && difficultyEnableBandwidthInterference)
            {
                // 自动尝试失败（如无合法目标）仅延迟 60 tick 再次尝试，不进入完整冷却。
                if (!TryStartBandwidthInterference(force: false))
                {
                    nextBandwidthTick = now + 60;
                }
            }
        }

        private bool CanRunAutomatically()
        {
            // 以下廉价条件仍每 Tick 即时判断。
            if (!ModsConfig.OdysseyActive
                || stopped
                || !parent.Spawned
                || parent.Map == null
                || parent.Faction == null
                || Faction.OfPlayer == null
                || !parent.Faction.HostileTo(Faction.OfPlayer))
            {
                return false;
            }

            // 缓存只用于“是否允许新的自动技能排程”，最多每30 Tick扫描一次。
            int now = Find.TickManager.TicksGame;
            if (now >= nextPlayerPresenceCheckTick)
            {
                cachedPlayerPawnPresent = HasActionablePlayerPawn(parent.Map);
                nextPlayerPresenceCheckTick =
                    now + PlayerPresenceCheckIntervalTicks;
            }

            return cachedPlayerPawnPresent;
        }

        // 纯机械族机械师殖民地可能没有人类殖民者，因此只按玩家派系 Pawn 判断。
        private static bool HasActionablePlayerPawn(Map map)
        {
            List<Pawn> pawns = map.mapPawns.PawnsInFaction(Faction.OfPlayer);
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null
                    || pawn.Dead
                    || pawn.Destroyed
                    || pawn.Discarded
                    || !pawn.Spawned
                    || pawn.Map != map
                    || pawn.Faction != Faction.OfPlayer)
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        // ----------------------------------------------------------------
        // 机械族增援
        // ----------------------------------------------------------------

        private int CountLivingSummonedMechs()
        {
            int count = 0;
            for (int i = summonedMechs.Count - 1; i >= 0; i--)
            {
                Pawn? p = summonedMechs[i];
                if (p == null || p.Dead || p.Destroyed || p.Discarded)
                {
                    summonedMechs.RemoveAt(i);
                    continue;
                }

                count++;
            }

            return count;
        }

        public void RegisterPendingSummonKind(PawnKindDef kind)
        {
            if (kind != null)
            {
                pendingSummonKinds.Add(kind);
            }
        }

        private int TrySummonWave(bool force)
        {
            if (parent.Map == null)
            {
                return 0;
            }

            Faction? mechFaction = Faction.OfMechanoids;
            if (mechFaction == null)
            {
                return 0;
            }

            // DEV 调用可能不经过 CompTick；在入口统一刷新，当前批次重试仍使用自己的锁定值。
            RefreshDifficultySettings();
            int now = Find.TickManager.TicksGame;

            // 先处理失败空投的重试（同一批次共用重试轮数；允许重复 PawnKind）。
            if (pendingSummonKinds.Count > 0)
            {
                if (!summonBatchSettingsCaptured)
                {
                    summonBatchMaxLivingSummonedMechs = difficultyMaxLivingSummonedMechs;
                    summonBatchApplyMobileCombatToSummons = difficultyApplyMobileCombatToSummons;
                    summonBatchSettingsCaptured = true;
                }

                if (summonDropRetryCount >= MaxDropRetryAttempts)
                {
                    pendingSummonKinds.Clear();
                    summonDropRetryCount = 0;
                    summonBatchSettingsCaptured = false;
                    nextSummonTick = now + difficultySummonIntervalTicks;
                    return 0;
                }

                EnsureAssaultLord();
                int living = CountLivingSummonedMechs();
                int avail = summonBatchMaxLivingSummonedMechs - living;
                if (avail <= 0)
                {
                    summonDropRetryCount++;
                    nextSummonTick = now + DropRetryDelayTicks;
                    return 0;
                }

                // 复制当前待重试列表，清空原列表，再逐项重试；失败的条目逐项重新加入。
                // 超过可用空位的条目本轮不重试，但保留在 pendingSummonKinds 等待下次。
                List<PawnKindDef> retryKinds = new List<PawnKindDef>(pendingSummonKinds);
                pendingSummonKinds.Clear();

                int toRetryCount = Mathf.Min(avail, retryKinds.Count);
                List<PawnKindDef> toRetry = retryKinds.GetRange(0, toRetryCount);
                for (int i = toRetryCount; i < retryKinds.Count; i++)
                {
                    pendingSummonKinds.Add(retryKinds[i]);
                }

                int spawned = CerebrexBossSpawnUtility.SpawnSummonWave(
                    parent.Map,
                    toRetry,
                    summonedMechs,
                    parent.Position,
                    mechFaction,
                    this,
                    summonBatchApplyMobileCombatToSummons);
                summonDropRetryCount++;

                // 当前批次全部成功或彻底放弃后，重置本批次重试计数与动作锁定。
                if (pendingSummonKinds.Count == 0)
                {
                    summonDropRetryCount = 0;
                    summonBatchSettingsCaptured = false;
                }

                nextSummonTick = pendingSummonKinds.Count > 0
                    ? now + DropRetryDelayTicks
                    : now + difficultySummonIntervalTicks;
                return spawned;
            }

            // 没有待重试单位：开始一批新的正常召唤，并锁定本批次需要保持一致的参数。
            summonDropRetryCount = 0;
            summonBatchMaxLivingSummonedMechs = difficultyMaxLivingSummonedMechs;
            summonBatchApplyMobileCombatToSummons = difficultyApplyMobileCombatToSummons;
            summonBatchSettingsCaptured = true;

            int livingCount = CountLivingSummonedMechs();
            int availableSlots = summonBatchMaxLivingSummonedMechs - livingCount;
            int count = Mathf.Min(difficultyMechsPerWave, availableSlots);
            if (count <= 0)
            {
                summonBatchSettingsCaptured = false;
                nextSummonTick = now + difficultySummonIntervalTicks;
                return 0;
            }

            List<PawnGenOption> pool = JusticeBossMechPoolUtility.BuildCombatPool();
            List<PawnKindDef> chosen = new List<PawnKindDef>();
            for (int i = 0; i < count; i++)
            {
                PawnKindDef? kind = JusticeBossMechPoolUtility.PickWeighted(pool);
                if (kind != null)
                {
                    chosen.Add(kind);
                }
            }

            if (chosen.Count == 0)
            {
                summonBatchSettingsCaptured = false;
                nextSummonTick = now + difficultySummonIntervalTicks;
                return 0;
            }

            EnsureAssaultLord();
            int spawnedNormal = CerebrexBossSpawnUtility.SpawnSummonWave(
                parent.Map,
                chosen,
                summonedMechs,
                parent.Position,
                mechFaction,
                this,
                summonBatchApplyMobileCombatToSummons);

            if (pendingSummonKinds.Count == 0)
            {
                summonBatchSettingsCaptured = false;
            }

            nextSummonTick = pendingSummonKinds.Count > 0
                ? now + DropRetryDelayTicks
                : now + difficultySummonIntervalTicks;
            return spawnedNormal;
        }

        private void EnsureAssaultLord()
        {
            if (parent.Map == null)
            {
                return;
            }

            assaultLord = CerebrexBossLordUtility.EnsureAssaultLord(parent.Map, Faction.OfMechanoids, parent.thingIDNumber);
        }

        // ----------------------------------------------------------------
        // EMP 冲击波
        // ----------------------------------------------------------------

        private bool StartEmpWarning(bool force)
        {
            if (empWarningActive)
            {
                if (force)
                {
                    Messages.Message("主脑正在准备释放EMP冲击。", MessageTypeDefOf.RejectInput);
                }

                return false;
            }

            if (!ModsConfig.OdysseyActive || stopped || parent.Destroyed || !parent.Spawned || parent.Map == null)
            {
                return false;
            }

            RefreshDifficultySettings();
            empCastRadius = difficultyEmpRadius;
            empCastBaseDurationTicks = difficultyEmpBaseDurationTicks;
            empCastSettingsCaptured = true;

            int now = Find.TickManager.TicksGame;
            empWarningActive = true;
            empWarningStartTick = now;
            empShockwaveReleaseTick = now + EmpWarningRiseTicks + EmpWarningHoldTicks + EmpWarningSlamTicks;
            empWarningEndTick = empShockwaveReleaseTick + EmpWarningRecoveryTicks;
            empShockwaveReleased = false;
            return true;
        }

        private void TickEmpWarning()
        {
            if (!empWarningActive)
            {
                return;
            }

            if (!ModsConfig.OdysseyActive || stopped || parent.Destroyed || !parent.Spawned || parent.Map == null)
            {
                ResetEmpWarningState();
                return;
            }

            int now = Find.TickManager.TicksGame;
            if (!empShockwaveReleased && now >= empShockwaveReleaseTick)
            {
                TriggerEmpShockwave();
                empShockwaveReleased = true;

                // 当前这发 EMP 已锁定；这里只为下一发排程，使用释放时的最新冷却设置。
                RefreshDifficultySettings();
                nextEmpTick = now + Rand.RangeInclusive(
                    difficultyEmpCooldownMinTicks,
                    difficultyEmpCooldownMaxTicks);
            }

            if (now >= empWarningEndTick)
            {
                ResetEmpWarningState();
            }
        }

        private void ResetEmpWarningState()
        {
            empWarningActive = false;
            empWarningStartTick = 0;
            empShockwaveReleaseTick = 0;
            empWarningEndTick = 0;
            empShockwaveReleased = false;
            empCastRadius = 0f;
            empCastBaseDurationTicks = 0;
            empCastSettingsCaptured = false;
        }

        public static float CalculateOriginalBrainZOffset(int tick)
        {
            float bob = 0.5f * (1f + Mathf.Sin(Mathf.PI * 2f * tick / OriginalBrainBobPeriodTicks));
            return OriginalBrainBaseZOffset + bob * OriginalBrainBobHeight;
        }

        public bool TryGetEmpWarningBrainZOffset(out float zOffset)
        {
            zOffset = 0f;
            if (!empWarningActive || stopped || !parent.Spawned || parent.Destroyed || parent.Map == null)
            {
                return false;
            }

            int now = Find.TickManager.TicksGame;
            int riseEndTick = empWarningStartTick + EmpWarningRiseTicks;
            int holdEndTick = riseEndTick + EmpWarningHoldTicks;

            if (now < riseEndTick)
            {
                float progress = Mathf.InverseLerp(empWarningStartTick, riseEndTick, now);
                float eased = Mathf.SmoothStep(0f, 1f, progress);
                float startZ = CalculateOriginalBrainZOffset(empWarningStartTick);
                zOffset = Mathf.Lerp(startZ, EmpWarningPeakZOffset, eased);
                return true;
            }

            if (now < holdEndTick)
            {
                zOffset = EmpWarningPeakZOffset;
                return true;
            }

            if (now < empShockwaveReleaseTick)
            {
                float progress = Mathf.InverseLerp(holdEndTick, empShockwaveReleaseTick, now);
                float eased = Mathf.SmoothStep(0f, 1f, progress);
                zOffset = Mathf.Lerp(EmpWarningPeakZOffset, EmpWarningImpactZOffset, eased);
                return true;
            }

            if (now < empWarningEndTick)
            {
                float progress = Mathf.InverseLerp(empShockwaveReleaseTick, empWarningEndTick, now);
                float eased = Mathf.SmoothStep(0f, 1f, progress);
                float originalZ = CalculateOriginalBrainZOffset(now);
                zOffset = Mathf.Lerp(EmpWarningImpactZOffset, originalZ, eased);
                return true;
            }

            return false;
        }

        private void TriggerEmpShockwave()
        {
            if (parent.Destroyed || parent.Map == null)
            {
                return;
            }

            // 兼容旧存档恰好停在 EMP 预警中的情况。
            if (!empCastSettingsCaptured)
            {
                RefreshDifficultySettings();
                empCastRadius = difficultyEmpRadius;
                empCastBaseDurationTicks = difficultyEmpBaseDurationTicks;
                empCastSettingsCaptured = true;
            }

            float radius = empCastRadius;
            int baseDurationTicks = empCastBaseDurationTicks;
            PlayEmpShockwaveVisual(radius);

            Map map = parent.Map;
            int now = Find.TickManager.TicksGame;
            HashSet<Thing> addedThisRelease = new HashSet<Thing>();

            foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
            {
                if (p == null || addedThisRelease.Contains(p) || !IsValidEmpTarget(p))
                {
                    continue;
                }

                float distance = parent.Position.DistanceTo(p.Position);
                if (distance > radius)
                {
                    continue;
                }

                int delay = Mathf.CeilToInt(distance / EmpPropagationSpeed);
                pendingEmpHits.Add(
                    new PendingCerebrexEmpHit(
                        p,
                        now + delay,
                        radius,
                        baseDurationTicks));
                addedThisRelease.Add(p);
            }

            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (thing is Building b && !addedThisRelease.Contains(b) && IsValidEmpTarget(b))
                {
                    float distance = parent.Position.DistanceTo(b.Position);
                    if (distance > radius)
                    {
                        continue;
                    }

                    int delay = Mathf.CeilToInt(distance / EmpPropagationSpeed);
                    pendingEmpHits.Add(
                        new PendingCerebrexEmpHit(
                            b,
                            now + delay,
                            radius,
                            baseDurationTicks));
                    addedThisRelease.Add(b);
                }
            }
        }

        private void ProcessPendingEmpHits()
        {
            if (pendingEmpHits.Count == 0 || parent.Map == null)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            for (int i = pendingEmpHits.Count - 1; i >= 0; i--)
            {
                PendingCerebrexEmpHit hit = pendingEmpHits[i];
                if (now < hit.impactTick)
                {
                    continue;
                }

                Thing? t = hit.target;
                if (t == null || t.Destroyed || t.Discarded)
                {
                    pendingEmpHits.RemoveAt(i);
                    continue;
                }

                if (t.Map != parent.Map)
                {
                    pendingEmpHits.RemoveAt(i);
                    continue;
                }

                float distance = parent.Position.DistanceTo(t.Position);
                if (distance > hit.radiusAtRelease)
                {
                    pendingEmpHits.RemoveAt(i);
                    continue;
                }

                if (!IsValidEmpTarget(t))
                {
                    pendingEmpHits.RemoveAt(i);
                    continue;
                }

                ApplyEmpToTarget(t, hit.baseDurationTicksAtRelease);
                pendingEmpHits.RemoveAt(i);
            }
        }

        private static bool IsValidEmpTarget(Thing t)
        {
            Faction? mechHiveFaction = Faction.OfMechanoids;
            if (t is Pawn p)
            {
                if (!p.RaceProps.IsMechanoid)
                {
                    return false;
                }

                if (p.Dead || p.Downed)
                {
                    return false;
                }

                if (!p.Spawned)
                {
                    return false;
                }

                if (mechHiveFaction != null && p.Faction == mechHiveFaction)
                {
                    return false;
                }

                return true;
            }

            if (t is Building b)
            {
                CompPowerTrader? trader = b.GetComp<CompPowerTrader>();
                if (trader == null)
                {
                    return false;
                }

                if (trader.Props.PowerConsumption <= 0f)
                {
                    return false;
                }

                if (mechHiveFaction != null && b.Faction == mechHiveFaction)
                {
                    return false;
                }

                return true;
            }

            return false;
        }

        private int CalculateEmpDuration(Thing target, int baseDurationTicks)
        {
            float resistance = 0f;
            StatDef? stat = DefDatabase<StatDef>.GetNamedSilentFail("EMPResistance");
            if (stat != null && !stat.Worker.IsDisabledFor(target))
            {
                resistance = target.GetStatValue(stat);
            }

            return Mathf.RoundToInt(
                baseDurationTicks * Mathf.Clamp01(1f - resistance));
        }

        private void ApplyEmpToTarget(Thing target, int baseDurationTicks)
        {
            int duration = CalculateEmpDuration(target, baseDurationTicks);
            if (duration <= 0)
            {
                return;
            }

            if (target is Pawn p)
            {
                p.stances?.stunner?.StunFor(duration, parent, addBattleLog: true);
            }
            else if (target is Building b)
            {
                int until = Find.TickManager.TicksGame + duration;
                disabledPowerBuildings[b] = until;
                CerebrexPowerDisruptionUtility.Register(b, until);
                b.GetComp<CompStunnable>()?.StunHandler.StunFor(duration, parent);
                PlayEmpArcEffect(b);
            }
        }

        private void PlayEmpShockwaveVisual(float radius)
        {
            EffecterDef? effecterDef = DefDatabase<EffecterDef>.GetNamedSilentFail("BlastMechBandShockwave");
            SoundDef? soundDef = DefDatabase<SoundDef>.GetNamedSilentFail("Explosion_MechBandShockwave");
            float visualScale = radius / OriginalShockwaveRadius;
            Effecter? effecter = effecterDef?.Spawn(parent.Position, parent.Map, visualScale);
            effecter?.Cleanup();
            soundDef?.PlayOneShot(new TargetInfo(parent.Position, parent.Map));
        }

        private void PlayEmpArcEffect(Thing target)
        {
            EffecterDef? arcDef = DefDatabase<EffecterDef>.GetNamedSilentFail("MechBandElectricityArc");
            Effecter? arc = arcDef?.Spawn(target.Position, parent.Map);
            arc?.Cleanup();
        }

        private void CleanExpiredDisabledPowerBuildings()
        {
            if (disabledPowerBuildings.Count == 0)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            List<Thing>? remove = null;
            foreach (KeyValuePair<Thing, int> kv in disabledPowerBuildings)
            {
                if (kv.Key == null || kv.Key.Destroyed || now >= kv.Value)
                {
                    if (kv.Key != null)
                    {
                        remove ??= new List<Thing>();
                        remove.Add(kv.Key);
                    }
                }
            }

            if (remove != null)
            {
                foreach (Thing t in remove)
                {
                    disabledPowerBuildings.Remove(t);
                }
            }

            disabledPowerBuildings.Remove(null!);
        }

        // ----------------------------------------------------------------
        // 带宽干扰（多目标）
        // ----------------------------------------------------------------

        private bool TryStartBandwidthInterference(bool force)
        {
            if (bandwidthInterferenceActive)
            {
                if (force)
                {
                    Messages.Message("主脑带宽干扰已在运行。", MessageTypeDefOf.RejectInput);
                }

                return false;
            }

            RefreshDifficultySettings();
            int durationTicks = difficultyBandwidthDurationTicks;
            List<Pawn> targets = FindBandwidthTargets(difficultyBandwidthMaxTargets);
            if (targets.Count == 0)
            {
                if (force)
                {
                    Messages.Message("未找到合适的带宽干扰目标。", MessageTypeDefOf.RejectInput);
                }

                return false;
            }

            HediffDef? interferenceDef = DefDatabase<HediffDef>.GetNamedSilentFail("MAP_CerebrexBandwidthInterference");
            if (interferenceDef == null)
            {
                return false;
            }

            // 1~3. 在添加任何干扰 Hediff 之前，记录每个目标与其原监管者的对应关系；
            // 只保留具有有效原监管者（且具备 mechanitor tracker）的目标。
            Dictionary<Pawn, Pawn> originalOverseerByTarget = new Dictionary<Pawn, Pawn>();
            HashSet<Pawn> validOverseers = new HashSet<Pawn>();
            foreach (Pawn t in targets)
            {
                Pawn? ov = t.GetOverseer();
                if (ov != null && ov.mechanitor != null)
                {
                    originalOverseerByTarget[t] = ov;
                    validOverseers.Add(ov);
                }
            }

            if (originalOverseerByTarget.Count == 0)
            {
                if (force)
                {
                    Messages.Message("目标缺少有效的监管者。", MessageTypeDefOf.RejectInput);
                }

                return false;
            }

            // 2. 按不同监管者保存受控机械族变化前快照。
            Dictionary<Pawn, List<Pawn>> beforeControlled = new Dictionary<Pawn, List<Pawn>>();
            foreach (Pawn ov in validOverseers)
            {
                if (ov.mechanitor != null)
                {
                    beforeControlled[ov] = new List<Pawn>(ov.mechanitor.ControlledPawns);
                }
            }

            // 3~5. 给目标添加干扰 Hediff；先确认实际添加成功，才登记为生效目标，
            // 并保存原监管者、加入本轮已使用目标、受影响监管者与成功监管者集合。
            List<Pawn> applied = new List<Pawn>();
            HashSet<Pawn> appliedOverseers = new HashSet<Pawn>();
            foreach (Pawn t in originalOverseerByTarget.Keys)
            {
                // 已经处于带宽干扰中的目标直接跳过（FindBandwidthTargets 理论上已排除）。
                if (t.health.hediffSet.GetFirstHediffOfDef(interferenceDef) != null)
                {
                    continue;
                }

                Hediff hediff = HediffMaker.MakeHediff(interferenceDef, t);
                t.health.AddHediff(hediff);

                // 仅当目标身上确实出现了 MAP_CerebrexBandwidthInterference 才算成功；
                // 添加失败的目标不登记任何状态，继续尝试其他候选目标。
                Hediff? appliedHediff = t.health.hediffSet.GetFirstHediffOfDef(interferenceDef);
                if (appliedHediff == null)
                {
                    continue;
                }

                Pawn ov = originalOverseerByTarget[t];
                bandwidthTargetsUsedThisBattle.Add(t);
                bandwidthTargetRecords.Add(new CerebrexBandwidthTargetRecord(t, ov));
                if (!bandwidthAffectedOverseers.Contains(ov))
                {
                    bandwidthAffectedOverseers.Add(ov);
                }

                applied.Add(t);
                if (!appliedOverseers.Contains(ov))
                {
                    appliedOverseers.Add(ov);
                }
            }

            if (applied.Count == 0)
            {
                if (force)
                {
                    Messages.Message("目标已处于带宽干扰中。", MessageTypeDefOf.RejectInput);
                }

                return false;
            }

            // 4. 每个成功目标的原监管者只调用一次 Notify_BandwidthChanged。
            foreach (Pawn ov in appliedOverseers)
            {
                ov.mechanitor?.Notify_BandwidthChanged();
            }

            bandwidthVisuals.Start(parent, applied);

            // 5/6/7/8. 仅对成功目标的原监管者读取变化后受控列表，对变化前受控、变化后不再受控的玩家机械族执行狂暴。
            foreach (Pawn ov in appliedOverseers)
            {
                if (ov.mechanitor == null)
                {
                    continue;
                }

                List<Pawn> before = beforeControlled.TryGetValue(ov, out List<Pawn>? b)
                    ? b
                    : new List<Pawn>();
                List<Pawn> after = ov.mechanitor.ControlledPawns;
                foreach (Pawn p in before)
                {
                    if (p == null || p.Dead || p.Destroyed || p.Discarded)
                    {
                        continue;
                    }

                    if (after.Contains(p))
                    {
                        continue;
                    }

                    if (p.Faction != Faction.OfPlayer || !p.RaceProps.IsMechanoid || !p.Spawned || p.Downed)
                    {
                        continue;
                    }

                    TryBerserk(p, ov, durationTicks);
                }
            }

            bandwidthInterferenceActive = true;
            bandwidthInterferenceEndTick =
                Find.TickManager.TicksGame + durationTicks;
            return true;
        }

        private List<Pawn> FindBandwidthTargets(int maxTargets)
        {
            if (parent.Map == null)
            {
                return new List<Pawn>();
            }

            Map map = parent.Map;
            HediffDef? interferenceDef = DefDatabase<HediffDef>.GetNamedSilentFail("MAP_CerebrexBandwidthInterference");
            List<(Pawn pawn, float cost)> candidates = new List<(Pawn, float)>();

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn == null
                    || pawn.Dead
                    || pawn.Downed
                    || pawn.Destroyed
                    || pawn.Discarded)
                {
                    continue;
                }

                if (!pawn.RaceProps.IsMechanoid)
                {
                    continue;
                }

                if (pawn.Faction != Faction.OfPlayer)
                {
                    continue;
                }

                Pawn? overseer = pawn.GetOverseer();
                if (overseer == null || overseer.mechanitor == null)
                {
                    continue;
                }

                if (!overseer.mechanitor.ControlledPawns.Contains(pawn))
                {
                    continue;
                }

                if (GameComponent_MechanoidMechanitorRegistry.HasPersistentRecord(pawn))
                {
                    continue;
                }

                if (bandwidthTargetsUsedThisBattle.Contains(pawn))
                {
                    continue;
                }

                if (interferenceDef != null && pawn.health.hediffSet.GetFirstHediffOfDef(interferenceDef) != null)
                {
                    continue;
                }

                float cost = pawn.GetStatValue(StatDefOf.BandwidthCost);
                candidates.Add((pawn, cost));
            }

            // 按带宽消耗从高到低选择；同消耗目标之间随机。
            candidates = candidates
                .OrderByDescending(x => x.cost)
                .ThenBy(x => Rand.Int)
                .ToList();

            int take = Mathf.Min(maxTargets, candidates.Count);
            if (take <= 0)
            {
                return new List<Pawn>();
            }

            return candidates
                .GetRange(0, take)
                .Select(x => x.pawn)
                .ToList();
        }

        private void TryBerserk(Pawn p, Pawn? originalOverseer, int durationTicks)
        {
            bool started = p.mindState.mentalStateHandler.TryStartMentalState(
                MentalStateDefOf.BerserkMechanoid,
                reason: null,
                forced: true,
                forceWake: true);
            if (!started)
            {
                return;
            }

            HediffDef? markerDef = DefDatabase<HediffDef>.GetNamedSilentFail("MAP_CerebrexBandwidthBerserkMarker");
            if (markerDef != null && p.health.hediffSet.GetFirstHediffOfDef(markerDef) == null)
            {
                p.health.AddHediff(HediffMaker.MakeHediff(markerDef, p));
            }

            if (p.MentalState != null)
            {
                p.MentalState.forceRecoverAfterTicks = durationTicks;
            }

            if (!bandwidthBerserkRecords.Any(r => r.pawn == p))
            {
                bandwidthBerserkRecords.Add(new CerebrexBandwidthBerserkRecord(p, originalOverseer));
            }

            bandwidthVisuals.NotifyBerserk(p);
        }

        // 本轮狂暴 Pawn 是否“失去行动能力”。仅包括：null、倒地、死亡、摧毁、离开当前地图。
        // EMP 眩晕/睡眠/短暂不能移动但未倒地，不算失去行动能力。
        private bool IsBandwidthBerserkPawnIncapacitated(Pawn? pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed || pawn.Discarded)
            {
                return true;
            }

            if (parent.Map == null || !pawn.Spawned || pawn.Map != parent.Map || pawn.Downed)
            {
                return true;
            }

            return false;
        }

        // 无副作用的辅助方法：正常 tick 与读档校验共用，仅判断“目标 / 本轮狂暴者”的
        // 行动能力结束条件（不含主脑停止、派系、监管者引用损坏、结束时间等基础状态）。
        // 规则：
        //  - 仍存在至少一个能行动的有效干扰目标时，技能继续。
        //  - 本轮产生过狂暴单位时，只要名单中还有能行动的狂暴者，技能继续。
        //  - 全部有效目标与全部能行动的狂暴者均失去行动能力时，结束整轮。
        private bool ShouldEndBandwidthInterferenceForPawnState()
        {
            bool hasValidTarget = false;
            foreach (CerebrexBandwidthTargetRecord rec in bandwidthTargetRecords)
            {
                if (IsTargetRecordActionable(rec))
                {
                    hasValidTarget = true;
                    break;
                }
            }

            if (hasValidTarget)
            {
                return false;
            }

            bool hasActiveBerserk = false;
            foreach (CerebrexBandwidthBerserkRecord? rec in bandwidthBerserkRecords)
            {
                Pawn? bp = rec?.pawn;
                if (bp != null && !IsBandwidthBerserkPawnIncapacitated(bp))
                {
                    hasActiveBerserk = true;
                    break;
                }
            }

            return !hasActiveBerserk;
        }

        private bool IsTargetRecordActionable(CerebrexBandwidthTargetRecord? rec)
        {
            if (rec == null)
            {
                return false;
            }

            Pawn? t = rec.target;
            if (t == null || t.Dead || t.Destroyed || t.Discarded)
            {
                return false;
            }

            if (parent.Map == null || !t.Spawned || t.Map != parent.Map || t.Downed)
            {
                return false;
            }

            HediffDef? def = DefDatabase<HediffDef>.GetNamedSilentFail("MAP_CerebrexBandwidthInterference");
            if (def != null && t.health.hediffSet.GetFirstHediffOfDef(def) == null)
            {
                return false;
            }

            return true;
        }

        private void CleanupInvalidTargetRecords()
        {
            HediffDef? interferenceDef = DefDatabase<HediffDef>.GetNamedSilentFail("MAP_CerebrexBandwidthInterference");
            HashSet<Pawn> affectedOverseers = new HashSet<Pawn>();

            for (int i = bandwidthTargetRecords.Count - 1; i >= 0; i--)
            {
                CerebrexBandwidthTargetRecord? rec = bandwidthTargetRecords[i];
                if (rec == null)
                {
                    // 损坏的空记录直接移除，禁止 NullReference。
                    bandwidthTargetRecords.RemoveAt(i);
                    continue;
                }

                Pawn? t = rec.target;
                Pawn? ov = rec.originalOverseer;
                bool invalid =
                    t == null
                    || t.Dead
                    || t.Destroyed
                    || t.Discarded
                    || !t.Spawned
                    || (parent.Map != null && t.Map != parent.Map)
                    || t.Downed
                    || (interferenceDef != null && t.health.hediffSet.GetFirstHediffOfDef(interferenceDef) == null)
                    || ov == null
                    || ov.Discarded
                    || ov.mechanitor == null;

                if (!invalid)
                {
                    continue;
                }

                // 仅在 Pawn 仍可安全访问健康状态时移除残留干扰 Hediff。
                if (t != null && !t.Destroyed && t.health != null && interferenceDef != null)
                {
                    Hediff? h = t.health.hediffSet.GetFirstHediffOfDef(interferenceDef);
                    if (h != null)
                    {
                        t.health.RemoveHediff(h);
                    }
                }

                if (ov != null && ov.mechanitor != null)
                {
                    affectedOverseers.Add(ov);
                }

                // 删除该目标记录；不结束其他仍有效目标的干扰，
                // 不从 bandwidthTargetsUsedThisBattle 删除（确保复活后不会再次被选），
                // 也不从 bandwidthAffectedOverseers 删除监管者。
                bandwidthTargetRecords.RemoveAt(i);
            }

            // 一次性对涉及的不同有效监管者各通知一次。
            foreach (Pawn ov in affectedOverseers)
            {
                ov.mechanitor?.Notify_BandwidthChanged();
            }
        }

        private void TickBandwidthInterference()
        {
            int now = Find.TickManager.TicksGame;
            bool shouldEnd = false;

            // 1. 无条件结束原因：主脑停止 / 失效。
            if (stopped || !parent.Spawned || parent.Destroyed)
            {
                shouldEnd = true;
            }

            if (!shouldEnd)
            {
                // 清理已失效的目标记录（死亡 / 倒地 / 离开地图 / Hediff 丢失），
                // 不直接结束其他目标的整轮干扰。
                CleanupInvalidTargetRecords();
                // 清理已彻底失效（null / Discarded）的狂暴记录。
                bandwidthBerserkRecords.RemoveAll(r => r.pawn == null || r.pawn.Discarded);
            }

            // 3/4. 目标与本轮狂暴者的行动能力判断（与读档校验共用，无副作用）。
            if (!shouldEnd && ShouldEndBandwidthInterferenceForPawnState())
            {
                shouldEnd = true;
            }

            // 5. 某个本轮狂暴单位重新出现在原监管者的受控列表中 -> 结束整轮。
            if (!shouldEnd && HasBerserkPawnReturnedToOverseer())
            {
                shouldEnd = true;
            }

            // 6. 达到持续时间。
            if (!shouldEnd && now >= bandwidthInterferenceEndTick)
            {
                shouldEnd = true;
            }

            if (shouldEnd)
            {
                EndBandwidthInterference(startCooldown: true, reason: "tick");
            }
            else
            {
                bandwidthVisuals.Tick(
                    parent,
                    bandwidthTargetRecords
                        .Select(r => r?.target)
                        .Where(t => t != null));
                CleanupDownedBerserkMarkers();
            }
        }

        private bool HasBerserkPawnReturnedToOverseer()
        {
            foreach (CerebrexBandwidthBerserkRecord? rec in bandwidthBerserkRecords)
            {
                if (rec == null || rec.pawn == null || rec.originalOverseer == null)
                {
                    continue;
                }

                var tracker = rec.originalOverseer.mechanitor;
                if (tracker != null && tracker.ControlledPawns.Contains(rec.pawn))
                {
                    return true;
                }
            }

            return false;
        }

        // 仅移除隐藏狂暴标记；绝不可因此把 Pawn 从 bandwidthBerserkRecords 删除。
        private void CleanupDownedBerserkMarkers()
        {
            for (int i = bandwidthBerserkRecords.Count - 1; i >= 0; i--)
            {
                Pawn? p = bandwidthBerserkRecords[i].pawn;
                if (p == null || p.Dead || p.Destroyed || p.Discarded || p.Downed)
                {
                    RemoveBerserkMarker(p);
                }
            }
        }

        private void RemoveBerserkMarker(Pawn? p)
        {
            if (p == null || p.Destroyed)
            {
                return;
            }

            HediffDef? markerDef = DefDatabase<HediffDef>.GetNamedSilentFail("MAP_CerebrexBandwidthBerserkMarker");
            if (markerDef == null)
            {
                return;
            }

            Hediff? marker = p.health.hediffSet.GetFirstHediffOfDef(markerDef);
            if (marker != null)
            {
                p.health.RemoveHediff(marker);
            }
        }

        private void EndBandwidthInterference(bool startCooldown, string reason)
        {
            List<Pawn?> allTargets = bandwidthTargetRecords
                .Select(r => r?.target)
                .Where(p => p != null)
                .ToList();

            HashSet<Pawn> distinctOverseers = new HashSet<Pawn>();
            if (bandwidthAffectedOverseers != null)
            {
                foreach (Pawn ov in bandwidthAffectedOverseers)
                {
                    if (ov != null)
                    {
                        distinctOverseers.Add(ov);
                    }
                }
            }

            foreach (CerebrexBandwidthTargetRecord? rec in bandwidthTargetRecords)
            {
                if (rec?.originalOverseer != null)
                {
                    distinctOverseers.Add(rec.originalOverseer);
                }
            }

            foreach (CerebrexBandwidthBerserkRecord? rec in bandwidthBerserkRecords)
            {
                if (rec?.originalOverseer != null)
                {
                    distinctOverseers.Add(rec.originalOverseer);
                }
            }

            // 1. 只恢复带隐藏标记的狂暴单位。
            for (int i = bandwidthBerserkRecords.Count - 1; i >= 0; i--)
            {
                CerebrexBandwidthBerserkRecord? rec = bandwidthBerserkRecords[i];
                if (rec == null)
                {
                    bandwidthBerserkRecords.RemoveAt(i);
                    continue;
                }

                Pawn? p = rec.pawn;
                if (p == null)
                {
                    continue;
                }

                HediffDef? markerDef = DefDatabase<HediffDef>.GetNamedSilentFail("MAP_CerebrexBandwidthBerserkMarker");
                bool hasMarker = markerDef != null && p.health.hediffSet.GetFirstHediffOfDef(markerDef) != null;
                bool isBerserk = p.MentalState != null && p.MentalState.def == MentalStateDefOf.BerserkMechanoid;
                if (hasMarker && isBerserk)
                {
                    p.mindState.mentalStateHandler.CurState?.RecoverFromState();
                }

                RemoveBerserkMarker(p);
            }

            // 2. 删除所有目标身上的干扰 Hediff。
            HediffDef? interferenceDef = DefDatabase<HediffDef>.GetNamedSilentFail("MAP_CerebrexBandwidthInterference");
            foreach (Pawn? t in allTargets)
            {
                if (t != null && interferenceDef != null)
                {
                    Hediff? h = t.health.hediffSet.GetFirstHediffOfDef(interferenceDef);
                    if (h != null)
                    {
                        t.health.RemoveHediff(h);
                    }
                }
            }

            // 3. 对每个不同且仍有效的原监管者各通知一次。
            foreach (Pawn ov in distinctOverseers)
            {
                ov.mechanitor?.Notify_BandwidthChanged();
            }

            bool playRecoveryVisual = startCooldown
                && reason != "loadfix"
                && parent.Spawned
                && !parent.Destroyed
                && parent.Map != null;
            bandwidthVisuals.Stop(
                allTargets,
                bandwidthBerserkRecords.Select(r => r.pawn).Where(p => p != null),
                playRecoveryVisual);

            bandwidthBerserkRecords.Clear();
            bandwidthTargetRecords.Clear();
            bandwidthAffectedOverseers?.Clear();
            bandwidthInterferenceEndTick = 0;
            bandwidthInterferenceActive = false;

            if (startCooldown)
            {
                // 当前干扰已经结束；为下一轮排程时读取最新冷却设置。
                RefreshDifficultySettings();
                nextBandwidthTick = Find.TickManager.TicksGame + Rand.RangeInclusive(
                    difficultyBandwidthCooldownMinTicks,
                    difficultyBandwidthCooldownMaxTicks);
            }
        }

        // ----------------------------------------------------------------
        // 主脑关闭 / 销毁清理
        // ----------------------------------------------------------------

        private void StopBossCombat(string reason)
        {
            stopped = true;
            ResetEmpWarningState();
            EndBandwidthInterference(startCooldown: false, reason: reason);
            pendingEmpHits.Clear();
            bandwidthTargetsUsedThisBattle.Clear();
            pendingSummonKinds.Clear();
            summonDropRetryCount = 0;
            summonBatchSettingsCaptured = false;
        }

        public void Notify_CoreDeactivationStarted()
        {
            StopBossCombat("deactivation");
        }

        public void Notify_CoreDefencesLowered()
        {
            StopBossCombat("defencesLowered");
        }

        private void FullCleanup(string reason)
        {
            stopped = true;
            ResetEmpWarningState();
            EndBandwidthInterference(startCooldown: false, reason: reason);
            pendingEmpHits.Clear();
            bandwidthTargetsUsedThisBattle.Clear();
            pendingSummonKinds.Clear();
            summonDropRetryCount = 0;
            summonBatchSettingsCaptured = false;
            disabledPowerBuildings.Clear();
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            FullCleanup("destroy");
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            FullCleanup("despawn");
        }

        // ----------------------------------------------------------------
        // DEV 按钮
        // ----------------------------------------------------------------

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                yield return g;
            }

            if (!ModsConfig.OdysseyActive || !DebugSettings.godMode)
            {
                yield break;
            }

            if (!parent.Spawned || parent.Map == null)
            {
                yield break;
            }

            yield return new Command_Action
            {
                defaultLabel = "DEV：立即召唤机械族",
                action = DevSummon,
            };

            yield return new Command_Action
            {
                defaultLabel = "DEV：立即进行带宽干扰",
                action = DevBandwidth,
            };

            yield return new Command_Action
            {
                defaultLabel = "DEV：停止本次带宽干扰",
                action = DevStopBandwidth,
            };

            yield return new Command_Action
            {
                defaultLabel = "DEV：释放EMP冲击",
                action = DevEmp,
            };
        }

        private void DevSummon()
        {
            if (!ModsConfig.OdysseyActive || parent.Map == null)
            {
                return;
            }

            if (pendingSummonKinds.Count > 0)
            {
                Messages.Message("存在失败空投正在重试，请等待。", MessageTypeDefOf.RejectInput);
                return;
            }

            int beforePending = pendingSummonKinds.Count;
            int spawned = TrySummonWave(force: true);
            int failed = pendingSummonKinds.Count - beforePending;

            if (spawned <= 0 && failed <= 0)
            {
                Messages.Message("已经达到主脑召唤机械族上限。", MessageTypeDefOf.RejectInput);
            }
            else if (spawned <= 0 && failed > 0)
            {
                Messages.Message("没有找到合法空投位置，已进入重试。", MessageTypeDefOf.RejectInput);
            }
            else if (spawned > 0 && failed > 0)
            {
                Messages.Message("部分单位空投未找到位置，剩余单位将继续重试。", MessageTypeDefOf.RejectInput);
            }
        }

        private void DevBandwidth()
        {
            if (!ModsConfig.OdysseyActive)
            {
                return;
            }

            // force=true 绕过主脑总开关与带宽单项技能开关（仍受基础条件约束）。
            TryStartBandwidthInterference(force: true);
        }

        private void DevStopBandwidth()
        {
            if (!ModsConfig.OdysseyActive)
            {
                return;
            }

            if (!bandwidthInterferenceActive)
            {
                Messages.Message("当前没有正在进行的带宽干扰。", MessageTypeDefOf.RejectInput);
                return;
            }

            EndBandwidthInterference(startCooldown: true, reason: "devstop");
        }

        private void DevEmp()
        {
            if (!ModsConfig.OdysseyActive || parent.Map == null)
            {
                return;
            }

            // force=true 绕过主脑总开关与 EMP 单项技能开关（仍受基础条件约束）。
            StartEmpWarning(force: true);
        }
    }

    [HarmonyPatch(typeof(CompCerebrexCore), nameof(CompCerebrexCore.DrawAt), new System.Type[] { typeof(Vector3), typeof(bool) })]
    public static class CerebrexCoreEmpWarningDrawPatch
    {
        [HarmonyPrefix]
        public static void Prefix(CompCerebrexCore __instance, ref Vector3 drawLoc)
        {
            CompCerebrexBossController? controller = __instance.parent?.GetComp<CompCerebrexBossController>();
            if (controller == null || !controller.TryGetEmpWarningBrainZOffset(out float warningZ))
            {
                return;
            }

            float originalZ = CompCerebrexBossController.CalculateOriginalBrainZOffset(GenTicks.TicksGame);
            drawLoc.z += warningZ - originalZ;
        }
    }
}
