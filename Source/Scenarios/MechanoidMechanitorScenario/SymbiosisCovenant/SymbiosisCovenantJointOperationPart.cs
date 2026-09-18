using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 联合军事行动的唯一权威状态机。随 Quest 存档，所有行动状态
    /// （目标、参与派系、阶段、倒计时、援军生成状态）只存在于此，
    /// 不写入 GameComponent，避免 Quest 与 GameComponent 两套状态漂移。
    /// 继承 QuestPartActivable：接取任务（InitiateSignal）时启用，之后每 tick 自检，
    /// 并通过 ProcessQuestSignal 处理目标地图的 MapGenerated / NoActiveThreats / AllEnemiesDefeated 信号。
    /// </summary>
    public class QuestPart_SymbiosisCovenantJointOperation : QuestPartActivable
    {
        public enum SymbiosisCovenantJointOperationStage : byte
        {
            OfferPending,
            OperationActive,
            TargetMapEntered,
            ReinforcementsDeployed,
            Succeeded,
            Failed,
            InvalidEnded
        }

        public string? actionId;
        public string? targetQuestTag;
        public SymbiosisCovenantJointOperationStage stage = SymbiosisCovenantJointOperationStage.OfferPending;
        public WorldObject? targetWorldObject;
        public Faction? targetFaction;
        public Faction? proposerFaction;
        public List<Faction>? participantFactions;
        public int offerExpireTick;
        public int operationExpireTick;
        public bool targetMapWasGeneratedByThisOperation;
        public bool reinforcementsGenerated;
        public bool successApplied;
        public bool failureApplied;
        public bool invalidEndApplied;
        public bool declinedOrExpiredApplied;
        public int targetThreatPointsAtDeployment;
        public float totalSupportPointsAtDeployment;
        public List<SymbiosisCovenantJointOperationFactionSupportRecord>? supportRecords;
        public List<string>? spawnedAidTags;

        // 等级 / 援军倍率快照：玩家正式接受任务时锁定，本次行动之后一律使用快照。
        // 这样玩家在 L2 接任务后升到 L5，本次仍然按 L2 的 15% 部署，
        // 下一次新行动才使用新的等级。设计与主脑援军 covenantLevelSnapshot 一致。
        public int covenantLevelSnapshot;
        // 负值表示尚未建立快照（旧存档或尚未接受）：结算时按 LegacySupportPointsFactor 兼容。
        public float supportPointsFactorSnapshot = -1f;

        // 额外运行期字段（随存档）
        public SymbiosisCovenantJointOperationDef? jointOperationDef;
        public bool playerEngaged;

        // 延迟部署与威胁确认状态（随存档）。用于在 MapGenerated 信号内避免过早判定，
        // 等待 SitePartWorker / 守军初始化完成后，再由下一 tick 真正部署援军。
        private int deploymentDueTick = -1;
        private int threatInitializationDeadlineTick = -1;
        public bool targetThreatConfirmed;

        private const int ActiveCheckInterval = 2500;
        // 已部署后周期性直接轮询目标是否清除的间隔（避免完全依赖一次性信号）。
        private const int TargetClearCheckInterval = 60;
        // 地图生成后允许守军初始化的最大保护窗口（不是玩家战斗限时）。
        private const int ThreatInitializationDeadlineTicks = 600;
        // 目标地图登记只负责延迟一 tick 让地图完全进入 Find.Maps；
        // 真正部署仍受玩家单位到场与目标威胁确认双重门控，不可用此延迟代替正式门控。
        private const int TargetMapRegistrationDeploymentDelayTicks = 1;
        // OfferPending 普通 Site：收到 NoActiveThreats / AllEnemiesDefeated 信号后，到真正
        // 用原版活动威胁标准复查之间的延迟窗口，避免守军未初始化即被误判为已清除。
        private const int OfferPendingClearVerificationDelayTicks = 250;
        // OfferPending 的目标守军观察只需低频执行；到期验证仍由每 tick 的廉价整数比较保证准时。
        private const int OfferPendingTargetObservationIntervalTicks = 60;
        private const string AidQuestTagPrefix = "MAP_SymbiosisCovenantJointOp";

        // 联合军事行动诊断日志统一前缀。
        private const string JointOpLogPrefix = "[MAP-JointOperation]";

        // ===== 高科技援军专用安全室外空投参数（不使用原版 CenterDrop，避免落入前哨建筑 / 封闭院落） =====
        // 只在地图中心附近一定范围内寻找空投区，避免援军散落到地图极远处。
        private const float QuickDropAnchorSearchRadius = 80f;
        // GenRadial 要求实际半径严格小于 MaxRadialPatternRadius；
        // 保留少量安全余量，避免触发原版“Not enough squares...”错误。
        private const float QuickDropRadialPatternSafetyMargin = 0.1f;
        // 同一派系的空投格必须集中在一个空投区锚点周围。
        private const float QuickDropZoneRadius = 18f;
        // 两个实际空投格的距离平方至少为 4，即大致保持 2 格距离，避免空投舱重叠。
        private const int QuickDropMinimumSpacingSquared = 4;
        // 防止在大地图中遍历过多候选锚点。
        private const int QuickDropAnchorAttemptLimit = 300;
        // 使用明确的空投舱打开延迟，不依赖未初始化的 IncidentParms 默认值。
        private const int QuickDropPodOpenDelay = 110;
        // 高科技派系“实际采用”的安全室外精确空投方式名称（用于日志与返回值，避免各处散写字符串）。
        private const string SafeOutdoorExactDropModeName = "SafeOutdoorExactDrop";

        // 诊断日志辅助（仅运行期低噪音；不参与任何正式业务/存档逻辑）。
        private string? lastDeploymentWaitReason;

        // ===== 自动诊断日志运行期字段（不参与任何正式业务 / 不存档 / 不改变行为） =====

        // 接取后自动启用检查的一次性调度（运行期）。
        private int activationDiagnosticDueTick = -1;
        private bool activationDiagnosticLogged;

        // OperationActive 下观察到目标地图已存在的一次性观测标记（运行期）。
        private bool operationActiveTargetMapObservedLogged;

        // —— 普通 Site 在 OfferPending 期间的延迟验证状态（防止据点刚生成、守军未初始化，
        // 或一时找不到站立守军就被误判为“彻底清除”而过早成功）——
        // OfferPending 期间首次观察到目标地图有效（且含目标派系守军）的时间。
        private int offerPendingTargetMapFirstObservedTick = -1;
        // 延迟验证到期 tick；< 0 表示尚未排定验证。
        private int offerPendingClearVerificationDueTick = -1;
        // 是否曾观察到目标原本确实存在威胁（守军站场），一旦变 true 不再回退。
        private bool offerPendingTargetThreatObserved;
        // 仅用于诊断：记录最近一次排定验证的原因。
        private string? offerPendingClearVerificationReason;
        // 纯运行期节流：读档后保持 -1，使下一 tick 立即观察一次；不影响结算结果，无需存档。
        private int nextOfferPendingTargetObservationTick = -1;

        // 相关信号日志节流（运行期；只记录每种 tag 的首次出现，避免 NoActiveThreats /
        // AllEnemiesDefeated 等重复信号刷屏）。仅影响日志，不影响游戏逻辑。
        private HashSet<string>? loggedRelevantSignals;

        public bool IsActive =>
            stage != SymbiosisCovenantJointOperationStage.Succeeded
            && stage != SymbiosisCovenantJointOperationStage.Failed
            && stage != SymbiosisCovenantJointOperationStage.InvalidEnded;

        /// <summary>
        /// 是否已接取（OfferPending 不算；仅 OperationActive 及之后阶段算）。
        /// 用于 Settlement 窄范围 Patch 只在已接取且尚未结束的行动上生效。
        /// </summary>
        public bool IsOperationAccepted =>
            stage == SymbiosisCovenantJointOperationStage.OperationActive
            || stage == SymbiosisCovenantJointOperationStage.TargetMapEntered
            || stage == SymbiosisCovenantJointOperationStage.ReinforcementsDeployed;

        public override IEnumerable<GlobalTargetInfo> QuestLookTargets
        {
            get
            {
                if (targetWorldObject != null && !targetWorldObject.Destroyed)
                {
                    yield return new GlobalTargetInfo(targetWorldObject);
                }
            }
        }

        public override IEnumerable<Faction> InvolvedFactions
        {
            get
            {
                if (targetFaction != null)
                {
                    yield return targetFaction;
                }

                if (proposerFaction != null)
                {
                    yield return proposerFaction;
                }

                if (participantFactions != null)
                {
                    foreach (Faction faction in participantFactions)
                    {
                        if (faction != null)
                        {
                            yield return faction;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 玩家接取任务时调用：OfferPending → OperationActive，并设置行动超时（15 天）。
        /// </summary>
        public override void PreQuestAccept()
        {
            base.PreQuestAccept();

            // 诊断调度：在接取后的下一 tick 输出一次 PostAcceptActivationCheck。
            // 此刻仍可能是 NeverEnabled（原版 Accept 顺序：先 PreQuestAccept 再 Initiate），
            // 因此此处只记录时刻、设定 due tick，不判断 State 是否 Enabled。
            TickManager? tickManager = Find.TickManager;
            if (tickManager != null)
            {
                activationDiagnosticDueTick = tickManager.TicksGame + 1;
                activationDiagnosticLogged = false;
            }

            if (stage == SymbiosisCovenantJointOperationStage.OfferPending)
            {
                stage = SymbiosisCovenantJointOperationStage.OperationActive;
                SymbiosisCovenantJointOperationDef? def = ResolveDef();
                operationExpireTick = (tickManager?.TicksGame ?? 0)
                    + (def?.operationTimeoutTicks ?? 900000);

                // 接受瞬间锁定等级与援军倍率：此后本次行动始终使用快照，
                // 既不因中途升级变强，也不因中途降级变弱。
                GameComponent_SymbiosisCovenantState? snapshotState =
                    GameComponent_SymbiosisCovenantState.CurrentComponent;
                covenantLevelSnapshot = snapshotState?.CovenantLevel ?? 0;
                supportPointsFactorSnapshot = def != null
                    ? def.GetSupportPointsFactorForLevel(covenantLevelSnapshot)
                    : SymbiosisCovenantJointOperationDef.LegacySupportPointsFactor;

                int preTick = tickManager?.TicksGame ?? 0;
                LogDeploymentDiagnostic(
                    "QuestAccepted",
                    "actionId=" + (actionId ?? "-")
                    + " | stage=" + stage
                    + " | currentTick=" + preTick
                    + " | operationExpireTick=" + operationExpireTick
                    + " | operationExpireTickNote=15DayTimeoutNotReinforceWait"
                    + " | target=" + (targetWorldObject?.Label ?? "-")
                    + " | targetFaction=" + (targetFaction?.Name ?? "-")
                    + " | participantCount=" + (participantFactions?.Count ?? 0)
                    + " | targetIsMapParent=" + (targetWorldObject is MapParent)
                    + " | targetHasMap=" + (((targetWorldObject as MapParent)?.HasMap) ?? false));

                // 接取时若目标地图已经存在（玩家先进入/生成地图后接取任务），
                // 正式补登记为已进入地图阶段。该地图并非本次行动生成，generatedAfterAcceptance=false。
                MapParent? preAcceptMapParent = targetWorldObject as MapParent;
                Map? preAcceptMap = preAcceptMapParent?.Map;
                if (preAcceptMapParent != null && preAcceptMap != null && !preAcceptMap.Disposed && Find.Maps.Contains(preAcceptMap))
                {
                    TryRegisterAvailableTargetMap(
                        preAcceptMap,
                        generatedAfterAcceptance: false,
                        registrationSource: "PreQuestAcceptExistingMap");
                }
            }
        }

        /// <summary>
        /// 统一登记“目标地图已可用 / 已进入”的入口：OfferPending→OperationActive 之后，
        /// 当目标地图首次出现（玩家先进入后接取、tick 中确认、或 SouthMapEntered 信号生成）
        /// 都汇聚到此处，把阶段推进到 TargetMapEntered 并设置援军部署计时与守军初始化窗口。
        /// generatedAfterAcceptance 标记该地图是否由本次行动生成（用于旧存档恢复与清理判断）。
        /// registrationSource 仅用于诊断日志，便于排查登记来源。
        /// 幂等：若已经是 TargetMapEntered 阶段，不重置部署计时，避免重复登记导致援军永远延后。
        /// </summary>
        private void TryRegisterAvailableTargetMap(Map map, bool generatedAfterAcceptance, string registrationSource)
        {
            if (map == null || map.Disposed || !Find.Maps.Contains(map))
            {
                return;
            }

            // 收紧登记目标：只接受当前任务的精确目标地图（引用相等），禁止其他地图被误登记。
            if (targetWorldObject is not MapParent targetParent
                || !ReferenceEquals(targetParent.Map, map))
            {
                return;
            }

            bool alreadyEntered = stage == SymbiosisCovenantJointOperationStage.TargetMapEntered;
            if (stage != SymbiosisCovenantJointOperationStage.OperationActive && !alreadyEntered)
            {
                // 仅在尚未进入战斗部署阶段前登记；已部署/成功/失败/无效结束不再登记。
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;

            if (!alreadyEntered)
            {
                targetMapWasGeneratedByThisOperation = generatedAfterAcceptance;
                deploymentDueTick = now + TargetMapRegistrationDeploymentDelayTicks;
                // 威胁初始化期限从部署检查时刻继续计算，避免从 now 直接叠加缩短有效初始化窗口。
                threatInitializationDeadlineTick = deploymentDueTick + ThreatInitializationDeadlineTicks;
                stage = SymbiosisCovenantJointOperationStage.TargetMapEntered;
                // 首次登记成功：清除可能存在的等待原因记录。
                ClearDeploymentWaitReason();
            }
            else
            {
                // 已登记过：仅在本次登记为“本次行动生成”时补全标记（理论上首次登记
                // 若 generatedAfterAcceptance=false，后续不会变成 true，这里用 |= 兜底）。
                targetMapWasGeneratedByThisOperation |= generatedAfterAcceptance;
            }

            LogDeploymentDiagnostic(
                "TargetMapRegistered",
                "actionId=" + (actionId ?? "-")
                + " | stage=" + stage
                + " | registrationSource=" + (registrationSource ?? "-")
                + " | generatedAfterAcceptance=" + generatedAfterAcceptance
                + " | targetMapWasGeneratedByThisOperation=" + targetMapWasGeneratedByThisOperation
                + " | deploymentDueTick=" + deploymentDueTick
                + " | threatInitializationDeadlineTick=" + threatInitializationDeadlineTick
                + " | target=" + (targetWorldObject?.Label ?? "-"));
        }

        public override void QuestPartTick()
        {
            base.QuestPartTick();
            if (quest == null || !IsActive || Find.TickManager == null)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;

            // OfferPending（尚未接取）期间：观察目标地图与真实威胁，并运行延迟验证，
            // 以便在玩家已彻底清除目标时（不生成援军）成功结算。这条分支独立于正式行动逻辑，
            // 且必须在 targetWorldObject.Destroyed 等无效结束判断之前处理。
            if (stage == SymbiosisCovenantJointOperationStage.OfferPending)
            {
                // 地图与守军观察降为每秒一次；延迟验证的 dueTick 仍每 tick 检查，确保准时结算。
                if (nextOfferPendingTargetObservationTick < 0
                    || now >= nextOfferPendingTargetObservationTick)
                {
                    nextOfferPendingTargetObservationTick =
                        now + OfferPendingTargetObservationIntervalTicks;
                    ObserveOfferPendingTargetMap(now);
                }

                TryRunOfferPendingClearVerification(now);
                return;
            }

            // 诊断（只读）：接取后下一 tick 一次性检查 Part 是否被正确启用。
            if (activationDiagnosticDueTick >= 0
                && now >= activationDiagnosticDueTick
                && !activationDiagnosticLogged
                && stage != SymbiosisCovenantJointOperationStage.OfferPending)
            {
                activationDiagnosticLogged = true;
                activationDiagnosticDueTick = -1;
                LogPostAcceptActivationCheck(now);
            }

            if (stage == SymbiosisCovenantJointOperationStage.OperationActive
                || stage == SymbiosisCovenantJointOperationStage.TargetMapEntered
                || stage == SymbiosisCovenantJointOperationStage.ReinforcementsDeployed)
            {
                // OperationActive 下若目标地图已存在却尚未登记为 TargetMapEntered，
                // 每 tick 轻量确认并补登记（只检查 MapParent.Map / Disposed / Find.Maps，
                // 不做全图 Pawn 扫描）。登记成功后 stage 会变为 TargetMapEntered，自然停止。
                // 一次性日志标志只限制日志，绝不允许它阻断业务重试。
                if (stage == SymbiosisCovenantJointOperationStage.OperationActive)
                {
                    MapParent? observeParent = targetWorldObject as MapParent;
                    Map? observeMap = observeParent?.Map;
                    if (observeParent != null
                        && observeMap != null
                        && !observeMap.Disposed
                        && Find.Maps.Contains(observeMap))
                    {
                        TryRegisterAvailableTargetMap(
                            observeMap,
                            generatedAfterAcceptance: false,
                            registrationSource: "QuestPartTickExistingMapReconcile");

                        if (!operationActiveTargetMapObservedLogged)
                        {
                            operationActiveTargetMapObservedLogged = true;
                        }
                    }
                }

                // 每个 tick 都可执行的轻量部署检查：进入地图后尽快部署援军，
                // 不等待 ActiveCheckInterval（约 2500 tick）的广泛状态检查。
                QuestPartTickLight(now);

                // 行动超时（15 天）直接判失败；邀请超时由 Quest 原生 acceptanceExpireTick 处理。
                if (operationExpireTick > 0 && now > operationExpireTick)
                {
                    BeginFailure("timeout");
                    return;
                }

                if (now % ActiveCheckInterval != 0)
                {
                    return;
                }

                TickActiveOrDeployed(now);
            }
        }

        /// <summary>
        /// 精确观察本 Part 收到的相关信号（只读诊断）。
        /// 在调用 base 分发逻辑前后各记录一次，用于区分：
        /// - Part 根本没收到信号；
        /// - Part 收到 MapGenerated 但 State 仍是 NeverEnabled；
        /// - Part 收到 MapGenerated 且已 Enabled；
        /// - Part 已进入 ProcessQuestSignal 但登记地图失败。
        /// 不改动原版分发逻辑，必须调用一次 base.Notify_QuestSignalReceived。
        /// </summary>
        public override void Notify_QuestSignalReceived(Signal signal)
        {
            if (targetQuestTag == null || quest == null)
            {
                base.Notify_QuestSignalReceived(signal);
                return;
            }

            string tag = signal.tag ?? string.Empty;
            string mapGeneratedTag = targetQuestTag + ".MapGenerated";
            bool relevant =
                tag == quest.InitiateSignal
                || tag == inSignalEnable
                || tag == mapGeneratedTag
                || tag == targetQuestTag + ".NoActiveThreats"
                || tag == targetQuestTag + ".AllEnemiesDefeated";

            // 信号日志节流：每种相关 tag 只记录首次出现，避免重复信号刷屏。
            bool shouldLog = relevant;
            if (relevant)
            {
                loggedRelevantSignals ??= new HashSet<string>();
                if (loggedRelevantSignals.Contains(tag))
                {
                    shouldLog = false;
                }
                else
                {
                    loggedRelevantSignals.Add(tag);
                }
            }

            QuestPartState stateBefore = State;
            SymbiosisCovenantJointOperationStage stageBefore = stage;

            if (shouldLog)
            {
                SymbiosisCovenantJointOperationDiagnostics.Log(
                    "RelevantSignalReceivedBeforeBase",
                    "actionId=" + (actionId ?? "-")
                    + " | questId=" + quest.id.ToString()
                    + " | signalTag=" + tag
                    + " | signalIsInitiate=" + (tag == quest.InitiateSignal)
                    + " | signalIsEnable=" + (tag == inSignalEnable)
                    + " | signalIsMapGenerated=" + (tag == mapGeneratedTag)
                    + " | stateBefore=" + stateBefore
                    + " | stageBefore=" + stageBefore
                    + " | inSignalEnable=" + (inSignalEnable ?? "-")
                    + " | questInitiateSignal=" + (quest.InitiateSignal ?? "-")
                    + " | targetQuestTag=" + (targetQuestTag ?? "-")
                    + " | currentTick=" + (Find.TickManager?.TicksGame ?? 0));
            }

            // 不改动原版分发逻辑。
            base.Notify_QuestSignalReceived(signal);

            // 未接取（OfferPending）期间允许识别精确目标的 NoActiveThreats / AllEnemiesDefeated。
            // 必须按目标类型区分，绝不在收到信号时立即成功，以防守军尚未初始化被误判：
            // - MAPMechHiveNode：排定延迟验证，只有 MechHiveNodeThreatUtility 判定无威胁
            //   且节点转入 Cleaned 后才成功（节点是 Site，会走与普通 Site 相同的排期入口）。
            // - MAPFactionOutpost：只有 outpost.Cleaned == true 才能成功，否则等待其明确通知。
            // - Settlement：不依赖这些 Site 信号，以 SettlementDefeatUtility Patch 确认 Destroyed 为准。
            // - 其他普通 Site / WorkSite：不立即成功，排定延迟真实验证（可重复收到但不推迟早期限）。
            if (stage == SymbiosisCovenantJointOperationStage.OfferPending
                && targetQuestTag != null
                && (tag == targetQuestTag + ".NoActiveThreats"
                    || tag == targetQuestTag + ".AllEnemiesDefeated"))
            {
                ScheduleOfferPendingClearVerification(
                    "TargetClearSignal:" + tag,
                    Find.TickManager?.TicksGame ?? 0);
            }

            if (shouldLog)
            {
                SymbiosisCovenantJointOperationDiagnostics.Log(
                    "RelevantSignalReceivedAfterBase",
                    "actionId=" + (actionId ?? "-")
                    + " | questId=" + quest.id.ToString()
                    + " | signalTag=" + tag
                    + " | stateAfter=" + State
                    + " | stageAfter=" + stage
                    + " | stateChanged=" + (State != stateBefore)
                    + " | stageChanged=" + (stage != stageBefore)
                    + " | currentTick=" + (Find.TickManager?.TicksGame ?? 0));
            }
        }

        /// <summary>
        /// 处理目标地图的原版 QuestTag 信号（F1）。
        /// 信号由 Site/MapParent 通过 questTags 自动发出：
        /// - &lt;targetQuestTag&gt;.MapGenerated：玩家已进入目标地图 → 仅登记，下一 tick 再部署援军。
        /// - &lt;targetQuestTag&gt;.NoActiveThreats / .AllEnemiesDefeated：站点敌人已清除 → 严格校验后成功结算。
        /// Settlement 的成功不在此处理，由 SymbiosisCovenantJointOperationSettlementPatch 专用 Patch 触发。
        /// </summary>
        protected override void ProcessQuestSignal(Signal signal)
        {
            base.ProcessQuestSignal(signal);
            if (!IsActive || targetQuestTag == null)
            {
                return;
            }

            string tag = signal.tag;

            if (tag == targetQuestTag + ".MapGenerated")
            {
                LogDeploymentDiagnostic(
                    "MapGeneratedSignalReceived",
                    "signalTag=" + tag
                    + " | actionId=" + (actionId ?? "-")
                    + " | stage=" + stage
                    + " | targetQuestTag=" + (targetQuestTag ?? "-")
                    + " | target=" + (targetWorldObject?.Label ?? "-")
                    + " | currentTick=" + (Find.TickManager?.TicksGame ?? 0)
                    + " | questAccepted=" + IsOperationAccepted);

                // 只登记：目标地图已生成，但此时 SitePartWorker / 守军尚未完成初始化。
                // 不在本信号调用链里生成援军或判定成功，交由下一 tick 的轻量部署检查处理。
                NotifyTargetMapGenerated();
                return;
            }

            if (tag == targetQuestTag + ".NoActiveThreats"
                || tag == targetQuestTag + ".AllEnemiesDefeated")
            {
                // 仅对 Site / Outpost / WorkSite 生效。信号只负责提醒状态机立即重新检查，
                // 不能独立决定任务成功：必须已到达战斗就绪状态，再由统一轮询兜底判定。
                if (targetWorldObject is Site && IsOperationAccepted && HasReachedCombatReadyState)
                {
                    TryCompleteOperationIfTargetCleared();
                }
            }
        }

        /// <summary>
        /// 地图生成信号登记：设置进入地图阶段与延迟部署/威胁初始化窗口。
        /// 不在此处生成援军、不在此处判定成功、不在此处设置 ReinforcementsDeployed。
        /// </summary>
        private void NotifyTargetMapGenerated()
        {
            // 与 ProcessQuestSignal 中其它信号处理一致：统一走登记入口。
            Map? map = (targetWorldObject as MapParent)?.Map;
            if (map == null)
            {
                LogDeploymentDiagnostic(
                    "MapGeneratedIgnoredNoMap",
                    "stage=" + stage
                    + " | target=" + (targetWorldObject?.Label ?? "-")
                    + " | targetIsMapParent=" + (targetWorldObject is MapParent)
                    + " | currentTick=" + (Find.TickManager?.TicksGame ?? 0));
                return;
            }

            // 接取后由 MapGenerated 信号触发，视为本次行动生成的地图。
            TryRegisterAvailableTargetMap(
                map,
                generatedAfterAcceptance: true,
                registrationSource: "MapGeneratedSignal");
        }

        // ===== OfferPending（尚未接取）期间：观察目标地图与真实威胁 =====

        /// <summary>
        /// 仅观察目标地图并判定目标派系是否原本确实存在威胁（守军站场）。
        /// 只处理 Site / WorkSite（MAPFactionOutpost 的正式成功仍优先以其 Cleaned 为准；
        /// 机械巢节点用 MechHiveNodeThreatUtility 判定，兼容休眠与倒地机械族）；
        /// 不在此处做任何成功结算。第一次观察到有效地图时记录 firstObservedTick；
        /// 一旦观察到目标派系守军，offerPendingTargetThreatObserved 置 true 且不再回退。
        /// </summary>
        private void ObserveOfferPendingTargetMap(int now)
        {
            if (targetWorldObject == null || targetWorldObject is Settlement)
            {
                // Settlement 的成功以 SettlementDefeatUtility Patch 确认 Destroyed 为准，不在此观察。
                return;
            }

            if (targetWorldObject is MAPFactionOutpost)
            {
                // 前哨可观察，但其成功以 outpost.Cleaned 为准，这里不预先设置威胁证据
                // （前哨清理逻辑会独立通知）；若前哨确实有守军也可作为威胁证据，但不强求。
                return;
            }

            if (targetWorldObject is not Site)
            {
                return;
            }

            Map? map = (targetWorldObject as MapParent)?.Map;
            if (map == null || map.Disposed || !Find.Maps.Contains(map))
            {
                return;
            }

            if (offerPendingTargetMapFirstObservedTick < 0)
            {
                offerPendingTargetMapFirstObservedTick = now;
            }

            if (!offerPendingTargetThreatObserved)
            {
                // 机械巢节点守军可能全部休眠，站立 Pawn 判定会漏判；
                // 必须用节点专用威胁判定（含休眠/倒地机械族、炮塔、护盾、状态建筑、生成器）。
                bool nodeThreat = targetWorldObject is MAPMechHiveNode
                    ? MechHiveNodeThreatUtility.AnyMechHiveThreatOnMap(map)
                    : AnyStandingTargetFactionDefender(map);
                if (nodeThreat)
                {
                    offerPendingTargetThreatObserved = true;
                }
            }
        }

        /// <summary>
        /// 收到 NoActiveThreats / AllEnemiesDefeated 信号时为普通 Site 排定延迟真实验证。
        /// MAPFactionOutpost / Settlement 不走此处（各自有独立成功入口）。
        /// 重复收到信号时不会不断把期限向后推迟；若已排定则保留较早期限。
        /// </summary>
        private void ScheduleOfferPendingClearVerification(string reason, int now)
        {
            if (targetWorldObject == null
                || targetWorldObject is MAPFactionOutpost
                || targetWorldObject is Settlement)
            {
                return;
            }

            if (targetWorldObject is not Site)
            {
                return;
            }

            int due = now + OfferPendingClearVerificationDelayTicks;
            if (offerPendingTargetMapFirstObservedTick >= 0)
            {
                // 验证时间不得早于“地图首次观察 + 延迟窗口”，避免守军未初始化即被复查。
                due = Math.Max(
                    due,
                    offerPendingTargetMapFirstObservedTick + OfferPendingClearVerificationDelayTicks);
            }

            if (offerPendingClearVerificationDueTick < 0)
            {
                offerPendingClearVerificationDueTick = due;
            }

            offerPendingClearVerificationReason = reason;
        }

        /// <summary>
        /// 延迟到期后用原版活动威胁标准复查普通 Site，确认目标被真实清除后才成功。
        /// 只在 stage == OfferPending 时执行；未到期或尚未排定直接返回。
        /// 验证失败后清除本次 dueTick，等待下一次真实信号，不做每 tick 重复昂贵检查。
        /// </summary>
        private void TryRunOfferPendingClearVerification(int now)
        {
            if (stage != SymbiosisCovenantJointOperationStage.OfferPending)
            {
                return;
            }

            if (offerPendingClearVerificationDueTick < 0 || now < offerPendingClearVerificationDueTick)
            {
                return;
            }

            WorldObject? target = targetWorldObject;
            if (target == null)
            {
                offerPendingClearVerificationDueTick = -1;
                return;
            }

            // 机械巢节点：MapGenerated 早期守军尚未初始化时不得误判成功；
            // 必须曾观察到真实节点威胁、初始化已成功，且 MechHiveNodeThreatUtility 判定无威胁，
            // 才通过联合行动专用入口转入 Cleaned，再由 node.Cleaned 决定成功。
            if (target is MAPMechHiveNode node)
            {
                Map? nodeMap = (target as MapParent)?.Map;
                if (nodeMap == null || nodeMap.Disposed || !Find.Maps.Contains(nodeMap))
                {
                    offerPendingClearVerificationDueTick = -1;
                    return;
                }

                if (!offerPendingTargetThreatObserved
                    || node.MapInitState != MechHiveNodeMapInitState.Succeeded)
                {
                    offerPendingClearVerificationDueTick = -1;
                    return;
                }

                if (MechHiveNodeThreatUtility.AnyMechHiveThreatOnMap(nodeMap))
                {
                    offerPendingClearVerificationDueTick = -1;
                    return;
                }

                node.TryMarkCleanedByJointOperationIfNoThreats();
                if (node.Cleaned)
                {
                    TryCompleteOfferPendingTarget(
                        node,
                        offerPendingClearVerificationReason ?? "MechHiveNodeCleaned");
                }

                offerPendingClearVerificationDueTick = -1;
                return;
            }

            // Settlement 不在此处理；前哨以 Cleaned 为准（由独立通知触发）。
            if (target is MAPFactionOutpost outpost)
            {
                if (outpost.Cleaned)
                {
                    TryCompleteOfferPendingTarget(outpost, offerPendingClearVerificationReason ?? "OutpostCleaned");
                }

                offerPendingClearVerificationDueTick = -1;
                return;
            }

            if (target is not Site site)
            {
                offerPendingClearVerificationDueTick = -1;
                return;
            }

            if (site.Destroyed)
            {
                // 已销毁可直接视为真实清除。
                TryCompleteOfferPendingTarget(site, offerPendingClearVerificationReason ?? "SiteDestroyed");
                offerPendingClearVerificationDueTick = -1;
                return;
            }

            Map? map = (target as MapParent)?.Map;
            if (map == null || map.Disposed || !Find.Maps.Contains(map))
            {
                // 地图不存在/已释放/不在 Find.Maps：不视为清除。
                offerPendingClearVerificationDueTick = -1;
                return;
            }

            // 必须有“目标原本确实存在威胁”的证据，避免守军尚未初始化即被误判为无威胁。
            bool hadThreatEvidence = offerPendingTargetThreatObserved || (site.ActualThreatPoints > 0);
            if (!hadThreatEvidence)
            {
                offerPendingClearVerificationDueTick = -1;
                return;
            }

            // 使用原版活动威胁标准重新检查（包含休眠/可雾化的敌方 Pawn）。
            if (GenHostility.AnyHostileActiveThreatToPlayer(
                    map,
                    countDormantPawnsAsHostile: true,
                    canBeFogged: true))
            {
                // 仍有活动敌对威胁：不成功，清除本次期限等待下一次真实信号。
                offerPendingClearVerificationDueTick = -1;
                return;
            }

            TryCompleteOfferPendingTarget(site, offerPendingClearVerificationReason ?? "VerifiedNoThreat");
            offerPendingClearVerificationDueTick = -1;
        }

        private void TickActiveOrDeployed(int now)
        {
            // 部署由 QuestPartTick 每 tick 的轻量检查（TryDeployReinforcementsWhenReady）处理，
            // 此处不再重复；只做广泛状态校验。

            if (!reinforcementsGenerated)
            {
                // 未部署阶段的无效结束：目标被第三方移除、目标不再敌对玩家、
                // 参与派系全部失效、盟约降到 L3 以下。
                if (targetWorldObject == null || targetWorldObject.Destroyed)
                {
                    BeginInvalidEnd("targetRemoved");
                    return;
                }

                if (targetFaction == null
                    || Faction.OfPlayer == null
                    || !targetFaction.HostileTo(Faction.OfPlayer))
                {
                    BeginInvalidEnd("targetNoLongerHostile");
                    return;
                }

                if (!AnyValidParticipantRemaining())
                {
                    BeginInvalidEnd("noValidParticipant");
                    return;
                }

                // 只有跌到解锁等级以下才因等级不足终止；L2~L5 之间升降不取消已接取的行动。
                if (!GameComponent_SymbiosisCovenantState.IsActive
                    || GameComponent_SymbiosisCovenantState.CurrentComponent?.CovenantLevel
                        < SymbiosisCovenantJointOperationDef.MinimumCovenantLevel)
                {
                    BeginInvalidEnd("covenantLevelTooLow");
                    return;
                }

                return;
            }

            // 先检查目标 WorldObject 是否已不存在（被摧毁 / 移除）。
            if (targetWorldObject == null)
            {
                BeginInvalidEnd("targetMissing");
                return;
            }

            if (targetWorldObject.Destroyed)
            {
                // 仅在已到达战斗就绪状态后，才把目标销毁视为成功兜底；
                // 否则（玩家尚未进入/援军未生成/威胁未确认）按无效结束。
                if (HasReachedCombatReadyState)
                {
                    BeginSuccess();
                }
                else
                {
                    BeginInvalidEnd("targetRemovedBeforeCombatReady");
                }

                return;
            }

            Map? targetMap = (targetWorldObject as MapParent)?.Map;
            if (targetMap == null || targetMap.Disposed || !Find.Maps.Contains(targetMap))
            {
                // 目标仍存活但地图已消失：成功应由 NoActiveThreats / AllEnemiesDefeated（Site）
                // 或 SettlementDefeatUtility Patch（Settlement）在更早时机结算；
                // 否则按无效结束处理。
                BeginInvalidEnd("targetMapRemovedTargetAlive");
                return;
            }

            if (targetFaction == null || !targetFaction.HostileTo(Faction.OfPlayer))
            {
                BeginInvalidEnd("targetNoLongerHostile");
                return;
            }

            // 联合援军的离场与任务成功必须以既有的原版活动威胁清除标准为准：
            // 由 QuestPartTickLight 周期性调用 TryCompleteOperationIfTargetCleared 判定，
            // 以及 Site 发出的 <targetQuestTag>.NoActiveThreats / .AllEnemiesDefeated 信号触发。
            // 目标派系是否还有站立 Pawn 不能单独决定离场：地图可能仍存在原版活动威胁
            // （敌对炮塔、倒地敌人、其他敌对派系单位等），此时援军不得提前撤离。
            // 本分支不再调用 CommandReinforcementsLeave()。
        }

        /// <summary>
        /// 每个 tick 执行的轻量部署检查：当到达部署时刻即尝试部署；不等待 ActiveCheckInterval。
        /// </summary>
        private void QuestPartTickLight(int now)
        {
            if (stage == SymbiosisCovenantJointOperationStage.TargetMapEntered)
            {
                if (now >= deploymentDueTick)
                {
                    TryDeployReinforcementsWhenReady(now);
                }

                return;
            }

            if (stage == SymbiosisCovenantJointOperationStage.ReinforcementsDeployed)
            {
                // 旧存档恢复：若玩家单位仍在目标地图但 playerEngaged 未置位，重新置位，
                // 避免读档后任务永久卡死。
                if (!playerEngaged)
                {
                    Map? recoverMap = (targetWorldObject as MapParent)?.Map;
                    if (recoverMap != null
                        && !recoverMap.Disposed
                        && Find.Maps.Contains(recoverMap)
                        && HasPlayerControlledPawnOnTargetMap(recoverMap))
                    {
                        playerEngaged = true;
                    }
                }

                if (now % TargetClearCheckInterval == 0)
                {
                    TryCompleteOperationIfTargetCleared();
                }
            }
        }

        /// <summary>
        /// 进入地图阶段后，下一 tick 起确认玩家单位已进入且目标守军已初始化，
        /// 满足条件才真正部署援军；若初始化窗口内从未确认目标威胁，按无效结束收尾。
        /// </summary>
        private void TryDeployReinforcementsWhenReady(int now)
        {
            if (reinforcementsGenerated)
            {
                return;
            }

            if (stage != SymbiosisCovenantJointOperationStage.TargetMapEntered)
            {
                return;
            }

            Map? map = (targetWorldObject as MapParent)?.Map;
            if (map == null)
            {
                LogDeploymentWaitOnce(
                    "WaitingForTargetMap",
                    "reason=MapNull"
                    + " | stage=" + stage
                    + " | target=" + (targetWorldObject?.Label ?? "-")
                    + " | tick=" + now);
                return;
            }

            if (map.Disposed)
            {
                LogDeploymentWaitOnce(
                    "WaitingForTargetMap",
                    "reason=MapDisposed"
                    + " | stage=" + stage
                    + " | target=" + (targetWorldObject?.Label ?? "-")
                    + " | tick=" + now);
                return;
            }

            if (!Find.Maps.Contains(map))
            {
                LogDeploymentWaitOnce(
                    "WaitingForTargetMap",
                    "reason=MapNotInFindMaps"
                    + " | stage=" + stage
                    + " | target=" + (targetWorldObject?.Label ?? "-")
                    + " | tick=" + now);
                return;
            }

            // 必须确认玩家单位确实在目标地图（兼容纯机械族殖民地：pawn.Faction == OfPlayer）。
            if (!HasPlayerControlledPawnOnTargetMap(map))
            {
                int allSpawned = map.mapPawns.AllPawnsSpawned.Count;
                bool playerFactionNull = Faction.OfPlayer == null;
                int playerPawnCount = 0;
                int hostPlayerPawnCount = 0;
                Faction? playerF = Faction.OfPlayer;
                if (playerF != null)
                {
                    foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
                    {
                        if (p == null)
                        {
                            continue;
                        }

                        if (p.Faction == playerF)
                        {
                            playerPawnCount++;
                        }
                        else if (p.HostFaction == playerF)
                        {
                            hostPlayerPawnCount++;
                        }
                    }
                }

                LogDeploymentWaitOnce(
                    "WaitingForPlayerPawn",
                    "reason=NoPlayerControlledPawnOnTargetMap"
                    + " | stage=" + stage
                    + " | map=" + map.GetUniqueLoadID()
                    + " | allSpawnedPawnCount=" + allSpawned
                    + " | playerFactionNull=" + playerFactionNull
                    + " | playerFactionPawnCount=" + playerPawnCount
                    + " | hostFactionIsPlayerPawnCount=" + hostPlayerPawnCount
                    + " | tick=" + now);
                return;
            }

            // 玩家单位确已到场：设置 playerEngaged，否则任何成功路径都无法通过。
            // 普通友军、盟约援军、敌方单位都不算玩家到场。
            ClearDeploymentWaitReason();
            if (!playerEngaged)
            {
                LogDeploymentDiagnostic(
                    "PlayerPawnConfirmed",
                    "stage=" + stage
                    + " | map=" + map.GetUniqueLoadID()
                    + " | tick=" + now);
            }

            playerEngaged = true;

            // 必须观察到真实目标威胁（守军/敌对单位曾经生成）。
            if (!TryConfirmTargetThreat(map))
            {
                // 统计（仅用于诊断日志，不改变 FactionOutpostThreatUtility 的业务判断）。
                int targetPawnTotal = 0;
                int targetPawnStanding = 0;
                Faction? tf = targetFaction;
                if (tf != null)
                {
                    foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
                    {
                        if (p == null || p.Faction != tf)
                        {
                            continue;
                        }

                        targetPawnTotal++;
                        if (!p.Dead && !p.Downed)
                        {
                            targetPawnStanding++;
                        }
                    }
                }

                bool garrisonInit = targetWorldObject is MAPFactionOutpost gOutpost
                    && gOutpost.MapGarrisonInitialized;
                string targetType = targetWorldObject?.GetType().Name ?? "-";

                LogDeploymentWaitOnce(
                    "WaitingForTargetThreat",
                    "reason=TargetThreatNotConfirmed"
                    + " | stage=" + stage
                    + " | targetType=" + targetType
                    + " | targetFaction=" + (tf?.Name ?? "-")
                    + " | garrisonInitialized=" + garrisonInit
                    + " | targetThreatConfirmed=" + targetThreatConfirmed
                    + " | targetPawnTotal=" + targetPawnTotal
                    + " | targetPawnStanding=" + targetPawnStanding
                    + " | tick=" + now
                    + " | deadline=" + threatInitializationDeadlineTick);

                // 初始化窗口内尚未观察到目标威胁：继续等待，超出窗口则无效结束。
                if (now >= threatInitializationDeadlineTick)
                {
                    Log.Warning(
                        JointOpLogPrefix + " Event=TargetThreatInitializationTimedOut"
                        + " | actionId=" + (actionId ?? "-")
                        + " | stage=" + stage
                        + " | target=" + (targetWorldObject?.Label ?? "-")
                        + " | map=" + map.GetUniqueLoadID()
                        + " | playerEngaged=" + playerEngaged
                        + " | garrisonInitialized=" + garrisonInit
                        + " | targetThreatConfirmed=" + targetThreatConfirmed
                        + " | targetPawnTotal=" + targetPawnTotal
                        + " | targetPawnStanding=" + targetPawnStanding
                        + " | currentTick=" + now
                        + " | deadlineTick=" + threatInitializationDeadlineTick);

                    BeginInvalidEnd("targetThreatNeverInitialized");
                }

                return;
            }

            // 已确认目标威胁，即将真正部署援军。
            ClearDeploymentWaitReason();
            LogDeploymentDiagnostic(
                "DeploymentStarted",
                "actionId=" + (actionId ?? "-")
                + " | stage=" + stage
                + " | target=" + (targetWorldObject?.Label ?? "-")
                + " | targetThreatPointsAtDeployment=pending"
                + " | currentTick=" + now);

            // 真正部署援军：只有成功才进入 ReinforcementsDeployed，否则进入无效结束，
            // 绝不能把失败的部署覆盖成“已部署”状态。
            if (TryDeployReinforcements(map))
            {
                reinforcementsGenerated = true;
                stage = SymbiosisCovenantJointOperationStage.ReinforcementsDeployed;
                // 目标守军继续使用文化 DLC Work Site 的原版 DefendBase 状态机；
                // 联合行动只部署援军，不强制改写或迁移守军 Lord。
            }
            else
            {
                BeginInvalidEnd("reinforcementsDeployFailed");
            }
        }

        /// <summary>
        /// 兼容纯机械族殖民地：玩家控制的、已生成且未死亡的单位（含自由殖民者、
        /// 玩家机械体，以及通过 HostFaction 归属玩家的单位）都算玩家单位。
        /// </summary>
        private static bool HasPlayerControlledPawnOnTargetMap(Map map)
        {
            if (Faction.OfPlayer == null)
            {
                return false;
            }

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn == null || pawn.Destroyed || pawn.Dead)
                {
                    continue;
                }

                if (pawn.Faction == Faction.OfPlayer)
                {
                    return true;
                }

                // 通过 HostFaction 归属玩家的单位（如被玩家俘获/奴役的单位）。
                if (pawn.HostFaction == Faction.OfPlayer)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 判断目标地图中是否仍存在“任务目标派系”的真实站立防御者。
        /// 不依赖“地图上所有敌人”的判定，避免其他敌对事件/派系的敌人干扰。
        /// 自然包含：休眠但仍活着的守军、处于迷雾中的守军、尚未开始攻击但真实存在的守军。
        /// </summary>
        private bool AnyStandingTargetFactionDefender(Map map)
        {
            if (map == null || map.Disposed || targetFaction == null)
            {
                return false;
            }

            Faction player = Faction.OfPlayer;
            foreach (Pawn pawn in map.mapPawns.SpawnedPawnsInFaction(targetFaction))
            {
                if (pawn == null
                    || pawn.Destroyed
                    || pawn.Dead
                    || !pawn.Spawned
                    || pawn.Map != map)
                {
                    continue;
                }

                if (pawn.Downed)
                {
                    continue;
                }

                if (player == null || pawn.HostileTo(player))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 确认目标地图中存在真实目标威胁（守军/敌对单位曾经生成）。
        /// 对 MAPFactionOutpost 优先确认守军初始化并用 FactionOutpostThreatUtility 检查；
        /// 对普通 Site / 文化 DLC WorkSite / Settlement 使用 AnyStandingTargetFactionDefender
        /// （精确按目标派系判断，兼容休眠/迷雾守军）。
        /// 只有观察到了目标威胁才设置 targetThreatConfirmed。
        /// </summary>
        private bool TryConfirmTargetThreat(Map map)
        {
            if (targetThreatConfirmed)
            {
                return true;
            }

            // 目标仍存活才可确认威胁（已被移除无法确认）。
            if (targetWorldObject == null || targetWorldObject.Destroyed)
            {
                return false;
            }

            bool hasThreat = false;
            if (targetWorldObject is MAPMechHiveNode node)
            {
                // 机械巢节点的威胁包含休眠/倒地但未死亡的机械族、炮塔、高低角护盾、
                // 状态建筑与机械族生成器：不得只用「站立目标派系 Pawn」判断，
                // 否则守军尚未苏醒时会误判为没有威胁。
                if (node.MapInitState == MechHiveNodeMapInitState.Succeeded
                    && map == node.Map
                    && MechHiveNodeThreatUtility.AnyMechHiveThreatOnMap(map))
                {
                    hasThreat = true;
                }
            }
            else if (targetWorldObject is MAPFactionOutpost outpost)
            {
                if (outpost.MapGarrisonInitialized
                    && map == outpost.Map
                    && FactionOutpostThreatUtility.AnyStandingDefender(map, outpost.Faction))
                {
                    hasThreat = true;
                }
            }
            else if (targetWorldObject is Site)
            {
                // 普通 Site / 文化 DLC WorkSite：精确判断目标派系站立防御者，
                // 兼容休眠/迷雾中的守军，避免其他敌对事件干扰。
                hasThreat = AnyStandingTargetFactionDefender(map);
            }
            else if (targetWorldObject is Settlement)
            {
                // Settlement 地图已生成，且存在目标派系的 hostile 守军。
                // 具体“是否彻底清除”由 SettlementDefeatUtility Patch 另行判定成功，
                // 此处只确认“曾经存在威胁”。
                hasThreat = AnyStandingTargetFactionDefender(map);
            }

            if (hasThreat)
            {
                targetThreatConfirmed = true;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 统一成功前置条件：必须已进入战斗就绪状态，才能允许任何成功路径。
        /// </summary>
        private bool HasReachedCombatReadyState =>
            stage == SymbiosisCovenantJointOperationStage.ReinforcementsDeployed
            && reinforcementsGenerated
            && playerEngaged
            && targetThreatConfirmed;

        /// <summary>
        /// 统一成功轮询：仅在已到达战斗就绪状态后，根据目标 WorldObject 类型精确判断目标是否已清除，
        /// 成功时调用一次 BeginSuccess。即使在 MapGenerated 早期 NoActiveThreats 信号已发过、
        /// 或原版 Site 后续不再重复发送信号，任务仍能通过真实地图状态完成。
        /// </summary>
        private bool TryCompleteOperationIfTargetCleared()
        {
            if (!HasReachedCombatReadyState)
            {
                return false;
            }

            WorldObject? target = targetWorldObject;
            if (target == null)
            {
                return false;
            }

            // 目标在玩家进入、威胁确认、援军部署之后被摧毁，可作为成功兜底，
            // 避免 WorldObject 替换后任务卡死。
            if (target.Destroyed)
            {
                BeginSuccess();
                return true;
            }

            Map? map = (target as MapParent)?.Map;
            if (map == null || map.Disposed || !Find.Maps.Contains(map))
            {
                return false;
            }

            // MAPMechHiveNode 本身是 Site，必须最先判断，再判断 MAPFactionOutpost，最后才是普通 Site。
            // 多蓝图中任何一个集群仍有有效节点威胁时，任务都不得完成。
            if (target is MAPMechHiveNode node)
            {
                node.TryMarkCleanedByJointOperationIfNoThreats();

                if (node.Cleaned)
                {
                    BeginSuccess();
                    return true;
                }

                return false;
            }

            // MAPFactionOutpost 本身是 Site，必须先判断，再判断普通 Site。
            if (target is MAPFactionOutpost outpost)
            {
                if (!outpost.MapGarrisonInitialized)
                {
                    return false;
                }

                outpost.TryMarkCleanedIfNoActiveThreats();

                if (outpost.Cleaned)
                {
                    BeginSuccess();
                    return true;
                }

                return false;
            }

            if (target is Settlement)
            {
                // Settlement 的正式成功仍以 SettlementDefeatUtility.CheckDefeated Patch
                // 或目标被原版替换/摧毁为准。
                // 不要仅因所有守军暂时倒地就直接完成据点摧毁任务。
                return false;
            }

            if (target is Site)
            {
                if (!AnyStandingTargetFactionDefender(map))
                {
                    BeginSuccess();
                    return true;
                }

                return false;
            }

            return false;
        }

        // ===== 援军生成（B / C / D / H） =====

        /// <summary>
        /// 本次行动实际使用的援军点数倍率。始终使用接受任务时锁定的快照；
        /// 只有在快照缺失（更新前的旧存档已接受行动）时才回退到旧版固定 50%，
        /// 绝不用部署时的当前盟约等级重新计算。
        /// </summary>
        private float ResolveSupportPointsFactor()
        {
            return supportPointsFactorSnapshot < 0f
                ? SymbiosisCovenantJointOperationDef.LegacySupportPointsFactor
                : supportPointsFactorSnapshot;
        }

        private bool TryDeployReinforcements(Map targetMap)
        {
            SymbiosisCovenantJointOperationDef? def = ResolveDef();
            if (def == null)
            {
                Log.Warning(
                    JointOpLogPrefix + " Event=DeploymentFailed"
                    + " | reason=JointOperationDefNull"
                    + " | actionId=" + (actionId ?? "-")
                    + " | target=" + (targetWorldObject?.Label ?? "-")
                    + " | tick=" + (Find.TickManager?.TicksGame ?? 0));
                return false;
            }

            // H：真实目标威胁点（不再使用 StorytellerUtility.DefaultThreatPointsNow）。
            int threat = SymbiosisCovenantJointOperationUtility.TryGetTargetThreatPointsAtDeployment(
                targetWorldObject, targetMap);
            targetThreatPointsAtDeployment = threat;

            // B：所有参与派系“合计”的援军点数 = 目标威胁点 × 接取时锁定的等级倍率。
            float factor = ResolveSupportPointsFactor();
            float total = threat * factor;
            totalSupportPointsAtDeployment = total;

            LogDeploymentDiagnostic(
                "SupportPointsCalculated",
                "targetThreatPoints=" + threat
                + " | supportPointsFactor=" + factor.ToString("F3")
                + " | covenantLevelSnapshot=" + covenantLevelSnapshot
                + " | totalSupportPoints=" + total.ToString("F1")
                + " | targetThreatSource=" + SymbiosisCovenantJointOperationUtility.GetTargetThreatSourceName(targetWorldObject)
                + (targetWorldObject is MAPMechHiveNode node
                    ? " | targetNodePhase=" + (node.IsCompleted ? "Completed" : "Building")
                      + " | targetGarrisonBudget=" + node.GarrisonThreatPoints
                      + " | targetHasSavedSnapshot=" + node.HasGarrisonThreatPointsSnapshot
                    : string.Empty)
                + (targetWorldObject is MAPFactionOutpost outpost
                    ? " | targetOutpostPhase=" + (outpost.IsCompleted ? "Completed" : "Building")
                      + " | targetGarrisonBudget=" + outpost.GarrisonThreatPoints
                      + " | targetHasSavedSnapshot=" + outpost.HasGarrisonThreatPointsSnapshot
                    : string.Empty)
                + " | target=" + (targetWorldObject?.Label ?? "-")
                + " | tick=" + (Find.TickManager?.TicksGame ?? 0));

            supportRecords ??= new List<SymbiosisCovenantJointOperationFactionSupportRecord>();
            spawnedAidTags ??= new List<string>();

            // 仅诊断：对每个原始参与派系逐项校验并输出（不改变业务合法性）。
            if (participantFactions != null)
            {
                foreach (Faction participant in participantFactions)
                {
                    bool validNow = TryGetParticipantValidity(participant, out string reason);
                    LogDeploymentDiagnostic(
                        "ParticipantValidation",
                        "faction=" + (participant?.Name ?? "-")
                        + " | loadId=" + (participant?.loadID.ToString() ?? "-")
                        + " | valid=" + validNow
                        + " | reason=" + reason);
                }
            }

            // 只将点数分给部署时仍有效、且能生成战斗编组的参与派系（最多 maxParticipants）。
            List<Faction> valid = ValidParticipantsNow();

            LogDeploymentDiagnostic(
                "ParticipantsValidated",
                "configuredParticipantCount=" + (participantFactions?.Count ?? 0)
                + " | validParticipantCount=" + valid.Count
                + " | validFactionNames=" + string.Join(",", valid.Select(f => f.Name))
                + " | tick=" + (Find.TickManager?.TicksGame ?? 0));

            if (valid.Count == 0)
            {
                Log.Warning(
                    JointOpLogPrefix + " Event=DeploymentFailed"
                    + " | reason=NoValidParticipants"
                    + " | actionId=" + (actionId ?? "-")
                    + " | target=" + (targetWorldObject?.Label ?? "-")
                    + " | tick=" + (Find.TickManager?.TicksGame ?? 0));
                return false;
            }

            float[] assigned = AllocatePoints(valid, total);

            // C：统一抵达方式由“全部有效参与派系中的最高科技等级”决定（阈值 industrialArrivalThreshold 不变）。
            // 只要至少一个有效参与派系达到阈值，全部援军统一空投；否则全部援军统一边缘步行。
            // 严禁同一次行动中一部分派系空投、另一部分派系步行（每个派系的 arrivalModePlanned 来自同一判定）。
            bool operationQuickDropRequested = valid.Any(
                faction =>
                    faction != null
                    && (int)faction.def.techLevel >= (int)def.industrialArrivalThreshold);

            TechLevel highestParticipantTechLevel = TechLevel.Animal;
            foreach (Faction validFaction in valid)
            {
                if (validFaction == null)
                {
                    continue;
                }

                if ((int)validFaction.def.techLevel > (int)highestParticipantTechLevel)
                {
                    highestParticipantTechLevel = validFaction.def.techLevel;
                }
            }

            string plannedArrivalMode = operationQuickDropRequested
                ? SafeOutdoorExactDropModeName
                : PawnsArrivalModeDefOf.EdgeWalkIn.defName;

            // 阶段1：生成但不部署。严禁在此阶段调用 Arrive / MakeDropPodAt / MakeNewLord。
            List<PendingJointOperationAidGroup> pendingGroups = new List<PendingJointOperationAidGroup>();
            List<Pawn> allPendingPawns = new List<Pawn>();
            for (int i = 0; i < valid.Count; i++)
            {
                Faction participant = valid[i];
                float points = assigned[i];
                if (points <= 0f)
                {
                    continue;
                }

                List<Pawn>? pawns = GenerateCombatGroup(participant, points, targetMap, def);
                if (pawns == null || pawns.Count == 0)
                {
                    LogDeploymentDiagnostic(
                        "PawnGenerationSkipped",
                        "reason=" + (pawns == null ? "GenerateCombatGroupReturnedNull" : "GenerateCombatGroupReturnedZero")
                        + " | faction=" + participant.Name
                        + " | points=" + points.ToString("F1"));
                    continue;
                }

                LogDeploymentDiagnostic(
                    "SupportAllocated",
                    "faction=" + participant.Name
                    + " | assignedPoints=" + points.ToString("F1")
                    + " | canGenerateCombatGroup=" + CanGenerateCombatGroup(participant)
                    + " | arrivalModePlanned=" + plannedArrivalMode
                    + " | pawnCount=" + pawns.Count);

                string aidTag = MakeAidTag(targetMap, participant);
                pendingGroups.Add(new PendingJointOperationAidGroup(participant, points, pawns, aidTag));
                allPendingPawns.AddRange(pawns);
            }

            if (pendingGroups.Count == 0)
            {
                Log.Warning(
                    JointOpLogPrefix + " Event=DeploymentFailed"
                    + " | reason=NoPawnsGeneratedForAnyParticipant"
                    + " | actionId=" + (actionId ?? "-")
                    + " | target=" + (targetWorldObject?.Label ?? "-")
                    + " | validParticipantCount=" + valid.Count
                    + " | totalSupportPoints=" + total.ToString("F1")
                    + " | tick=" + (Find.TickManager?.TicksGame ?? 0));
                return false;
            }

            // 阶段2：统一决定实际抵达方式（在任何 Pawn 进入地图前确定）。
            string actualOperationArrivalMode;
            string fallbackReason = "None";
            bool safeCellsResolved = false;
            bool preparedPodsResolved = false;
            List<IntVec3> allDropCells = new List<IntVec3>();
            List<ActiveTransporterInfo> allPreparedPods = new List<ActiveTransporterInfo>();

            if (!operationQuickDropRequested)
            {
                actualOperationArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn.defName;
            }
            else
            {
                safeCellsResolved = TryFindSafeJointOperationDropCells(
                    targetMap, allPendingPawns.Count, out allDropCells);

                if (safeCellsResolved && allDropCells.Count == allPendingPawns.Count)
                {
                    preparedPodsResolved = TryPrepareExactDropPods(allPendingPawns, out allPreparedPods);
                }

                if (safeCellsResolved
                    && allDropCells.Count == allPendingPawns.Count
                    && preparedPodsResolved
                    && allPreparedPods.Count == allPendingPawns.Count)
                {
                    actualOperationArrivalMode = SafeOutdoorExactDropModeName;
                }
                else
                {
                    // 整批回退边缘步行，严禁部分空投、部分步行。
                    fallbackReason =
                        (!safeCellsResolved
                            ? "SafeCellsNotResolved"
                            : (!preparedPodsResolved
                                ? "DropPodsNotPrepared"
                                : "MismatchedCounts"));
                    ReleasePreparedDropPods(allPendingPawns, allPreparedPods);
                    allDropCells.Clear();
                    allPreparedPods.Clear();
                    actualOperationArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn.defName;
                }
            }

            // 统一抵达方式完成判定后输出日志：覆盖纯低科技 EdgeWalkIn、高科技空投成功、
            // 以及高科技整批回退 EdgeWalkIn 三种情况，便于排查抵达方式分歧。
            LogDeploymentDiagnostic(
                "OperationArrivalModeResolved",
                "actionId=" + (actionId ?? "-")
                + " | validParticipantCount=" + valid.Count
                + " | pendingGroupCount=" + pendingGroups.Count
                + " | totalPawnCount=" + allPendingPawns.Count
                + " | highestParticipantTechLevel=" + highestParticipantTechLevel
                + " | industrialArrivalThreshold=" + def.industrialArrivalThreshold
                + " | quickDropRequested=" + operationQuickDropRequested
                + " | safeCellsResolved=" + safeCellsResolved
                + " | preparedPodsResolved=" + preparedPodsResolved
                + " | plannedArrivalMode=" + plannedArrivalMode
                + " | actualArrivalMode=" + actualOperationArrivalMode
                + " | fallbackReason=" + fallbackReason
                + " | map=" + targetMap.GetUniqueLoadID());

            // 阶段3：统一部署。单个派系仅执行已确定的抵达方式。
            int generated = 0;
            if (actualOperationArrivalMode == SafeOutdoorExactDropModeName)
            {
                int cursor = 0;
                foreach (PendingJointOperationAidGroup group in pendingGroups)
                {
                    List<IntVec3> groupCells = new List<IntVec3>();
                    List<ActiveTransporterInfo> groupPods = new List<ActiveTransporterInfo>();
                    for (int k = 0; k < group.Pawns.Count; k++)
                    {
                        groupCells.Add(allDropCells[cursor]);
                        groupPods.Add(allPreparedPods[cursor]);
                        cursor++;
                    }

                    if (!DeploySafeDropGroup(group.Faction, group.Pawns, groupCells, groupPods, targetMap, group.AidTag, targetFaction))
                    {
                        Log.Warning(
                            JointOpLogPrefix + " Event=FactionAidDeployFailed"
                            + " | faction=" + group.Faction.Name
                            + " | arrivalMode=" + actualOperationArrivalMode
                            + " | aidTag=" + group.AidTag
                            + " | actionId=" + (actionId ?? "-"));
                        SafelyDiscardPawns(group.Pawns);
                        continue;
                    }

                    supportRecords.Add(new SymbiosisCovenantJointOperationFactionSupportRecord(
                        group.Faction, group.Points, group.Pawns.Count, group.AidTag));
                    spawnedAidTags.Add(group.AidTag);
                    generated++;
                    LogDeploymentDiagnostic(
                        "FactionAidDeployed",
                        "faction=" + group.Faction.Name
                        + " | aidTag=" + group.AidTag
                        + " | pawnCount=" + group.Pawns.Count
                        + " | arrivalMode=" + actualOperationArrivalMode);
                }
            }
            else
            {
                foreach (PendingJointOperationAidGroup group in pendingGroups)
                {
                    if (!DeployEdgeWalkInGroup(group.Faction, group.Pawns, group.Points, targetMap, def, targetFaction, group.AidTag))
                    {
                        Log.Warning(
                            JointOpLogPrefix + " Event=FactionAidDeployFailed"
                            + " | faction=" + group.Faction.Name
                            + " | arrivalMode=" + actualOperationArrivalMode
                            + " | aidTag=" + group.AidTag
                            + " | actionId=" + (actionId ?? "-"));
                        SafelyDiscardPawns(group.Pawns);
                        continue;
                    }

                    supportRecords.Add(new SymbiosisCovenantJointOperationFactionSupportRecord(
                        group.Faction, group.Points, group.Pawns.Count, group.AidTag));
                    spawnedAidTags.Add(group.AidTag);
                    generated++;
                    LogDeploymentDiagnostic(
                        "FactionAidDeployed",
                        "faction=" + group.Faction.Name
                        + " | aidTag=" + group.AidTag
                        + " | pawnCount=" + group.Pawns.Count
                        + " | arrivalMode=" + actualOperationArrivalMode);
                }
            }

            if (generated == 0)
            {
                Log.Warning(
                    JointOpLogPrefix + " Event=DeploymentFailed"
                    + " | reason=NoFactionAidGenerated"
                    + " | actionId=" + (actionId ?? "-")
                    + " | target=" + (targetWorldObject?.Label ?? "-")
                    + " | validParticipantCount=" + valid.Count
                    + " | totalSupportPoints=" + total.ToString("F1")
                    + " | tick=" + (Find.TickManager?.TicksGame ?? 0));
                return false;
            }

            LogDeploymentDiagnostic(
                "DeploymentSucceeded",
                "generatedFactionCount=" + generated
                + " | spawnedAidTagCount=" + (spawnedAidTags?.Count ?? 0)
                + " | supportRecordCount=" + (supportRecords?.Count ?? 0)
                + " | target=" + (targetWorldObject?.Label ?? "-")
                + " | tick=" + (Find.TickManager?.TicksGame ?? 0));

            if (spawnedAidTags != null && spawnedAidTags.Count > 0 && targetWorldObject != null)
            {
                Find.LetterStack.ReceiveLetter(
                    "MAP_MechanoidMechanitor.Symbiosis.JointOp.Reinforcements.Letter.Label"
                        .Translate(),
                    "MAP_MechanoidMechanitor.Symbiosis.JointOp.Reinforcements.Letter.Text"
                        .Translate(targetMap.Parent?.Label ?? "-"),
                    LetterDefOf.PositiveEvent,
                    new GlobalTargetInfo(targetWorldObject));
            }

            return true;
        }

        /// <summary>
        /// B：将总援军点数平均分配给“能生成有效战斗编组”的参与派系；
        /// 不能生成的派系份额为 0（其预算被其余可生成派系均分）；
        /// 最后一个可生成派系吸收浮点余量，保证总和 == total。
        /// </summary>
        private float[] AllocatePoints(List<Faction> participants, float total)
        {
            float[] assigned = new float[participants.Count];
            bool[] canGenerate = new bool[participants.Count];
            int generatable = 0;
            for (int i = 0; i < participants.Count; i++)
            {
                canGenerate[i] = CanGenerateCombatGroup(participants[i]);
                if (canGenerate[i])
                {
                    generatable++;
                }
            }

            if (generatable == 0)
            {
                return assigned; // 全为 0
            }

            float per = total / generatable;
            float used = 0f;
            int lastIndex = -1;
            for (int i = 0; i < participants.Count; i++)
            {
                if (!canGenerate[i])
                {
                    assigned[i] = 0f;
                    continue;
                }

                assigned[i] = per;
                used += per;
                lastIndex = i;
            }

            // 最后一派吸收浮点余量，保证总和 == total。
            if (lastIndex >= 0)
            {
                assigned[lastIndex] += total - used;
            }

            return assigned;
        }

        /// <summary>
        /// 使用原版 PawnGroupMaker 链路为某个参与派系生成真实战斗编组（D）。
        /// 不调用 RaidFriendly，不直接创建 Lord。
        /// </summary>
        private static List<Pawn>? GenerateCombatGroup(
            Faction faction,
            float points,
            Map map,
            SymbiosisCovenantJointOperationDef def)
        {
            IncidentParms parms = new IncidentParms
            {
                target = map,
                faction = faction,
                points = points,
                raidStrategy = RaidStrategyDefOf.ImmediateAttackFriendly,
                spawnCenter = map.Center
            };

            PawnGroupMakerParms makerParms = IncidentParmsUtility.GetDefaultPawnGroupMakerParms(
                PawnGroupKindDefOf.Combat, parms, ensureCanGenerateAtLeastOnePawn: true);
            if (!PawnGroupMakerUtility.TryGetRandomPawnGroupMaker(makerParms, out _))
            {
                return null;
            }

            List<Pawn> pawns = PawnGroupMakerUtility.GeneratePawns(makerParms, warnOnZeroResults: false)
                .ToList();
            if (pawns.Count == 0)
            {
                return null;
            }

            return pawns;
        }

        /// <summary>
        /// 判断一个格子能否作为联合行动援军“实际”空投格：必须满足全部安全条件，
        /// 不得位于室内、不得穿透屋顶、不得被建筑/Pawn/Skyfaller 占用，且能不破坏
        /// 墙体、不穿过关闭门地抵达地图边缘。
        /// 该方法只读，不修改 Pawn / 地图 / Quest 状态。
        /// </summary>
        private static bool IsSafeJointOperationDropCell(
            Map map,
            IntVec3 cell,
            Dictionary<Region, bool> regionCanReachEdgeCache)
        {
            if (map == null
                || regionCanReachEdgeCache == null
                || !cell.IsValid
                || !cell.InBounds(map))
            {
                return false;
            }

            // 廉价 / 局部判断优先，尽早短路，避免不必要的可达性搜索。
            if (!cell.Standable(map)
                || cell.Fogged(map)
                || cell.Roofed(map)
                || cell.GetEdifice(map) != null)
            {
                return false;
            }

            // 显式禁止穿透屋顶、禁止室内：排除没有屋顶但被墙完全封闭的结构。
            if (!DropCellFinder.IsGoodDropSpot(
                    cell,
                    map,
                    allowFogged: false,
                    canRoofPunch: false,
                    allowIndoors: false))
            {
                return false;
            }

            // 排除会与非本批空投舱 / 已落下的 Skyfaller / 现有 Pawn / 建筑冲突的 Thing。
            // 窄范围：只拦截 Pawn / Building / Skyfaller，不拦截草、普通物品等可自然覆盖的小物体。
            foreach (Thing thing in cell.GetThingList(map))
            {
                if (thing is Pawn || thing is Building || thing is Skyfaller)
                {
                    return false;
                }
            }

            // 连通性校验：不允许破墙、不允许穿过关闭门，必须用普通路径抵达地图边缘。
            // 同一 Region 在“一次搜索”内只执行一次 CanReachMapEdge，后续同区域格子直接复用缓存结果。
            Region? region = cell.GetRegion(map, RegionType.Set_Passable);
            if (region == null)
            {
                return false;
            }

            if (!regionCanReachEdgeCache.TryGetValue(region, out bool canReachEdge))
            {
                TraverseParms traverseParms =
                    TraverseParms.For(TraverseMode.NoPassClosedDoors);

                canReachEdge =
                    map.reachability.CanReachMapEdge(cell, traverseParms);

                regionCanReachEdgeCache[region] = canReachEdge;
            }

            return canReachEdge;
        }

        /// <summary>
        /// 在地图中心附近寻找一片能容纳 requiredCount 个援军的安全室外空投区。
        /// 只有找到“完整”的 requiredCount 个不重叠、保持最小间距的格子时才返回 true。
        /// 不返回不完整列表；找不到时清空 dropCells 并返回 false（由调用方回退 EdgeWalkIn）。
        /// </summary>
        private static bool TryFindSafeJointOperationDropCells(
            Map map,
            int requiredCount,
            out List<IntVec3> dropCells)
        {
            dropCells = new List<IntVec3>();

            if (map == null || requiredCount <= 0)
            {
                return false;
            }

            // 根据原版运行时提供的径向格表实际上限动态限制搜索半径，
            // 避免把超过 MaxRadialPatternRadius 的半径直接传给 GenRadial.RadialCellsAround。
            float maximumSupportedRadius =
                GenRadial.MaxRadialPatternRadius
                - QuickDropRadialPatternSafetyMargin;

            float effectiveSearchRadius =
                Math.Min(
                    QuickDropAnchorSearchRadius,
                    maximumSupportedRadius);

            if (effectiveSearchRadius <= 0f)
            {
                dropCells.Clear();

                Log.Warning(
                    JointOpLogPrefix
                    + " Event=SafeJointOperationDropZoneResolveFailed"
                    + " | reason=InvalidEffectiveSearchRadius"
                    + " | configuredSearchRadius="
                    + QuickDropAnchorSearchRadius.ToString("F2")
                    + " | maxRadialPatternRadius="
                    + GenRadial.MaxRadialPatternRadius.ToString("F2")
                    + " | effectiveSearchRadius="
                    + effectiveSearchRadius.ToString("F2")
                    + " | requiredCount=" + requiredCount
                    + " | map=" + map.GetUniqueLoadID());

                return false;
            }

            // 本次搜索专用缓存：同一 Region 只执行一次边缘可达性检查。
            // 缓存只存在于一次方法调用内，不跨地图、不跨部署、不写入存档。
            Dictionary<Region, bool> regionCanReachEdgeCache =
                new Dictionary<Region, bool>();

            int attemptedSafeAnchors = 0;

            // 候选锚点按从地图中心向外的顺序枚举（GenRadial 本身由近到远），不再扫描全图或排序。
            foreach (IntVec3 anchor in GenRadial.RadialCellsAround(
                         map.Center,
                         effectiveSearchRadius,
                         useCenter: true))
            {
                if (!anchor.InBounds(map))
                {
                    continue;
                }

                // 只有通过安全校验的锚点才计入 QuickDropAnchorAttemptLimit
                // （无效 / 屋顶 / 室内 / 建筑格不计入尝试上限）。
                if (!IsSafeJointOperationDropCell(
                        map,
                        anchor,
                        regionCanReachEdgeCache))
                {
                    continue;
                }

                attemptedSafeAnchors++;

                List<IntVec3> selected = new List<IntVec3>();

                // 在锚点周围枚举径向格子，再次逐一校验安全条件；不足则清空本次临时结果继续下一个锚点。
                // 内外两层共享同一个 regionCanReachEdgeCache，避免同一区域内重复可达性搜索。
                foreach (IntVec3 candidate in GenRadial.RadialCellsAround(
                             anchor,
                             QuickDropZoneRadius,
                             useCenter: true))
                {
                    if (!candidate.InBounds(map))
                    {
                        continue;
                    }

                    if (!IsSafeJointOperationDropCell(
                            map,
                            candidate,
                            regionCanReachEdgeCache))
                    {
                        continue;
                    }

                    bool tooClose = selected.Any(
                        existing => existing.DistanceToSquared(candidate)
                                   < QuickDropMinimumSpacingSquared);

                    if (tooClose)
                    {
                        continue;
                    }

                    selected.Add(candidate);

                    if (selected.Count >= requiredCount)
                    {
                        dropCells = selected;

                        LogDeploymentDiagnostic(
                            "SafeJointOperationDropZoneResolved",
                            "faction=-"
                            + " | requiredCount=" + requiredCount
                            + " | attemptedSafeAnchors=" + attemptedSafeAnchors
                            + " | cachedRegionCount=" + regionCanReachEdgeCache.Count
                            + " | success=true"
                            + " | dropCellCount=" + dropCells.Count
                            + " | configuredSearchRadius="
                            + QuickDropAnchorSearchRadius.ToString("F2")
                            + " | maxRadialPatternRadius="
                            + GenRadial.MaxRadialPatternRadius.ToString("F2")
                            + " | effectiveSearchRadius="
                            + effectiveSearchRadius.ToString("F2")
                            + " | map=" + map.GetUniqueLoadID());

                        return true;
                    }
                }

                // QuickDropAnchorAttemptLimit 限制“已验证安全并实际尝试建立空投区的锚点数量”。
                if (attemptedSafeAnchors >= QuickDropAnchorAttemptLimit)
                {
                    break;
                }
            }

            dropCells.Clear();

            LogDeploymentDiagnostic(
                "SafeJointOperationDropZoneResolved",
                "faction=-"
                + " | requiredCount=" + requiredCount
                + " | attemptedSafeAnchors=" + attemptedSafeAnchors
                + " | cachedRegionCount=" + regionCanReachEdgeCache.Count
                + " | success=false"
                + " | dropCellCount=0"
                + " | configuredSearchRadius="
                + QuickDropAnchorSearchRadius.ToString("F2")
                + " | maxRadialPatternRadius="
                + GenRadial.MaxRadialPatternRadius.ToString("F2")
                + " | effectiveSearchRadius="
                + effectiveSearchRadius.ToString("F2")
                + " | map=" + map.GetUniqueLoadID());

            return false;
        }

        /// <summary>
        /// 仅为全部 Pawn 准备空投容器（ActiveTransporterInfo），不生成任何空投舱、不修改地图。
        /// 只有“全部” Pawn 都成功放入各自容器时才返回 true；任一 TryAdd 失败时回滚此前已准备的容器，
        /// 使整支援军可以安全地回退 EdgeWalkIn（不破坏、不生成、不 Spawn Pawn）。
        /// </summary>
        private static bool TryPrepareExactDropPods(
            List<Pawn> pawns,
            out List<ActiveTransporterInfo> preparedPods)
        {
            preparedPods = new List<ActiveTransporterInfo>();

            if (pawns == null || pawns.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];

                if (pawn == null || pawn.Destroyed || pawn.Spawned)
                {
                    ReleasePreparedDropPods(pawns, preparedPods);
                    preparedPods.Clear();
                    return false;
                }

                ActiveTransporterInfo info = new ActiveTransporterInfo
                {
                    openDelay = QuickDropPodOpenDelay,
                    leaveSlag = true
                };

                if (!info.innerContainer.TryAdd(pawn))
                {
                    ReleasePreparedDropPods(pawns, preparedPods);
                    preparedPods.Clear();
                    return false;
                }

                preparedPods.Add(info);
            }

            return preparedPods.Count == pawns.Count;
        }

        /// <summary>
        /// 仅用于“尚未调用 MakeDropPodAt”的准备阶段回滚：把已进入临时容器的 Pawn 安全移出，
        /// 不 Destroy、不 Spawn、不加入 WorldPawns，使同一批 Pawn 可被 EdgeWalkIn 复用。
        /// </summary>
        private static void ReleasePreparedDropPods(
            List<Pawn> pawns,
            List<ActiveTransporterInfo> preparedPods)
        {
            if (pawns == null || preparedPods == null)
            {
                return;
            }

            int count = Math.Min(pawns.Count, preparedPods.Count);

            for (int i = 0; i < count; i++)
            {
                Pawn pawn = pawns[i];
                ActiveTransporterInfo info = preparedPods[i];

                if (pawn != null
                    && info?.innerContainer != null
                    && info.innerContainer.Contains(pawn))
                {
                    info.innerContainer.Remove(pawn);
                }
            }
        }

        /// <summary>
        /// 在“全部安全格”与“全部空投容器”已准备好的前提下，逐个生成空投舱到确定格子上。
        /// 本方法不再执行 TryAdd；全部容器和全部安全格已在修改地图前完成准备，
        /// 避免因落点不足或容器准备失败造成部分部署。
        /// 注意：MakeDropPodAt 等外部生成调用若发生异常仍可能留下部分 Skyfaller，
        /// 该异常由 DeployGroup 现有异常日志处理，本方法不作强行销毁回滚。
        /// </summary>
        private static void SpawnPreparedExactDropPods(
            Faction faction,
            List<ActiveTransporterInfo> preparedPods,
            List<IntVec3> dropCells,
            Map map)
        {
            if (faction == null
                || preparedPods == null
                || dropCells == null
                || preparedPods.Count == 0
                || preparedPods.Count != dropCells.Count)
            {
                throw new ArgumentException(
                    "Joint operation prepared drop pods must match validated drop cells.");
            }

            for (int i = 0; i < preparedPods.Count; i++)
            {
                DropPodUtility.MakeDropPodAt(
                    dropCells[i],
                    map,
                    preparedPods[i],
                    faction);
            }
        }

        /// <summary>
        /// 把生成的援军送入目标地图并创建本 MOD 自定义 Lord（D / C）。
        /// 低科技参与派系继续走边缘步行（EdgeWalkIn）；
        /// 高科技参与派系优先使用专用安全室外空投，找不到完整安全区或容器准备失败时整体回退为边缘步行。
        /// 实际采用的抵达方式通过 out actualArrivalMode 返回给调用方，供外层日志如实记录。
        /// 每只援军写入唯一 aidTag，并通过 QuestUtility.AddQuestTag 标记到 Lord，
        /// 之后只通过 LordJob 类型 + aidTag 精确追踪，不误伤其他援军。
        /// </summary>
        /// <summary>
        /// 安全室外空投部署：全部安全格与全部空投容器已在修改地图前完成准备，此处仅生成空投舱并创建 Lord。
        /// </summary>
        private static bool DeploySafeDropGroup(
            Faction faction,
            List<Pawn> pawns,
            List<IntVec3> dropCells,
            List<ActiveTransporterInfo> preparedPods,
            Map map,
            string aidTag,
            Faction? enemyFaction)
        {
            try
            {
                SpawnPreparedExactDropPods(faction, preparedPods, dropCells, map);
            }
            catch (Exception ex)
            {
                Log.Error(
                    JointOpLogPrefix + " Event=SafeDropSpawnException"
                    + " | faction=" + (faction?.Name ?? "-")
                    + " | aidTag=" + (aidTag ?? "-")
                    + " | map=" + map.GetUniqueLoadID()
                    + " | ex=" + ex);
                return false;
            }

            if (!TryCreateJointLord(faction, pawns, map, aidTag, enemyFaction))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 边缘步行部署：从 Invalid 由 Worker.TryResolveRaidSpawnCenter 解析地图边缘入口，再 Arrive。
        /// 同一行动级实际抵达方式已确定为 EdgeWalkIn，因此本方法只执行步行。
        /// </summary>
        private static bool DeployEdgeWalkInGroup(
            Faction faction,
            List<Pawn> pawns,
            float points,
            Map map,
            SymbiosisCovenantJointOperationDef def,
            Faction? targetFaction,
            string aidTag)
        {
            IncidentParms parms = new IncidentParms
            {
                target = map,
                faction = faction,
                points = points,
                raidStrategy = RaidStrategyDefOf.ImmediateAttackFriendly,
                spawnCenter = IntVec3.Invalid,
                raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn,
                raidArrivalModeForQuickMilitaryAid = false
            };

            if (!parms.raidArrivalMode.Worker.TryResolveRaidSpawnCenter(parms))
            {
                Log.Warning(
                    JointOpLogPrefix + " Event=DeployGroupFailed"
                    + " | reason=EdgeSpawnCenterResolveFailed"
                    + " | faction=" + (faction?.Name ?? "-")
                    + " | aidTag=" + (aidTag ?? "-")
                    + " | map=" + map.GetUniqueLoadID());
                return false;
            }

            try
            {
                parms.raidArrivalMode.Worker.Arrive(pawns, parms);
            }
            catch (Exception ex)
            {
                Log.Error(
                    JointOpLogPrefix + " Event=EdgeArriveException"
                    + " | faction=" + (faction?.Name ?? "-")
                    + " | aidTag=" + (aidTag ?? "-")
                    + " | map=" + map.GetUniqueLoadID()
                    + " | ex=" + ex);
                return false;
            }

            if (!TryCreateJointLord(faction, pawns, map, aidTag, targetFaction))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 为本 MOD 自定义援军 Lord 创建 Lord 并写入唯一 aidTag（与 DeployGroup 原有行为一致）。
        /// </summary>
        private static bool TryCreateJointLord(
            Faction faction,
            List<Pawn> pawns,
            Map map,
            string aidTag,
            Faction? enemyFaction)
        {
            LordJob_SymbiosisCovenantJointOperation job =
                new LordJob_SymbiosisCovenantJointOperation(faction, enemyFaction, map.Center);
            Lord? lord = LordMaker.MakeNewLord(faction, job, map, pawns);
            if (lord == null)
            {
                Log.Warning(
                    JointOpLogPrefix + " Event=DeployGroupFailed"
                    + " | reason=LordCreationReturnedNull"
                    + " | faction=" + (faction?.Name ?? "-")
                    + " | aidTag=" + (aidTag ?? "-"));
                return false;
            }

            foreach (Pawn p in pawns)
            {
                if (p != null && !p.Dead)
                {
                    Find.TickManager?.RegisterAllTickabilityFor(p);
                }
            }

            QuestUtility.AddQuestTag(lord, aidTag);

            LogDeploymentDiagnostic(
                "LordCreated",
                "faction=" + (faction?.Name ?? "-")
                + " | lord=" + lord.GetUniqueLoadID()
                + " | aidTag=" + (aidTag ?? "-")
                + " | ownedPawnCount=" + (lord.ownedPawns?.Count ?? 0));

            return true;
        }

        private static void SafelyDiscardPawns(List<Pawn> pawns)
        {
            if (pawns == null)
            {
                return;
            }

            foreach (Pawn p in pawns)
            {
                MechHiveCombatPawnUtility.SafelyDiscardPawn(p);
            }
        }

        /// <summary>
        /// 仅运行期使用的私有嵌套类：在一次部署调用内暂存“已生成但尚未部署”的各派系援军。
        /// 不保存为存档字段；行动级实际抵达方式在统一决定后才下发到每个派系。
        /// </summary>
        private sealed class PendingJointOperationAidGroup
        {
            public Faction Faction = null!;
            public float Points;
            public List<Pawn> Pawns = new List<Pawn>();
            public string AidTag = string.Empty;

            public PendingJointOperationAidGroup(
                Faction faction, float points, List<Pawn> pawns, string aidTag)
            {
                Faction = faction;
                Points = points;
                Pawns = pawns ?? new List<Pawn>();
                AidTag = aidTag;
            }
        }

        /// <summary>
        /// E：仅向本行动自己的援军 Lord 发出专用撤离 memo（不 Destroy / Kill 任何 Pawn）。
        /// </summary>
        private void CommandReinforcementsLeave()
        {
            if (spawnedAidTags == null || spawnedAidTags.Count == 0)
            {
                return;
            }

            // 不依赖 targetWorldObject.Map：据点被原版摧毁时，旧 Settlement 已被
            // DestroyedSettlement 替换，旧目标引用将不再能找到原地图。
            // 通过 aidTag 在当前所有地图中寻找本行动自己的 Lord，才能保证结算后仍会撤离。
            foreach (Map map in Find.Maps)
            {
                foreach (string tag in spawnedAidTags)
                {
                    foreach (Lord lord in FindTaggedJointOpLords(map, tag))
                    {
                        LordJob_SymbiosisCovenantJointOperation.SendLeave(lord);
                    }
                }
            }
        }

        private static List<Lord> FindTaggedJointOpLords(Map map, string? tag)
        {
            if (string.IsNullOrEmpty(tag) || map?.lordManager == null)
            {
                return new List<Lord>();
            }

            return map.lordManager.lords
                .Where(lord =>
                    lord != null
                    && lord.LordJob is LordJob_SymbiosisCovenantJointOperation
                    && lord.questTags != null
                    && lord.questTags.Contains(tag))
                .ToList();
        }

        /// <summary>
        /// DEV 用：统计某个 aidTag 对应的本行动援军 Lord 数量（供状态快照展示）。
        /// </summary>
        public static int CountTaggedJointOpLordsPublic(Map map, string? tag)
        {
            return FindTaggedJointOpLords(map, tag).Count;
        }

        private void BeginSuccess(string reason = "TargetCleared", bool completionBeforeAcceptance = false)
        {
            if (successApplied || failureApplied || invalidEndApplied)
            {
                return;
            }

            SymbiosisCovenantJointOperationStage stageBeforeSuccess = stage;
            bool reinforcementsGeneratedSnapshot = reinforcementsGenerated;
            bool playerEngagedSnapshot = playerEngaged;
            bool targetThreatConfirmedSnapshot = targetThreatConfirmed;
            // 在 stage 改为 Succeeded 之前保存战斗就绪快照，否则后续读取会固定为 false。
            bool hasReachedCombatReadySnapshot = HasReachedCombatReadyState;

            stage = SymbiosisCovenantJointOperationStage.Succeeded;
            CommandReinforcementsLeave();
            ApplySuccessOutcome();

            LogDeploymentDiagnostic(
                "OperationSucceeded",
                "actionId=" + (actionId ?? "-")
                + " | questId=" + (quest?.id.ToString() ?? "-")
                + " | target=" + (targetWorldObject?.Label ?? "-")
                + " | reason=" + reason
                + " | completionBeforeAcceptance=" + completionBeforeAcceptance
                + " | stageBeforeSuccess=" + stageBeforeSuccess
                + " | reinforcementsGenerated=" + reinforcementsGeneratedSnapshot
                + " | playerEngaged=" + playerEngagedSnapshot
                + " | targetThreatConfirmed=" + targetThreatConfirmedSnapshot
                + " | spawnedAidTagCount=" + (spawnedAidTags?.Count ?? 0)
                + " | hasReachedCombatReady=" + hasReachedCombatReadySnapshot
                + " | tick=" + (Find.TickManager?.TicksGame ?? 0));

            quest?.End(QuestEndOutcome.Success);
        }

        private void BeginFailure(string reason)
        {
            if (successApplied || failureApplied || invalidEndApplied)
            {
                return;
            }

            stage = SymbiosisCovenantJointOperationStage.Failed;
            CommandReinforcementsLeave();
            ApplyFailOutcome();
            quest?.End(QuestEndOutcome.Fail);
        }

        private void BeginInvalidEnd(string reason)
        {
            if (successApplied || failureApplied || invalidEndApplied)
            {
                return;
            }

            SymbiosisCovenantJointOperationStage stageBeforeEnd = stage;
            stage = SymbiosisCovenantJointOperationStage.InvalidEnded;
            ClearDeploymentWaitReason();
            CommandReinforcementsLeave();
            ApplyInvalidOutcome();

            Log.Warning(
                JointOpLogPrefix + " Event=OperationInvalidEnded"
                + " | reason=" + reason
                + " | actionId=" + (actionId ?? "-")
                + " | stageBeforeEnd=" + stageBeforeEnd
                + " | target=" + (targetWorldObject?.Label ?? "-")
                + " | reinforcementsGenerated=" + reinforcementsGenerated
                + " | playerEngaged=" + playerEngaged
                + " | targetThreatConfirmed=" + targetThreatConfirmed
                + " | targetThreatPointsAtDeployment=" + targetThreatPointsAtDeployment
                + " | totalSupportPointsAtDeployment="
                + totalSupportPointsAtDeployment.ToString("F1")
                + " | spawnedAidTagCount=" + (spawnedAidTags?.Count ?? 0)
                + " | tick=" + (Find.TickManager?.TicksGame ?? 0));

            quest?.End(QuestEndOutcome.Unknown);
        }

        /// <summary>
        /// 外部目标清除的统一入口：Settlement、MAPFactionOutpost 及其它精确目标通知都走这里。
        /// 严格校验引用相等与尚未结束，再根据 OfferPending / 已接取 / 已结束分支决定行为。
        /// </summary>
        public void NotifyTargetClearedExternally(WorldObject clearedTarget, string reason)
        {
            if (clearedTarget == null || targetWorldObject == null || quest == null)
            {
                return;
            }

            if (!ReferenceEquals(targetWorldObject, clearedTarget))
            {
                return;
            }

            if (successApplied || failureApplied || invalidEndApplied)
            {
                return;
            }

            TryCompleteOfferPendingTarget(clearedTarget, reason);
        }

        /// <summary>
        /// 据点（Settlement）被原版 SettlementDefeatUtility 真正摧毁时由窄范围 Patch 调用。
        /// 直接复用外部目标清除结算路径：内部会再次核对引用相等、是否已结束、
        /// 目标是否真实清除（Settlement 取 settlement.Destroyed），以及是否已接取且战斗就绪；
        /// 满足全部条件才会 BeginSuccess，避免误结算。
        /// </summary>
        public void NotifySettlementDestroyed(Settlement settlement)
        {
            if (settlement == null)
            {
                return;
            }

            NotifyTargetClearedExternally(settlement, "SettlementDestroyedPatch");
        }

        /// <summary>
        /// 统一成功判定：未接取的成功不要求战斗就绪、不生成援军、直接 Success；
        /// 已接取的成功必须战斗就绪；已结束阶段直接返回，不重复奖励。
        /// 不能仅凭信号字符串成功，必须再次核对真实目标状态。
        /// </summary>
        private bool TryCompleteOfferPendingTarget(WorldObject? candidate, string reason)
        {
            if (candidate == null || targetWorldObject == null || quest == null)
            {
                return false;
            }

            if (!ReferenceEquals(targetWorldObject, candidate))
            {
                return false;
            }

            if (successApplied || failureApplied || invalidEndApplied)
            {
                return false;
            }

            if (!IsActive)
            {
                return false;
            }

            if (!IsTargetWorldObjectTrulyCleared(candidate))
            {
                return false;
            }

            if (stage == SymbiosisCovenantJointOperationStage.OfferPending)
            {
                BeginSuccess(reason: reason, completionBeforeAcceptance: true);
                return true;
            }

            if (IsOperationAccepted)
            {
                if (!HasReachedCombatReadyState)
                {
                    // 已接取但尚未战斗就绪：不要因为外部通知提前成功。
                    return false;
                }

                BeginSuccess(reason: reason, completionBeforeAcceptance: false);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 严格核对目标是否真实清除（按类型区分，避免误结算）。
        /// 判定顺序：MAPMechHiveNode → MAPFactionOutpost → Settlement → 普通 Site。
        /// 本方法是纯谓词，不在此暗中修改节点状态；
        /// 节点进入 Cleaned 由联合行动专用清除验证入口完成。
        /// </summary>
        private bool IsTargetWorldObjectTrulyCleared(WorldObject target)
        {
            if (target == null || !ReferenceEquals(targetWorldObject, target))
            {
                return false;
            }

            if (target is MAPMechHiveNode node)
            {
                return node.Cleaned;
            }

            if (target is MAPFactionOutpost outpost)
            {
                return outpost.Cleaned;
            }

            if (target is Settlement settlement)
            {
                return settlement.Destroyed;
            }

            if (target is Site site)
            {
                if (site.Destroyed)
                {
                    return true;
                }

                // 普通 Site：禁止“地图不存在 / 找不到站立守军就默认清除”的宽松规则。
                MapParent? siteParent = target as MapParent;
                Map? map = siteParent?.Map;
                if (map == null || map.Disposed || !Find.Maps.Contains(map))
                {
                    return false;
                }

                // 必须存在“目标原本确实有威胁”的证据，否则可能是守军尚未初始化。
                // OfferPending 期间已通过 ObserveOfferPendingTargetMap 记录；已接取路径依赖
                // targetThreatConfirmed / 部署阶段威胁观察。此处兜底：站场守军或实际威胁点数。
                bool hadThreatEvidence =
                    offerPendingTargetThreatObserved
                    || site.ActualThreatPoints > 0
                    || AnyStandingTargetFactionDefender(map);
                if (!hadThreatEvidence)
                {
                    return false;
                }

                // 仍有原版活动敌对威胁（含休眠/可雾化）则不算清除。
                if (GenHostility.AnyHostileActiveThreatToPlayer(
                        map,
                        countDormantPawnsAsHostile: true,
                        canBeFogged: true))
                {
                    return false;
                }

                return true;
            }

            // 其他 WorldObject 默认不成功，除非已有明确、安全的真实清除规则。
            return false;
        }

        /// <summary>
        /// DEV 专用：立即清除当前活动联合军事行动（按无效结束，不加不减 Unity/Trust）。
        /// 正式游戏路径不得调用。
        /// </summary>
        public void DevEndOperation()
        {
            BeginInvalidEnd("devClear");
        }

        /// <summary>
        /// 任务清理（无论何种原因结束都会调用）。这是未显式结算时的兜底（G7）：
        /// 邀请被拒绝/过期 → 5 天冷却，无奖惩；
        /// 已接取但未由本系统显式结算（非 timeout 失败）→ 默认无效结束（不加不减 Unity/Trust），
        /// 绝不因外部中断/未知原因误扣惩罚。
        /// </summary>
        public override void Notify_PreCleanup()
        {
            base.Notify_PreCleanup();
            if (successApplied || failureApplied || invalidEndApplied)
            {
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            SymbiosisCovenantJointOperationDef? def = ResolveDef();
            if (stage == SymbiosisCovenantJointOperationStage.OfferPending)
            {
                declinedOrExpiredApplied = true;
                if (def != null)
                {
                    SymbiosisCovenantJointOperationScheduler
                        .SetCooldownEndTick(now + def.declinedOrExpiredCooldownTicks);
                }
            }
            else
            {
                // 此处已在 Quest.End → CleanupQuestParts 调用链内，绝不能再次调用 quest.End，
                // 否则会重入清理并造成重复 Cleanup / 重复结束信件。
                stage = SymbiosisCovenantJointOperationStage.InvalidEnded;
                CommandReinforcementsLeave();
                ApplyInvalidOutcome();
            }
        }

        private void ApplySuccessOutcome()
        {
            successApplied = true;
            SymbiosisCovenantJointOperationDef? def = ResolveDef();
            if (def == null)
            {
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            SymbiosisCovenantJointOperationScheduler
                .SetCooldownEndTick(now + def.completedOrFailedCooldownTicks);
            GameComponent_SymbiosisCovenantState.TryAdjustUnity(
                def.successUnityDelta,
                "MAP_MechanoidMechanitor.Symbiosis.TrustReason.JointOperation");
            foreach (Faction faction in ValidParticipantsNow())
            {
                GameComponent_SymbiosisCovenantState.TryAdjustTrust(
                    faction,
                    def.successTrustDeltaPerValidParticipant,
                    "MAP_MechanoidMechanitor.Symbiosis.TrustReason.JointOperation",
                    SymbiosisCovenantTrustSource.Quest);
            }
        }

        private void ApplyFailOutcome()
        {
            failureApplied = true;
            SymbiosisCovenantJointOperationDef? def = ResolveDef();
            if (def == null)
            {
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            SymbiosisCovenantJointOperationScheduler
                .SetCooldownEndTick(now + def.completedOrFailedCooldownTicks);
            GameComponent_SymbiosisCovenantState.TryAdjustUnity(
                def.failUnityDelta,
                "MAP_MechanoidMechanitor.Symbiosis.TrustReason.JointOperation");
            foreach (Faction faction in ValidParticipantsNow())
            {
                GameComponent_SymbiosisCovenantState.TryAdjustTrust(
                    faction,
                    def.failTrustDeltaPerValidParticipant,
                    "MAP_MechanoidMechanitor.Symbiosis.TrustReason.JointOperation",
                    SymbiosisCovenantTrustSource.Quest);
            }
        }

        private void ApplyInvalidOutcome()
        {
            invalidEndApplied = true;
            SymbiosisCovenantJointOperationDef? def = ResolveDef();
            if (def == null)
            {
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            SymbiosisCovenantJointOperationScheduler
                .SetCooldownEndTick(now + def.invalidEndCooldownTicks);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref actionId, "actionId");
            Scribe_Values.Look(ref targetQuestTag, "targetQuestTag");
            Scribe_Values.Look(ref stage, "stage", SymbiosisCovenantJointOperationStage.OfferPending);
            Scribe_References.Look(ref targetWorldObject, "targetWorldObject");
            Scribe_References.Look(ref targetFaction, "targetFaction");
            Scribe_References.Look(ref proposerFaction, "proposerFaction");
            Scribe_Collections.Look(ref participantFactions, "participantFactions", LookMode.Reference);
            Scribe_Values.Look(ref offerExpireTick, "offerExpireTick", 0);
            Scribe_Values.Look(ref operationExpireTick, "operationExpireTick", 0);
            Scribe_Values.Look(ref targetMapWasGeneratedByThisOperation, "targetMapWasGeneratedByThisOperation", false);
            Scribe_Values.Look(ref reinforcementsGenerated, "reinforcementsGenerated", false);
            Scribe_Values.Look(ref deploymentDueTick, "deploymentDueTick", -1);
            Scribe_Values.Look(ref threatInitializationDeadlineTick, "threatInitializationDeadlineTick", -1);
            Scribe_Values.Look(ref targetThreatConfirmed, "targetThreatConfirmed", false);
            Scribe_Values.Look(ref successApplied, "successApplied", false);
            Scribe_Values.Look(ref failureApplied, "failureApplied", false);
            Scribe_Values.Look(ref invalidEndApplied, "invalidEndApplied", false);
            Scribe_Values.Look(ref declinedOrExpiredApplied, "declinedOrExpiredApplied", false);
            Scribe_Values.Look(ref targetThreatPointsAtDeployment, "targetThreatPointsAtDeployment", 0);
            Scribe_Values.Look(ref totalSupportPointsAtDeployment, "totalSupportPointsAtDeployment", 0f);
            Scribe_Collections.Look(
                ref supportRecords,
                "supportRecords",
                LookMode.Deep);
            Scribe_Collections.Look(ref spawnedAidTags, "spawnedAidTags", LookMode.Value);
            Scribe_Defs.Look(ref jointOperationDef, "jointOperationDef");
            Scribe_Values.Look(ref playerEngaged, "playerEngaged", false);
            Scribe_Values.Look(ref covenantLevelSnapshot, "covenantLevelSnapshot", 0);
            Scribe_Values.Look(
                ref supportPointsFactorSnapshot,
                "supportPointsFactorSnapshot",
                -1f);

            // OfferPending 普通 Site 延迟验证状态（参与存档，保证旧存档读档后能继续验证）。
            Scribe_Values.Look(ref offerPendingTargetMapFirstObservedTick, "offerPendingTargetMapFirstObservedTick", -1);
            Scribe_Values.Look(ref offerPendingClearVerificationDueTick, "offerPendingClearVerificationDueTick", -1);
            Scribe_Values.Look(ref offerPendingTargetThreatObserved, "offerPendingTargetThreatObserved", false);
            Scribe_Values.Look(ref offerPendingClearVerificationReason, "offerPendingClearVerificationReason");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                participantFactions ??= new List<Faction>();
                participantFactions.RemoveAll(f => f == null);
                supportRecords ??= new List<SymbiosisCovenantJointOperationFactionSupportRecord>();
                spawnedAidTags ??= new List<string>();
                if (stage == SymbiosisCovenantJointOperationStage.TargetMapEntered
                    && !targetThreatConfirmed
                    && threatInitializationDeadlineTick < 0)
                {
                    // 缺失的截止 tick 是未初始化标记，不是已经过期。
                    // 只修复缺失值，保留正常存档已经排定或已经到期的窗口。
                    int now = Find.TickManager?.TicksGame ?? 0;
                    threatInitializationDeadlineTick =
                        Math.Max(now, deploymentDueTick) + ThreatInitializationDeadlineTicks;
                }

                // 兼容旧存档：确保本 Part 在 OfferPending 阶段也能监听目标完成信号。
                signalListenMode = QuestPart.SignalListenMode.OngoingOrNotYetAccepted;

                // 旧存档兼容：更新前接取的行动没有快照。更新前只有 L4 及以上才可能接到
                // 联合行动，其历史承诺就是旧版固定 50%，必须显式补成 0.50，
                // 绝不能按读档时的当前等级重新计算（否则可能从 50% 变成 75%）。
                // 尚未接受的 OfferPending 保持 -1，留给 PreQuestAccept 按新规则建立快照。
                if (supportPointsFactorSnapshot < 0f && IsOperationAccepted)
                {
                    supportPointsFactorSnapshot =
                        SymbiosisCovenantJointOperationDef.LegacySupportPointsFactor;
                }
            }
        }

        // ===== 静态入口：供调度器在盟约跌破解锁等级时取消进行中的行动（G6） =====

        public static void NotifyCovenantLevelChanged(int level)
        {
            // L2~L5 之间任意变化都不取消已接取的行动，只有跌到 L1 及以下才算等级不足。
            if (level >= SymbiosisCovenantJointOperationDef.MinimumCovenantLevel)
            {
                return;
            }

            QuestPart_SymbiosisCovenantJointOperation? part =
                SymbiosisCovenantJointOperationUtility.FindActiveOperationPart();
            if (part != null && part.IsActive)
            {
                part.BeginInvalidEnd("covenantLevelTooLow");
            }
        }

        // ===== 内部辅助 =====

        /// <summary>
        /// 正常流程的详细诊断日志：仅在开发者模式下输出，统一前缀 [MAP-JointOperation]。
        /// </summary>
        private static void LogDeploymentDiagnostic(string eventName, string details)
        {
            if (!Prefs.DevMode)
            {
                return;
            }

            Log.Message(JointOpLogPrefix + " Event=" + eventName + " | " + details);
        }

        /// <summary>
        /// 等待类日志：同一种 eventName（即同一种等待原因）连续出现只记录一次，
        /// 原因改变后才允许再次输出，避免每 tick 刷屏。仅在开发者模式下输出。
        /// </summary>
        private void LogDeploymentWaitOnce(string eventName, string details)
        {
            if (!Prefs.DevMode)
            {
                return;
            }

            if (lastDeploymentWaitReason == eventName)
            {
                return;
            }

            lastDeploymentWaitReason = eventName;
            Log.Message(JointOpLogPrefix + " Event=" + eventName + " | " + details);
        }

        private void ClearDeploymentWaitReason()
        {
            lastDeploymentWaitReason = null;
        }

        /// <summary>
        /// 诊断（只读）：接取后下一 tick 检查 Part 是否被正确启用。不修复状态、
        /// 不调用任何启用方法；若 State 仍不是 Enabled，仅额外输出一条 Warning。
        /// </summary>
        private void LogPostAcceptActivationCheck(int now)
        {
            MapParent? mapParent = targetWorldObject as MapParent;
            bool targetHasMap = mapParent != null && mapParent.HasMap;
            string inSignal = inSignalEnable ?? "-";
            string initiateSignal = quest?.InitiateSignal ?? "-";
            bool enableMatches = string.Equals(
                inSignal, initiateSignal, StringComparison.Ordinal);
            string tags = SymbiosisCovenantJointOperationDiagnostics.FormatQuestTags(mapParent?.questTags);
            bool tagPresent = SymbiosisCovenantJointOperationDiagnostics.QuestTagContains(
                mapParent?.questTags, targetQuestTag);

            SymbiosisCovenantJointOperationDiagnostics.Log(
                "PostAcceptActivationCheck",
                "actionId=" + (actionId ?? "-")
                + " | questId=" + (quest != null ? quest.id.ToString() : "-")
                + " | currentTick=" + now
                + " | stage=" + stage
                + " | state=" + State
                + " | inSignalEnable=" + inSignal
                + " | questInitiateSignal=" + initiateSignal
                + " | enableSignalMatchesInitiateSignal=" + enableMatches
                + " | stateIsEnabled=" + (State == QuestPartState.Enabled)
                + " | target=" + (targetWorldObject?.Label ?? "-")
                + " | targetQuestTag=" + (targetQuestTag ?? "-")
                + " | targetHasMap=" + targetHasMap
                + " | questTags=" + tags
                + " | targetTagPresent=" + tagPresent);

            if (State != QuestPartState.Enabled)
            {
                // 仅警告，不结束任务、不修复状态、不调用任何启用方法。
                SymbiosisCovenantJointOperationDiagnostics.LogWarning(
                    "QuestPartNotEnabledAfterAccept",
                    "actionId=" + (actionId ?? "-")
                    + " | questId=" + (quest != null ? quest.id.ToString() : "-")
                    + " | state=" + State
                    + " | stage=" + stage
                    + " | inSignalEnable=" + inSignal
                    + " | questInitiateSignal=" + initiateSignal
                    + " | targetQuestTag=" + (targetQuestTag ?? "-"));
            }
        }

        /// <summary>
        /// 诊断（只读）：OperationActive 下目标地图已存在却尚未通过正常信号路径登记为
        /// TargetMapEntered。只观察、只记录，绝不修复：不调用 NotifyTargetMapGenerated、
        /// 不改 stage、不部署援军、不完成任务。
        /// </summary>
        private void LogOperationActiveTargetMapObserved(int now, MapParent mapParent, Map? map)
        {
            string tags = SymbiosisCovenantJointOperationDiagnostics.FormatQuestTags(mapParent.questTags);
            bool tagPresent = SymbiosisCovenantJointOperationDiagnostics.QuestTagContains(
                mapParent.questTags, targetQuestTag);
            string mapInfo = SymbiosisCovenantJointOperationDiagnostics.DescribeMapParent(mapParent);
            string pawnInfo = SymbiosisCovenantJointOperationDiagnostics.DescribePlayerPawnCounts(map);

            SymbiosisCovenantJointOperationDiagnostics.Log(
                "OperationActiveTargetMapObserved",
                "actionId=" + (actionId ?? "-")
                + " | questId=" + (quest != null ? quest.id.ToString() : "-")
                + " | currentTick=" + now
                + " | stage=" + stage
                + " | state=" + State
                + " | target=" + (targetWorldObject?.Label ?? "-")
                + " | targetLoadId=" + (targetWorldObject?.GetUniqueLoadID() ?? "-")
                + " | targetQuestTag=" + (targetQuestTag ?? "-")
                + " | " + mapInfo
                + " | questTags=" + tags
                + " | targetTagPresent=" + tagPresent
                + " | targetMapWasGeneratedByThisOperation=" + targetMapWasGeneratedByThisOperation
                + " | deploymentDueTick=" + deploymentDueTick
                + " | threatInitializationDeadlineTick=" + threatInitializationDeadlineTick
                + " | playerEngaged=" + playerEngaged
                + " | targetThreatConfirmed=" + targetThreatConfirmed
                + " | reinforcementsGenerated=" + reinforcementsGenerated
                + " | " + pawnInfo);

            // 代表「目标地图已经存在，但联合行动没有通过正常信号路径登记为 TargetMapEntered」。
            // 仅警告，不修复。
            SymbiosisCovenantJointOperationDiagnostics.LogWarning(
                "TargetMapExistsButOperationStillActive",
                "actionId=" + (actionId ?? "-")
                + " | questId=" + (quest != null ? quest.id.ToString() : "-")
                + " | stage=" + stage
                + " | state=" + State
                + " | target=" + (targetWorldObject?.Label ?? "-")
                + " | targetQuestTag=" + (targetQuestTag ?? "-")
                + " | targetTagPresent=" + tagPresent
                + " | currentTick=" + now);
        }

        private SymbiosisCovenantJointOperationDef? ResolveDef()
        {
            return jointOperationDef
                ?? SymbiosisCovenantJointOperationDefOf.MAP_SymbiosisCovenant_JointOperationConfig;
        }

        private List<Faction> ValidParticipantsNow()
        {
            List<Faction> result = new List<Faction>();
            if (participantFactions == null)
            {
                return result;
            }

            foreach (Faction faction in participantFactions)
            {
                if (IsParticipantStillValid(faction))
                {
                    result.Add(faction);
                }
            }

            return result;
        }

        private bool AnyValidParticipantRemaining()
        {
            return ValidParticipantsNow().Count > 0;
        }

        private bool IsParticipantStillValid(Faction? faction)
        {
            return TryGetParticipantValidity(faction, out _);
        }

        /// <summary>
        /// 提取参与派系合法性判断，供 IsParticipantStillValid 复用，也供诊断日志逐项输出原因。
        /// 不新增任何合法性条件，必须与原逻辑逐项一致。
        /// </summary>
        private bool TryGetParticipantValidity(Faction? faction, out string reason)
        {
            reason = string.Empty;

            if (faction == null)
            {
                reason = "FactionNull";
                return false;
            }

            if (faction.defeated || faction.deactivated || faction.Hidden || faction.temporary)
            {
                reason = "DefeatedOrDeactivatedOrHiddenOrTemporary";
                return false;
            }

            if (Faction.OfPlayer != null && faction.HostileTo(Faction.OfPlayer))
            {
                reason = "HostileToPlayer";
                return false;
            }

            GameComponent_SymbiosisCovenantState? state =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (state == null || state.GetRecord(faction)?.CovenantMember != true)
            {
                reason = "NotCovenantMember";
                return false;
            }

            // 参与派系必须对本次行动的目标派系保持敌对。
            if (targetFaction == null || !faction.HostileTo(targetFaction))
            {
                reason = "NotHostileToTargetFaction";
                return false;
            }

            return true;
        }

        private static bool CanGenerateCombatGroup(Faction faction)
        {
            if (faction.def?.raidsForbidden == true)
            {
                return false;
            }

            // 仅做能力判定，不真正生成；使用安全的最小参数（与共同防卫判定一致）。
            PawnGroupMakerParms makerParms = new PawnGroupMakerParms
            {
                faction = faction,
                groupKind = PawnGroupKindDefOf.Combat,
                points = 1000f
            };
            return PawnGroupMakerUtility.TryGetRandomPawnGroupMaker(makerParms, out _);
        }

        private static string MakeAidTag(Map map, Faction responder)
        {
            int tick = Find.TickManager?.TicksGame ?? 0;
            return AidQuestTagPrefix
                + "_"
                + map.GetUniqueLoadID()
                + "_"
                + tick
                + "_"
                + responder.GetUniqueLoadID();
        }
    }
}
