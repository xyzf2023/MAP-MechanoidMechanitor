using System;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 解除合体的唯一幂等入口。手动解除、能源归零、稳定值归零、
    /// 目标死亡或倒地、服装异常、读档不一致都必须调用这里，
    /// 不允许复制多套清理代码或直接 Destroy 服装代替解除逻辑。
    /// 清理分阶段推进，每阶段完成即落盘标记，延迟恢复与读档后继续时
    /// 绝不重复结算源机械族部位耐久、能源写回、白名单撤销或服装清理。
    /// </summary>
    public static class MechFusionTeardownService
    {
        private const int RestoreSearchRadius = 8;
        private const int MaxPendingRecoveryAttempts = 600;

        public static bool TryTeardown(
            MechFusionSession? session,
            MechFusionExitReason reason,
            bool force)
        {
            if (session == null || session.TeardownCompleted)
            {
                return false;
            }

            if (!session.IsEnding
                && reason == MechFusionExitReason.Manual
                && MechanicalFlightUtility.IsAirborne(session.WearerPawn))
            {
                session.SetExitReason(reason);
                Log.Warning(
                    "[MAP-机械族机械师] 飞行中禁止手动解除合体，未执行任何清理：" +
                    $"session={session.SessionId}。");
                return false;
            }

            session.SetExitReason(reason);

            if (session.IsPendingRecovery)
            {
                return TryRecoverPendingSession(session);
            }

            if (session.IsEnding && !session.TeardownDeferred && !force)
            {
                return false;
            }

            session.SetState(MechFusionSessionState.Ending);
            session.TeardownDeferred = false;
            return ExecuteTeardown(session);
        }

        internal static void TryResumeDeferredTeardown(MechFusionSession session)
        {
            if (session == null || !session.IsEnding || !session.TeardownDeferred)
            {
                return;
            }

            session.TeardownDeferred = false;
            ExecuteTeardown(session);
        }

        internal static bool TryRecoverPendingSession(MechFusionSession session)
        {
            if (session == null || !session.IsPendingRecovery)
            {
                return false;
            }

            return ExecuteTeardown(session);
        }

        internal static void NotifyShellUnequipped(
            CompMechFusionShell? shell,
            Pawn? pawn)
        {
            NotifyShellLost(shell);
        }

        internal static void NotifyShellDestroyed(
            CompMechFusionShell? shell,
            Map? previousMap)
        {
            NotifyShellLost(shell);
        }

        private static void NotifyShellLost(CompMechFusionShell? shell)
        {
            if (shell == null
                || string.IsNullOrEmpty(shell.SessionId)
                || !GameComponent_MechFusionSessionRegistry.TryGetSessionById(
                    shell.SessionId,
                    out MechFusionSession? session)
                || session == null
                || !session.IsActive
                || !ReferenceEquals(session.FusionApparel, shell.parent))
            {
                return;
            }

            TryTeardown(
                session,
                MechFusionExitReason.ApparelLost,
                force: false);
        }

        private static bool ExecuteTeardown(MechFusionSession session)
        {
            try
            {
                return ExecuteTeardownInternal(session);
            }
            catch (Exception ex)
            {
                // 任何未预期异常都保留会话并标记延迟重试，绝不静默吞掉真实 Pawn。
                session.TeardownDeferred = true;
                Log.Error(
                    "[MAP-机械族机械师] 解除合体事务发生异常，已保留会话等待重试：" +
                    $"session={session.SessionId}：{ex}");
                return false;
            }
        }

        private static bool ExecuteTeardownInternal(MechFusionSession session)
        {
            Pawn? source = session.SourcePawn;
            Pawn? wearer = session.WearerPawn;

            if (source == null)
            {
                return HandleMissingSourceReference(session, wearer);
            }

            if (IsSourceTrulyUnrecoverable(source))
            {
                CleanupUnrecoverableSource(session, source, wearer);
                return false;
            }

            if (!HandleAirborneExit(session, wearer))
            {
                return false;
            }

            if (!RunWearerCleanupSteps(session, wearer))
            {
                return false;
            }

            // 源机械体必须先离开合体期间的 WorldPawns 临时仓库，进入地图、
            // 远行队或合法尸体容器。只有这样后续结构耐久结算即使致死，
            // 原版也能产生正常尸体，而不是把后台 WorldPawn 直接销毁。
            // 旧存档可能错误地写有 SourceRestored=true，因此同时校验实际容器。
            if (!session.SourceRestored || !IsSourceInRestoredContainer(source))
            {
                if (!TryRestoreSourcePawn(session, source, out bool deferred))
                {
                    if (IsSourceTrulyUnrecoverable(source))
                    {
                        CleanupUnrecoverableSource(session, source, wearer);
                        return false;
                    }

                    session.SetState(MechFusionSessionState.PendingRecovery);
                    session.TeardownDeferred = false;
                    if (deferred)
                    {
                        session.UpdateRecoveryLocation(
                            session.PendingMap,
                            session.PendingPosition,
                            session.PendingRotation);
                        Log.Warning(
                            "[MAP-机械族机械师] 合体解除时找不到安全容器，" +
                            "源 Pawn 已安全保留并等待恢复：" +
                            $"pawn={source.LabelShort}（{source.ThingID}）。");
                    }
                    else
                    {
                        Log.Error(
                            "[MAP-机械族机械师] 合体解除时源 Pawn 无法恢复合法容器，" +
                            "已保留会话等待重试：" +
                            $"pawn={source.LabelShort}（{source.ThingID}）。");
                    }

                    return false;
                }

                if (!session.SourceRestored)
                {
                    session.MarkSourceRestored();
                }

                session.ResetPendingRecoveryAttempts();
            }

            // 先恢复形态，再提交权威能源。这样即使后续结构结算致死，
            // 尸体中的真实 Pawn 也已经脱离 Merged 形态并完成最终能源同步。
            if (!session.TransformationRestored)
            {
                if (!TryRestoreTransformationRecord(session, source))
                {
                    session.SetState(MechFusionSessionState.Ending);
                    session.TeardownDeferred = true;
                    Log.Error(
                        "[MAP-机械族机械师] 合体解除后未能恢复形态记录，" +
                        "已保留会话继续尝试修复，不视为完全结束：" +
                        $"pawn={source.LabelShort}（{source.ThingID}）。");
                    return false;
                }

                session.MarkTransformationRestored();
            }

            if (!TryCommitRestoredSourceEnergy(session, source))
            {
                return false;
            }

            // DormantGuard 只服务于合体期间的 WorldPawns 暂存状态；源 Pawn 已经
            // 恢复到死亡安全容器后再移除，避免等待恢复期间 Hediff 计时被冻结。
            MechFusionSourceUtility.RemoveDormantGuard(source);

            if (!session.StabilitySettled)
            {
                // 这是唯一允许结构结算致死的阶段。此时 SourceRestored 已经成立，
                // StabilityUtility 还会再次校验实际容器，形成双重保险。
                MechFusionStabilityUtility.SettleSourcePartDurability(
                    session,
                    source);
                session.MarkStabilitySettled();
            }

            MechFusionStatCacheUtility.Invalidate(session);

            if (!session.ApparelRemoved)
            {
                RemoveApparel(session, wearer);
                session.MarkApparelRemoved();
            }

            if (!CanDeleteSession(session, source))
            {
                session.TeardownDeferred = true;
                Log.Warning(
                    "[MAP-机械族机械师] 合体解除收束条件尚未全部满足，" +
                    "已保留会话等待下一轮校验：" +
                    $"session={session.SessionId}。");
                return false;
            }

            FinalizeSession(session, source);
            return true;
        }

        /// <summary>
        /// 源引用暂时不可用时保留记录与服装，等待 Tick 与读档修复重试；
        /// 只有达到重试上限且确认引用永久丢失后，才清理人类侧残留并结束会话。
        /// </summary>
        private static bool HandleMissingSourceReference(
            MechFusionSession session,
            Pawn? wearer)
        {
            session.SetState(MechFusionSessionState.PendingRecovery);
            session.TeardownDeferred = false;
            session.IncrementPendingRecoveryAttempts();

            if (session.PendingRecoveryAttempts < MaxPendingRecoveryAttempts)
            {
                if (session.PendingRecoveryAttempts <= 1)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 合体解除时源 Pawn 引用无法解析，" +
                        "已保留记录与服装等待延迟修复：" +
                        $"session={session.SessionId}。");
                }

                return false;
            }

            Log.Error(
                "[MAP-机械族机械师] 合体解除时源 Pawn 引用已确定永久丢失，" +
                "只能清理目标人类上的本 MOD 效果、临时飞行授权与合体服装，" +
                "不生成替代 Pawn：" +
                $"session={session.SessionId}。");
            CleanupLostSourceSession(session, wearer);
            return false;
        }

        private static bool HandleAirborneExit(
            MechFusionSession session,
            Pawn? wearer)
        {
            if (wearer == null || !MechanicalFlightUtility.IsAirborne(wearer))
            {
                return true;
            }

            MechFusionExitReason reason = session.ExitReason;

            if (wearer.Dead || wearer.Downed)
            {
                // 飞行系统自身的死亡/倒地逻辑会立即坠毁；这里只负责收尾，
                // 不重复调用坠毁入口，避免二次爆炸与二次坠毁。
                MechFusionFlightUtility.EnsureVanillaFlightEnds(wearer);
                if (MechanicalFlightUtility.IsAirborne(wearer))
                {
                    session.TeardownDeferred = true;
                    return false;
                }

                return true;
            }

            if (MechFusionFlightUtility.RequiresImmediateCrash(reason))
            {
                if (!MechanicalFlightEmergencyUtility.TryCrashImmediately(wearer))
                {
                    Log.Warning(
                        "[MAP-机械族机械师] 合体空中退出时无法立即进入坠毁流程，" +
                        "等待飞行系统清理：" +
                        $"pawn={wearer.LabelShort}（{wearer.ThingID}）。");
                }
            }
            else if (reason == MechFusionExitReason.FlightCrash)
            {
                MechanicalFlightEmergencyUtility.TryBeginEmergencySequence(wearer);
            }
            else
            {
                MechanicalFlightUtility.TryBeginLanding(wearer);
            }

            if (MechanicalFlightUtility.IsAirborne(wearer))
            {
                session.TeardownDeferred = true;
                return false;
            }

            session.TeardownDeferred = false;
            return true;
        }

        private static bool RunWearerCleanupSteps(
            MechFusionSession session,
            Pawn? wearer)
        {
            if (!session.FlightRevoked)
            {
                if (!MechFusionFlightUtility.TryRevokeTemporaryFlight(session))
                {
                    session.TeardownDeferred = true;
                    Log.Warning(
                        "[MAP-机械族机械师] 临时飞行授权尚未撤销完成，" +
                        "已保留会话等待下一轮清理：" +
                        $"session={session.SessionId}。");
                    return false;
                }

                session.MarkFlightRevoked();
            }

            if (!session.WhitelistRevoked)
            {
                if (!MechFusionWhitelistUtility.RevokeAll(session, wearer))
                {
                    session.TeardownDeferred = true;
                    Log.Warning(
                        "[MAP-机械族机械师] 至少一条白名单效果尚未撤销完成，" +
                        "已保留会话等待下一轮清理：" +
                        $"session={session.SessionId}。");
                    return false;
                }

                session.MarkWhitelistRevoked();
            }

            if (!session.SynchronizationRemoved)
            {
                MechFusionBodySynchronizationUtility.RemoveFromWearer(wearer);
                session.MarkSynchronizationRemoved();
            }

            MechFusionStatCacheUtility.Invalidate(session);
            return true;
        }

        /// <summary>
        /// 源 Pawn 完成地图、远行队或现有合法容器恢复后，才把 Session 中的
        /// 权威能源提交给最终 Need 实例。存活源 Pawn 写回失败时保留会话重试；
        /// 死亡源 Pawn 不要求能源提交，不阻塞尸体恢复与会话收束。
        /// </summary>
        private static bool TryCommitRestoredSourceEnergy(
            MechFusionSession session,
            Pawn source)
        {
            if (source.Dead)
            {
                return true;
            }

            // 兼容旧存档：旧版 EnergyWrittenBack 只表示“尝试过写回”，
            // 可能发生在 SpawnSetup 重建最终 Need 之前，因此恢复后始终幂等重写。
            if (MechFusionEnergyUtility.TryWriteBackToSource(session, source))
            {
                if (!session.EnergyWrittenBack)
                {
                    session.MarkEnergyWrittenBack();
                }

                return true;
            }

            session.SetState(MechFusionSessionState.Ending);
            session.TeardownDeferred = true;
            Log.Warning(
                "[MAP-机械族机械师] 源机械族已恢复，但最终能源暂时无法写入，" +
                "已保留合体会话等待下一轮重试：" +
                $"pawn={source.LabelShort}（{source.ThingID}），" +
                $"session={session.SessionId}。");
            return false;
        }

        private static bool TryBeginReturnToPawnForm(
            MechFusionSession session,
            Pawn source)
        {
            if (GameComponent_MechTransformationRegistry.TryBeginTransition(
                    source,
                    MechTransformationForm.Pawn,
                    out _,
                    out _))
            {
                return true;
            }

            Thing? carrier = session.FusionApparel;
            if (GameComponent_MechTransformationRegistry.TryBeginRecoveryToPawn(
                    source,
                    carrier,
                    out _,
                    out _))
            {
                return true;
            }

            return false;
        }

        internal static bool TryRestoreTransformationRecord(
            MechFusionSession session,
            Pawn source)
        {
            if (!GameComponent_MechTransformationRegistry.TryGetRecord(
                    source,
                    out MechTransformationRecord? record)
                || record == null)
            {
                // 没有形态记录说明本次合体从未提交形态，视为已恢复。
                return true;
            }

            if (record.CurrentForm == MechTransformationForm.Pawn)
            {
                if (record.ExternalCarrier == null
                    || record.ExternalCarrier.Destroyed)
                {
                    return true;
                }

                if (record.TransitionInProgress)
                {
                    return GameComponent_MechTransformationRegistry
                        .TryCommitTransition(source, null, out _);
                }

                return GameComponent_MechTransformationRegistry
                    .TryForceRestorePawnForm(source);
            }

            if (record.TransitionInProgress)
            {
                return GameComponent_MechTransformationRegistry
                    .TryCommitTransition(source, null, out _);
            }

            if (TryBeginReturnToPawnForm(session, source)
                && GameComponent_MechTransformationRegistry.TryCommitTransition(
                    source,
                    null,
                    out _))
            {
                return true;
            }

            Log.Warning(
                "[MAP-机械族机械师] 形态记录无法通过标准入口恢复，" +
                "尝试强制恢复并解除载体链接：" +
                $"pawn={source.LabelShort}（{source.ThingID}）。");
            return GameComponent_MechTransformationRegistry
                .TryForceRestorePawnForm(source);
        }

        internal static bool TryRestoreSourcePawn(
            MechFusionSession session,
            Pawn source,
            out bool deferred)
        {
            deferred = false;

            Pawn? wearer = session.WearerPawn;
            Caravan? caravan = wearer?.GetCaravan();
            Map? map = wearer != null && wearer.Spawned
                ? wearer.Map
                : session.PendingMap;
            IntVec3 anchor = wearer != null && wearer.Spawned
                ? wearer.Position
                : session.PendingPosition;
            Rot4 rotation = wearer != null && wearer.Spawned
                ? wearer.Rotation
                : session.PendingRotation;

            // 正常死亡后的真实 Pawn 会带有 Destroyed 标记，但仍被 Corpse 持有。
            // 这种情况不是“源 Pawn 丢失”；若尸体已经有合法容器则直接成功，
            // 若尸体暂时悬空则尝试放回远行队或最近恢复位置。
            Corpse? existingCorpse = GetRecoverableCorpse(source);
            if (existingCorpse != null)
            {
                if (IsCorpseInLegalContainer(existingCorpse))
                {
                    return true;
                }

                if (caravan != null)
                {
                    caravan.AddPawnOrItem(
                        existingCorpse,
                        addCarriedPawnToWorldPawnsIfAny: true);
                    return true;
                }

                if (map == null || map.Disposed || !anchor.IsValid)
                {
                    deferred = true;
                    return false;
                }

                IntVec3 corpseCell = CellFinder.FindNoWipeSpawnLocNear(
                    anchor,
                    map,
                    existingCorpse.def,
                    rotation,
                    RestoreSearchRadius);
                if (!corpseCell.IsValid)
                {
                    deferred = true;
                    return false;
                }

                GenSpawn.Spawn(
                    existingCorpse,
                    corpseCell,
                    map,
                    rotation,
                    WipeMode.VanishOrMoveAside);
                return true;
            }

            if (source.Destroyed || source.Discarded)
            {
                return false;
            }

            RestoreOriginalSourceIdentity(session, source);

            if (source.Spawned)
            {
                RemoveFromWorldPawns(source);
                return true;
            }

            if (source.Dead)
            {
                if (caravan != null)
                {
                    RemoveFromWorldPawns(source);
                    Corpse caravanCorpse = EnsureCorpse(source);
                    caravan.AddPawnOrItem(
                        caravanCorpse,
                        addCarriedPawnToWorldPawnsIfAny: true);
                    return true;
                }

                if (map == null || map.Disposed || !anchor.IsValid)
                {
                    EnsureWorldPawn(source);
                    deferred = true;
                    return false;
                }

                IntVec3 corpseCell = CellFinder.FindNoWipeSpawnLocNear(
                    anchor,
                    map,
                    source.RaceProps.corpseDef,
                    rotation,
                    RestoreSearchRadius);
                if (!corpseCell.IsValid)
                {
                    EnsureWorldPawn(source);
                    deferred = true;
                    return false;
                }

                RemoveFromWorldPawns(source);
                Corpse corpse = EnsureCorpse(source);
                GenSpawn.Spawn(
                    corpse,
                    corpseCell,
                    map,
                    rotation,
                    WipeMode.VanishOrMoveAside);
                return true;
            }

            if (caravan != null)
            {
                RemoveFromWorldPawns(source);
                caravan.AddPawn(source, addCarriedPawnToWorldPawnsIfAny: true);
                RestoreOriginalSourceIdentity(session, source);
                return true;
            }

            if (map == null || map.Disposed || !anchor.IsValid)
            {
                EnsureWorldPawn(source);
                deferred = true;
                return false;
            }

            RemoveFromWorldPawns(source);
            IntVec3 spawnCell = CellFinder.FindNoWipeSpawnLocNear(
                anchor,
                map,
                source.def,
                rotation,
                RestoreSearchRadius);
            if (!spawnCell.IsValid)
            {
                EnsureWorldPawn(source);
                deferred = true;
                return false;
            }

            GenSpawn.Spawn(
                source,
                spawnCell,
                map,
                rotation,
                WipeMode.VanishOrMoveAside);
            // SpawnSetup 及第三方生成补丁可能再次处理阵营或监管状态；
            // 生成结束后做一次幂等复核，确保最终落地状态与快照一致。
            RestoreOriginalSourceIdentity(session, source);
            return true;
        }

        /// <summary>
        /// 合体期间源 Pawn 可能被失控或第三方生命周期逻辑改写阵营。
        /// 恢复到地图或远行队前先还原合体开始时的身份；监管关系只为
        /// 原本确有监管者的普通机械族补回，机械族机械师保持无监管者结构。
        /// </summary>
        private static void RestoreOriginalSourceIdentity(
            MechFusionSession session,
            Pawn source)
        {
            session.EnsureOriginalSourceStateForRecovery();
            Faction? originalFaction = session.OriginalSourceFaction;
            if (source.Faction != originalFaction)
            {
                source.SetFactionDirect(originalFaction);
            }

            Pawn? originalOverseer = session.OriginalOverseer;
            if (source.Dead
                || originalOverseer == null
                || originalOverseer.Destroyed
                || originalOverseer.Discarded
                || originalOverseer.Dead
                || MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(source))
            {
                return;
            }

            if (!MechControlGroupPositionUtility.TryRestore(
                    originalOverseer,
                    source,
                    session.OriginalControlGroupIndex))
            {
                Log.Warning(
                    "[MAP-机械族机械师] 已恢复合体源机械族阵营和监管者，" +
                    "但无法精确恢复原控制组位置：" +
                    $"source={source.LabelShort}（{source.ThingID}），" +
                    $"overseer={originalOverseer.LabelShort}（{originalOverseer.ThingID}），" +
                    $"groupIndex={session.OriginalControlGroupIndex}。");
            }
        }

        private static bool CanDeleteSession(
            MechFusionSession session,
            Pawn source)
        {
            if (!session.SourceRestored
                || (!source.Dead && !session.EnergyWrittenBack)
                || !session.TransformationRestored
                || !session.StabilitySettled
                || !session.ApparelRemoved)
            {
                return false;
            }

            if (!IsSourceInRestoredContainer(source))
            {
                return false;
            }

            if (GameComponent_MechTransformationRegistry.TryGetRecord(
                    source,
                    out MechTransformationRecord? record)
                && record != null)
            {
                if (record.CurrentForm != MechTransformationForm.Pawn)
                {
                    return false;
                }

                if (record.ExternalCarrier != null
                    && !record.ExternalCarrier.Destroyed)
                {
                    return false;
                }
            }

            if (session.FusionApparel is Thing apparel
                && !apparel.Destroyed
                && apparel.TryGetComp<CompMechFormCarrier>()
                    is CompMechFormCarrier carrier
                && carrier.Committed)
            {
                return false;
            }

            return true;
        }

        private static void FinalizeSession(
            MechFusionSession session,
            Pawn source)
        {
            Pawn? wearer = session.WearerPawn;
            GameComponent_MechFusionSessionRegistry.RemoveSession(session);
            RefreshAfterEnd(source, wearer);

            Need_MechEnergy? energy = source.needs?.energy;
            if (energy != null && energy.CurLevel <= 0f && !source.Dead)
            {
                energy.NeedInterval();
            }

            Messages.Message(
                "MAP_MechanoidMechanitor.Fusion.Ended".Translate(
                    source.LabelShortCap,
                    wearer?.LabelShortCap ?? "-"),
                source,
                MessageTypeDefOf.NeutralEvent,
                historical: false);
        }

        /// <summary>
        /// 源 Pawn 已销毁或永久丢弃：不可能恢复原 Pawn，但人类身上的本 MOD
        /// 效果、临时飞行授权与合体服装必须完整清理，并尽量修复损坏的形态记录。
        /// </summary>
        private static void CleanupUnrecoverableSource(
            MechFusionSession session,
            Pawn source,
            Pawn? wearer)
        {
            Log.Error(
                "[MAP-机械族机械师] 合体解除时源 Pawn 已被销毁或永久丢弃，" +
                "无法恢复原 Pawn；将保留会话直到目标人类上的临时效果全部清理完成，" +
                "且不会生成任何复制 Pawn：" +
                $"session={session.SessionId}。");

            if (!HandleAirborneExit(session, wearer))
            {
                session.SetState(MechFusionSessionState.Ending);
                session.TeardownDeferred = true;
                return;
            }

            bool cleanupComplete = true;
            cleanupComplete &= TryCleanupStep(
                session,
                "撤销临时飞行授权",
                () =>
                {
                    if (!session.FlightRevoked)
                    {
                        if (!MechFusionFlightUtility.TryRevokeTemporaryFlight(
                                session))
                        {
                            throw new InvalidOperationException(
                                "临时飞行授权仍处于活动状态。");
                        }

                        session.MarkFlightRevoked();
                    }
                });
            cleanupComplete &= TryCleanupStep(
                session,
                "撤销白名单效果",
                () =>
                {
                    if (!session.WhitelistRevoked)
                    {
                        if (!MechFusionWhitelistUtility.RevokeAll(
                                session,
                                wearer))
                        {
                            throw new InvalidOperationException(
                                "至少一条白名单效果撤销失败。");
                        }

                        session.MarkWhitelistRevoked();
                    }
                });
            cleanupComplete &= TryCleanupStep(
                session,
                "移除机体同调",
                () =>
                {
                    if (!session.SynchronizationRemoved)
                    {
                        MechFusionBodySynchronizationUtility.RemoveFromWearer(
                            wearer);
                        session.MarkSynchronizationRemoved();
                    }
                });
            cleanupComplete &= TryCleanupStep(
                session,
                "移除休眠维持标记",
                () => MechFusionSourceUtility.RemoveDormantGuard(source));
            cleanupComplete &= TryCleanupStep(
                session,
                "修复损坏形态记录",
                () =>
                {
                    if (GameComponent_MechTransformationRegistry.TryGetRecord(
                            source,
                            out MechTransformationRecord? record)
                        && record != null
                        && !GameComponent_MechTransformationRegistry
                            .TryForceRestorePawnForm(source))
                    {
                        throw new InvalidOperationException(
                            "损坏的形态记录无法恢复。");
                    }
                });
            cleanupComplete &= TryCleanupStep(
                session,
                "清理合体服装",
                () =>
                {
                    if (!session.ApparelRemoved)
                    {
                        RemoveApparel(session, wearer);
                        session.MarkApparelRemoved();
                    }
                });

            if (!cleanupComplete
                || !session.FlightRevoked
                || !session.WhitelistRevoked
                || !session.SynchronizationRemoved
                || !session.ApparelRemoved)
            {
                session.SetState(MechFusionSessionState.Ending);
                session.TeardownDeferred = true;
                Log.Error(
                    "[MAP-机械族机械师] 源 Pawn 不可恢复时仍有合体清理步骤未完成，" +
                    "已保留权威会话等待下一 Tick 重试：" +
                    $"session={session.SessionId}。");
                return;
            }

            MechFusionStatCacheUtility.Invalidate(session);
            GameComponent_MechFusionSessionRegistry.RemoveSession(session);
            RefreshAfterEnd(null, wearer);
        }

        private static void CleanupLostSourceSession(
            MechFusionSession session,
            Pawn? wearer)
        {
            if (!HandleAirborneExit(session, wearer))
            {
                session.TeardownDeferred = true;
                return;
            }

            bool cleanupComplete = true;
            cleanupComplete &= TryCleanupStep(
                session,
                "撤销临时飞行授权",
                () =>
                {
                    if (!session.FlightRevoked)
                    {
                        if (!MechFusionFlightUtility.TryRevokeTemporaryFlight(
                                session))
                        {
                            throw new InvalidOperationException(
                                "临时飞行授权仍处于活动状态。");
                        }

                        session.MarkFlightRevoked();
                    }
                });
            cleanupComplete &= TryCleanupStep(
                session,
                "撤销白名单效果",
                () =>
                {
                    if (!session.WhitelistRevoked)
                    {
                        if (!MechFusionWhitelistUtility.RevokeAll(
                                session,
                                wearer))
                        {
                            throw new InvalidOperationException(
                                "至少一条白名单效果撤销失败。");
                        }

                        session.MarkWhitelistRevoked();
                    }
                });
            cleanupComplete &= TryCleanupStep(
                session,
                "移除机体同调",
                () =>
                {
                    if (!session.SynchronizationRemoved)
                    {
                        MechFusionBodySynchronizationUtility.RemoveFromWearer(
                            wearer);
                        session.MarkSynchronizationRemoved();
                    }
                });
            cleanupComplete &= TryCleanupStep(
                session,
                "清理合体服装",
                () =>
                {
                    if (!session.ApparelRemoved)
                    {
                        RemoveApparel(session, wearer);
                        session.MarkApparelRemoved();
                    }
                });

            if (!cleanupComplete
                || !session.FlightRevoked
                || !session.WhitelistRevoked
                || !session.SynchronizationRemoved
                || !session.ApparelRemoved)
            {
                session.TeardownDeferred = true;
                Log.Error(
                    "[MAP-机械族机械师] 源 Pawn 引用永久丢失后仍有合体清理步骤未完成，" +
                    "已保留权威会话等待下一 Tick 重试：" +
                    $"session={session.SessionId}。");
                return;
            }

            MechFusionStatCacheUtility.Invalidate(session);
            GameComponent_MechFusionSessionRegistry.RemoveSession(session);
            RefreshAfterEnd(null, wearer);
        }

        private static bool TryCleanupStep(
            MechFusionSession session,
            string stepName,
            Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 合体异常清理步骤失败（" + stepName + "）：" +
                    $"session={session.SessionId}：{ex}");
                return false;
            }
        }

        internal static void RemoveApparel(
            MechFusionSession session,
            Pawn? wearer)
        {
            Thing? apparel = session.FusionApparel;
            if (apparel == null || apparel.Destroyed)
            {
                return;
            }

            if (wearer?.apparel != null
                && apparel is Apparel wornApparel
                && wearer.apparel.Wearing(wornApparel))
            {
                wearer.apparel.Remove(wornApparel);
            }

            apparel.Destroy(DestroyMode.Vanish);
        }

        private static bool IsSourceTrulyUnrecoverable(Pawn source)
        {
            if (source.Discarded)
            {
                return true;
            }

            if (!source.Destroyed)
            {
                return false;
            }

            return GetRecoverableCorpse(source) == null;
        }

        private static bool IsSourceInRestoredContainer(Pawn source)
        {
            if (source.Discarded)
            {
                return false;
            }

            if (source.Dead)
            {
                Corpse? corpse = GetRecoverableCorpse(source);
                return corpse != null && IsCorpseInLegalContainer(corpse);
            }

            if (source.Destroyed || Find.WorldPawns.Contains(source))
            {
                return false;
            }

            return source.Spawned || source.GetCaravan() != null;
        }

        private static Corpse? GetRecoverableCorpse(Pawn source)
        {
            Corpse? corpse = source.Corpse;
            return corpse != null && !corpse.Destroyed ? corpse : null;
        }

        private static bool IsCorpseInLegalContainer(Corpse corpse)
        {
            return !corpse.Destroyed
                && (corpse.Spawned || corpse.ParentHolder != null);
        }

        private static void RemoveFromWorldPawns(Pawn pawn)
        {
            if (Find.WorldPawns.Contains(pawn))
            {
                Find.WorldPawns.RemovePawn(pawn);
            }
        }

        private static void EnsureWorldPawn(Pawn pawn)
        {
            if (pawn.Destroyed || pawn.Discarded)
            {
                return;
            }

            if (!Find.WorldPawns.Contains(pawn))
            {
                Find.WorldPawns.PassToWorld(
                    pawn,
                    PawnDiscardDecideMode.KeepForever);
            }
        }

        private static Corpse EnsureCorpse(Pawn pawn)
        {
            Corpse? corpse = pawn.Corpse;
            if (corpse == null)
            {
                corpse = (Corpse)ThingMaker.MakeThing(pawn.RaceProps.corpseDef);
                corpse.InnerPawn = pawn;
            }

            return corpse;
        }

        private static void RefreshAfterEnd(Pawn? source, Pawn? wearer)
        {
            wearer?.Drawer?.renderer?.SetAllGraphicsDirty();
            if (source != null && !source.Destroyed)
            {
                source.Drawer?.renderer?.SetAllGraphicsDirty();
            }
        }
    }
}
