using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// “机控同调”的快照、控制权迁移、回转和异常失控入口。
    /// 本服务不直接增删 Hediff；Hediff 生命周期始终由合体健康状态管理层负责。
    /// </summary>
    internal static class MechFusionMechanitorSynchronizationService
    {
        private static readonly AccessTools.FieldRef<
            MechanitorControlGroup,
            Dictionary<Pawn, string>> TagsField =
            AccessTools.FieldRefAccess<
                MechanitorControlGroup,
                Dictionary<Pawn, string>>("tags");

        private static readonly AccessTools.FieldRef<
            Pawn_PlayerSettings,
            Dictionary<Map, Area>> AllowedAreasField =
            AccessTools.FieldRefAccess<
                Pawn_PlayerSettings,
                Dictionary<Map, Area>>("allowedAreas");

        internal static bool HasTemporaryMechanitorAccess(Pawn? pawn)
        {
            HediffDef? def = GetSynchronizationDef();
            return pawn?.health?.hediffSet != null
                && def != null
                && pawn.health.hediffSet.HasHediff(def);
        }

        internal static bool GrantsQuantumCommunicatorEffect(Pawn? pawn)
        {
            return TryGetActiveSnapshot(
                    pawn,
                    out MechFusionMechanitorSnapshot? snapshot)
                && snapshot!.grantsQuantumCommunicator;
        }

        internal static bool GrantsProxySubchainEffect(Pawn? pawn)
        {
            return TryGetActiveSnapshot(
                    pawn,
                    out MechFusionMechanitorSnapshot? snapshot)
                && snapshot!.grantsProxySubchain;
        }

        /// <summary>
        /// 必须在源机械师 DeSpawn 前调用；原版 DeSpawn 会解除下属征召。
        /// </summary>
        internal static void CaptureBeforeSourceDespawn(
            MechFusionSession session,
            Pawn? source,
            Pawn? wearer)
        {
            if (session == null
                || source == null
                || wearer == null
                || session.MechanitorSnapshot != null)
            {
                return;
            }

            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(source))
            {
                return;
            }

            Log.Message(
                "[MAP-机械族机械师] 机控同调开始捕获：" +
                $"session={session.SessionId}，" +
                $"source={FormatPawnForLog(source)}，" +
                $"wearer={FormatPawnForLog(wearer)}。");

            MechanoidMechanitorRoleUtility.EnsureRoleState(source);
            Pawn_MechanitorTracker? sourceTracker = source.mechanitor;
            if (sourceTracker == null)
            {
                Log.Warning(
                    "[MAP-机械族机械师] 机控同调无法捕获源机械师 Tracker：" +
                    $"source={source.ThingID}。");
                return;
            }

            MAPMechanitorNodeLifecycleUtility
                .EnsureMechanitorTrackerCollections(sourceTracker);
            Pawn_MechanitorTracker? wearerTracker = wearer.mechanitor;
            bool wearerHadMechlink =
                wearer.health?.hediffSet?.HasHediff(
                    HediffDefOf.MechlinkImplant) == true;
            bool wearerWasMechanitor = wearerHadMechlink
                || MechanitorUtility.IsMechanitor(wearer);
            int destinationStart = 0;
            if (wearerWasMechanitor && wearerTracker?.controlGroups != null)
            {
                for (int i = 0; i < wearerTracker.controlGroups.Count; i++)
                {
                    if (HasAssignedMech(wearerTracker.controlGroups[i]))
                    {
                        destinationStart = i + 1;
                    }
                }
            }

            var snapshot = new MechFusionMechanitorSnapshot
            {
                captured = true,
                wearerWasMechanitor = wearerWasMechanitor,
                wearerHadMechlink = wearerHadMechlink,
                implantEffectsCaptured = true,
                grantsQuantumCommunicator =
                    QuantumCommunicatorUtility.HasImplant(source),
                grantsProxySubchain =
                    ProxySubchainUtility.HasImplant(source),
                bandwidthBonus = Math.Max(0, sourceTracker.TotalBandwidth),
                controlGroupBonus = Math.Max(
                    0,
                    sourceTracker.TotalAvailableControlGroups),
                destinationStartGroupIndex = destinationStart
            };

            List<MechanitorControlGroup> groups = sourceTracker.controlGroups;
            // 控制组列表理论上会被原版修剪到当前上限，但仍以
            // 实际 AssignedMechs 为准：即使遇到尚未修剪的边缘状态，
            // 也不能漏掉其中的机械体。
            int groupCount = groups.Count;
            for (int i = 0; i < groupCount; i++)
            {
                snapshot.sourceGroups.Add(
                    CaptureGroup(groups[i], i, captureMechs: true));
            }

            session.SetMechanitorSnapshot(snapshot);
            DataProcessingOverseerResolver.CaptureFrozenSelf(session);
            Log.Message(
                "[MAP-机械族机械师] 机控同调捕获完成：" +
                $"session={session.SessionId}，sourceGroups={groupCount}，" +
                $"mechs={CountSnapshotMechs(snapshot)}，" +
                $"bandwidthBonus={snapshot.bandwidthBonus}，" +
                $"controlGroupBonus={snapshot.controlGroupBonus}，" +
                $"destinationStartGroup={snapshot.destinationStartGroupIndex + 1}。");
        }

        /// <summary>
        /// 兼容功能加入前已经处于活动状态的旧会话。旧实现没有迁移控制权，
        /// 因此源机械师仍保存着可用于迁移的控制组事实。
        /// </summary>
        internal static void EnsureLegacySnapshot(
            MechFusionSession session,
            Pawn? source,
            Pawn? wearer)
        {
            MechFusionMechanitorSnapshot? snapshot =
                session.MechanitorSnapshot;
            if (snapshot != null)
            {
                if (!snapshot.implantEffectsCaptured
                    && source != null
                    && MechanoidMechanitorRoleUtility
                        .IsMechanoidMechanitor(source))
                {
                    snapshot.grantsQuantumCommunicator =
                        QuantumCommunicatorUtility.HasImplant(source);
                    snapshot.grantsProxySubchain =
                        ProxySubchainUtility.HasImplant(source);
                    snapshot.implantEffectsCaptured = true;
                }

                return;
            }

            CaptureBeforeSourceDespawn(session, source, wearer);
        }

        internal static void ApplyOrRepair(
            MechFusionSession session,
            Pawn? source,
            Pawn? wearer)
        {
            using var dataProcessingTransfer = DataProcessingOverseerResolver.BeginControlTransfer(session);

            MechFusionMechanitorSnapshot? snapshot = session.MechanitorSnapshot;
            if (snapshot == null
                || !snapshot.captured
                || snapshot.controlsRestored
                || wearer == null)
            {
                if (snapshot != null
                    || (source != null
                        && MechanoidMechanitorRoleUtility
                            .IsMechanoidMechanitor(source)))
                {
                    Log.Warning(
                        "[MAP-机械族机械师] 机控同调跳过接管：" +
                        $"session={session?.SessionId ?? "null"}，" +
                        $"snapshot={(snapshot != null)}，" +
                        $"captured={snapshot?.captured.ToString() ?? "n/a"}，" +
                        $"controlsRestored=" +
                        $"{snapshot?.controlsRestored.ToString() ?? "n/a"}，" +
                        $"wearer={FormatPawnForLog(wearer)}。");
                }

                return;
            }

            Log.Message(
                "[MAP-机械族机械师] 机控同调开始接管：" +
                $"session={session.SessionId}，" +
                $"source={FormatPawnForLog(source)}，" +
                $"wearer={FormatPawnForLog(wearer)}，" +
                $"mechs={CountSnapshotMechs(snapshot)}，" +
                $"transferApplied={snapshot.transferApplied}。");

            PawnComponentsUtility.AddAndRemoveDynamicComponents(wearer);
            Pawn_MechanitorTracker? tracker = wearer.mechanitor;
            Hediff? synchronization = FindSynchronizationHediff(wearer);
            if (tracker == null || synchronization == null)
            {
                LogTransferDiagnostic(
                    session,
                    "合体接管初始化",
                    wearer,
                    null,
                    -1,
                    $"必要组件缺失：tracker={(tracker != null)}，" +
                    $"synchronizationHediff={(synchronization != null)}。");
                MarkAllTransferFailures(snapshot);
                return;
            }

            MAPMechanitorNodeLifecycleUtility.EnsureMechanitorTrackerCollections(
                tracker);
            tracker.Notify_HediffStateChange(synchronization);
            if (!CaptureDestinationBackups(
                    snapshot,
                    tracker,
                    out string backupFailureReason))
            {
                LogTransferDiagnostic(
                    session,
                    "合体接管控制组快照",
                    wearer,
                    null,
                    snapshot.destinationStartGroupIndex,
                    backupFailureReason);
                MarkAllTransferFailures(snapshot);
                return;
            }

            if (!snapshot.transferApplied)
            {
                ApplyImportedGroupSettings(snapshot, tracker);
            }
            foreach (MechFusionControlledMechSnapshot mechSnapshot
                     in EnumerateMechs(snapshot))
            {
                Pawn? mech = mechSnapshot.pawn;
                if (mech == null
                    || mech.Destroyed
                    || mech.Discarded
                    || mech.Dead
                    || mechSnapshot.transferFailed)
                {
                    continue;
                }

                // 正确的监管关系已经保存时，不覆盖玩家在合体期间调整的组号。
                if (MAPOverseerRelationDirectionUtility.IsActualOverseerOf(
                        wearer,
                        mech))
                {
                    if (!TrySetMechFaction(
                            mech,
                            wearer.Faction,
                            out string existingFactionFailureReason))
                    {
                        LogTransferDiagnostic(
                            session,
                            "合体接管已有关系派系同步",
                            wearer,
                            mech,
                            snapshot.destinationStartGroupIndex
                                + mechSnapshot.sourceGroupIndex,
                            existingFactionFailureReason);
                        mechSnapshot.transferFailed = true;
                        TryDisconnectCompletely(mech);
                        LogTransferFailure(mech);
                    }

                    continue;
                }

                int destinationIndex = snapshot.destinationStartGroupIndex
                    + mechSnapshot.sourceGroupIndex;
                if (!TryAssignToSpecificGroup(
                        wearer,
                        mech,
                        destinationIndex,
                        fallbackToFirstGroup: true,
                        out string transferFailureReason))
                {
                    LogTransferDiagnostic(
                        session,
                        "合体接管",
                        wearer,
                        mech,
                        destinationIndex,
                        transferFailureReason);
                    mechSnapshot.transferFailed = true;
                    TryDisconnectCompletely(mech);
                    LogTransferFailure(mech);
                    continue;
                }

                if (!TrySetMechFaction(
                        mech,
                        wearer.Faction,
                        out string factionFailureReason))
                {
                    LogTransferDiagnostic(
                        session,
                        "合体接管派系同步",
                        wearer,
                        mech,
                        destinationIndex,
                        factionFailureReason);
                    mechSnapshot.transferFailed = true;
                    TryDisconnectCompletely(mech);
                    LogTransferFailure(mech);
                    continue;
                }

                RestoreMechSettings(mechSnapshot);
                RestoreAssignedTick(
                    tracker.GetControlGroup(mech),
                    mech,
                    mechSnapshot.assignedTick);
            }

            source?.mechanitor?.Notify_BandwidthChanged();
            tracker.Notify_BandwidthChanged();
            AuditTransferResults(session, snapshot, wearer, tracker);
            snapshot.transferApplied = true;
        }

        /// <summary>
        /// 健康状态撤销前调用。所有单体回转失败都转为明确的未受控状态，
        /// 记录警告后继续，不阻塞其他机械体与整体解除流程。
        /// </summary>
        internal static void PrepareForHealthEffectRemoval(
            MechFusionSession session,
            Pawn? source,
            Pawn? wearer,
            bool sourceRecoverable)
        {
            using var dataProcessingTransfer = DataProcessingOverseerResolver.BeginControlTransfer(session);

            MechFusionMechanitorSnapshot? snapshot = session.MechanitorSnapshot;
            if (snapshot == null || !snapshot.captured || snapshot.controlsRestored)
            {
                return;
            }

            if (!sourceRecoverable
                || source == null
                || source.Destroyed
                || source.Discarded
                || source.Dead)
            {
                if (!snapshot.wearerWasMechanitor)
                {
                    DisconnectAllOverseenBy(wearer);
                }

                snapshot.controlsRestored = true;
                return;
            }

            // 回转发生在源 Pawn 放回地图之前，不能等待 TryRestoreSourcePawn 才恢复阵营。
            // 合体期间源 Pawn 的阵营可能变化；EnsureRoleState 只补空阵营，
            // 非玩家阵营会令 IsMechanitor 拒绝监管，即使 Tracker 和带宽仍然完整。
            MechFusionTeardownService.RestoreOriginalSourceIdentity(session, source);
            MechanoidMechanitorRoleUtility.EnsureRoleState(source);
            Pawn_MechanitorTracker? sourceTracker = source.mechanitor;
            if (sourceTracker == null)
            {
                foreach (MechFusionControlledMechSnapshot mechSnapshot
                         in EnumerateMechs(snapshot))
                {
                    ResolveRestoreFailure(
                        session,
                        mechSnapshot,
                        source,
                        mechSnapshot.sourceGroupIndex,
                        "源机械师缺少 Mechanitor Tracker。");
                }

                if (!snapshot.wearerWasMechanitor)
                {
                    DisconnectAllOverseenBy(wearer);
                }

                snapshot.controlsRestored = true;
                return;
            }

            MAPMechanitorNodeLifecycleUtility.EnsureMechanitorTrackerCollections(
                sourceTracker);
            RestoreSourceGroupSettings(snapshot, sourceTracker);

            HashSet<Pawn> originalMechs = new HashSet<Pawn>();
            foreach (MechFusionControlledMechSnapshot mechSnapshot
                     in EnumerateMechs(snapshot))
            {
                Pawn? mech = mechSnapshot.pawn;
                if (mech != null)
                {
                    originalMechs.Add(mech);
                }

                if (mechSnapshot.restoreResolved)
                {
                    continue;
                }

                if (mech == null
                    || mech.Destroyed
                    || mech.Discarded
                    || mech.Dead)
                {
                    mechSnapshot.restoreResolved = true;
                    continue;
                }

                if (!TryAssignToSpecificGroup(
                        source,
                        mech,
                        mechSnapshot.sourceGroupIndex,
                        fallbackToFirstGroup: true,
                        out string restoreFailureReason))
                {
                    ResolveRestoreFailure(
                        session,
                        mechSnapshot,
                        source,
                        mechSnapshot.sourceGroupIndex,
                        restoreFailureReason);
                    continue;
                }

                Faction? restoredFaction =
                    mechSnapshot.originalFactionCaptured
                        ? mechSnapshot.originalFaction
                        : source.Faction;
                if (!TrySetMechFaction(
                        mech,
                        restoredFaction,
                        out string restoreFactionFailureReason))
                {
                    ResolveRestoreFailure(
                        session,
                        mechSnapshot,
                        source,
                        mechSnapshot.sourceGroupIndex,
                        restoreFactionFailureReason);
                    continue;
                }

                RestoreMechSettings(mechSnapshot);
                RestoreAssignedTick(
                    sourceTracker.GetControlGroup(mech),
                    mech,
                    mechSnapshot.assignedTick);
                mechSnapshot.restoreResolved = true;
            }

            // 临时机械师在合体期间新接管的机械体统一交给源机械师第一组。
            if (!snapshot.wearerWasMechanitor && wearer?.mechanitor != null)
            {
                List<Pawn> additional = CollectAssignedAndOverseen(
                    wearer.mechanitor);
                for (int i = 0; i < additional.Count; i++)
                {
                    Pawn? mech = additional[i];
                    if (mech == null || originalMechs.Contains(mech))
                    {
                        continue;
                    }

                    if (!TryAssignToSpecificGroup(
                            source,
                            mech,
                            0,
                            fallbackToFirstGroup: true,
                            out string additionalFailureReason))
                    {
                        LogTransferDiagnostic(
                            session,
                            "解除合体期间新增机械体回转",
                            source,
                            mech,
                            0,
                            additionalFailureReason);
                        TryDisconnectCompletely(mech);
                        LogTransferFailure(mech);
                        continue;
                    }

                    if (!TrySetMechFaction(
                            mech,
                            source.Faction,
                            out string additionalFactionFailureReason))
                    {
                        LogTransferDiagnostic(
                            session,
                            "解除合体期间新增机械体派系回转",
                            source,
                            mech,
                            0,
                            additionalFactionFailureReason);
                        TryDisconnectCompletely(mech);
                        LogTransferFailure(mech);
                    }
                }
            }

            sourceTracker.Notify_BandwidthChanged();
            wearer?.mechanitor?.Notify_BandwidthChanged();
            RestoreDestinationGroups(snapshot, wearer?.mechanitor);
            snapshot.controlsRestored = true;
        }

        internal static void NotifyHealthEffectsRevoked(
            MechFusionSession session,
            Pawn? wearer)
        {
            MechFusionMechanitorSnapshot? snapshot = session.MechanitorSnapshot;
            if (snapshot == null || wearer == null || snapshot.wearerWasMechanitor)
            {
                return;
            }

            PawnComponentsUtility.AddAndRemoveDynamicComponents(wearer);
        }

        private static bool CaptureDestinationBackups(
            MechFusionMechanitorSnapshot snapshot,
            Pawn_MechanitorTracker tracker,
            out string failureReason)
        {
            failureReason = string.Empty;
            if (snapshot.destinationGroupsCaptured)
            {
                return true;
            }

            int required = snapshot.destinationStartGroupIndex
                + snapshot.sourceGroups.Count;
            if (tracker.controlGroups == null
                || tracker.controlGroups.Count < snapshot.destinationStartGroupIndex)
            {
                failureReason =
                    $"目标控制组不足：controlGroups=" +
                    $"{tracker.controlGroups?.Count.ToString() ?? "null"}，" +
                    $"destinationStart={snapshot.destinationStartGroupIndex}，" +
                    $"sourceGroups={snapshot.sourceGroups.Count}，required={required}。";
                return false;
            }

            snapshot.destinationGroupBackups.Clear();
            int availableEnd = Math.Min(
                required,
                tracker.controlGroups.Count);
            for (int i = snapshot.destinationStartGroupIndex;
                 i < availableEnd;
                 i++)
            {
                snapshot.destinationGroupBackups.Add(
                    CaptureGroup(tracker.controlGroups[i], i, captureMechs: false));
            }

            snapshot.destinationGroupsCaptured = true;
            return true;
        }

        private static void ApplyImportedGroupSettings(
            MechFusionMechanitorSnapshot snapshot,
            Pawn_MechanitorTracker tracker)
        {
            for (int i = 0; i < snapshot.sourceGroups.Count; i++)
            {
                int destinationIndex = snapshot.destinationStartGroupIndex + i;
                if (destinationIndex < 0
                    || destinationIndex >= tracker.controlGroups.Count)
                {
                    continue;
                }

                ApplyGroupSettings(
                    tracker.controlGroups[destinationIndex],
                    snapshot.sourceGroups[i]);
            }
        }

        private static void RestoreSourceGroupSettings(
            MechFusionMechanitorSnapshot snapshot,
            Pawn_MechanitorTracker tracker)
        {
            for (int i = 0; i < snapshot.sourceGroups.Count; i++)
            {
                MechFusionControlGroupSnapshot groupSnapshot =
                    snapshot.sourceGroups[i];
                if (groupSnapshot.groupIndex < 0
                    || groupSnapshot.groupIndex >= tracker.controlGroups.Count)
                {
                    continue;
                }

                ApplyGroupSettings(
                    tracker.controlGroups[groupSnapshot.groupIndex],
                    groupSnapshot);
            }
        }

        private static void RestoreDestinationGroups(
            MechFusionMechanitorSnapshot snapshot,
            Pawn_MechanitorTracker? tracker)
        {
            if (snapshot.destinationGroupsRestored || tracker?.controlGroups == null)
            {
                return;
            }

            for (int i = 0; i < snapshot.destinationGroupBackups.Count; i++)
            {
                MechFusionControlGroupSnapshot backup =
                    snapshot.destinationGroupBackups[i];
                if (backup.groupIndex < 0
                    || backup.groupIndex >= tracker.controlGroups.Count)
                {
                    continue;
                }

                ApplyGroupSettings(tracker.controlGroups[backup.groupIndex], backup);
            }

            snapshot.destinationGroupsRestored = true;
        }

        private static MechFusionControlGroupSnapshot CaptureGroup(
            MechanitorControlGroup group,
            int groupIndex,
            bool captureMechs)
        {
            var result = new MechFusionControlGroupSnapshot
            {
                groupIndex = groupIndex,
                workMode = group.WorkMode,
                target = group.Target,
                rechargeThresholds = group.mechRechargeThresholds
            };

            Dictionary<Pawn, string>? tags = TagsField(group);
            if (tags != null)
            {
                foreach (KeyValuePair<Pawn, string> pair in tags)
                {
                    result.tags.Add(new MechFusionControlGroupTagSnapshot
                    {
                        pawn = pair.Key,
                        tag = pair.Value
                    });
                }
            }

            if (!captureMechs)
            {
                return result;
            }

            List<AssignedMech> assigned = group.AssignedMechs;
            for (int i = 0; i < assigned.Count; i++)
            {
                AssignedMech? entry = assigned[i];
                Pawn? mech = entry?.pawn;
                if (mech == null)
                {
                    continue;
                }

                CompMechRepairable? repairable =
                    mech.TryGetComp<CompMechRepairable>();
                var mechSnapshot = new MechFusionControlledMechSnapshot
                {
                    pawn = mech,
                    originalFaction = mech.Faction,
                    originalFactionCaptured = true,
                    sourceGroupIndex = groupIndex,
                    assignedTick = entry!.tickAssigned,
                    assignedOrder = i,
                    drafted = mech.Drafted,
                    autoRepairAvailable = repairable != null,
                    autoRepair = repairable?.autoRepair == true
                };
                CaptureAllowedAreas(mech, mechSnapshot.allowedAreas);
                result.mechs.Add(mechSnapshot);
            }

            return result;
        }

        private static void ApplyGroupSettings(
            MechanitorControlGroup group,
            MechFusionControlGroupSnapshot snapshot)
        {
            group.SetWorkMode(
                snapshot.workMode ?? MechWorkModeDefOf.Work,
                snapshot.target);
            group.mechRechargeThresholds = snapshot.rechargeThresholds;
            Dictionary<Pawn, string> restoredTags =
                new Dictionary<Pawn, string>();
            for (int i = 0; i < snapshot.tags.Count; i++)
            {
                MechFusionControlGroupTagSnapshot? tagSnapshot = snapshot.tags[i];
                if (tagSnapshot?.pawn != null && tagSnapshot.tag != null)
                {
                    restoredTags[tagSnapshot.pawn] = tagSnapshot.tag;
                }
            }

            ref Dictionary<Pawn, string> tags = ref TagsField(group);
            tags = restoredTags.Count > 0 ? restoredTags : null!;
        }

        private static bool TryAssignToSpecificGroup(
            Pawn overseer,
            Pawn mech,
            int preferredIndex,
            bool fallbackToFirstGroup,
            out string failureReason)
        {
            failureReason = string.Empty;
            try
            {
                if (!MAPOverseerAssignmentUtility.TryAssignActualOverseer(
                        overseer,
                        mech))
                {
                    failureReason = "TryAssignActualOverseer 返回 false。";
                    return false;
                }

                Pawn_MechanitorTracker? tracker = overseer.mechanitor;
                if (tracker?.controlGroups == null
                    || tracker.controlGroups.Count == 0)
                {
                    failureReason = "监管关系写入后目标 Mechanitor Tracker 没有控制组。";
                    return false;
                }

                if (preferredIndex >= 0
                    && preferredIndex < tracker.controlGroups.Count)
                {
                    tracker.controlGroups[preferredIndex].Assign(mech);
                    if (ReferenceEquals(
                            tracker.GetControlGroup(mech),
                            tracker.controlGroups[preferredIndex]))
                    {
                        return true;
                    }
                }

                if (!fallbackToFirstGroup)
                {
                    failureReason =
                        $"首选控制组分配未通过校验，且不允许回退：" +
                        $"preferredIndex={preferredIndex}，" +
                        $"controlGroups={tracker.controlGroups.Count}。";
                    return false;
                }

                tracker.controlGroups[0].Assign(mech);
                bool assignedToFallback = ReferenceEquals(
                    tracker.GetControlGroup(mech),
                    tracker.controlGroups[0]);
                if (!assignedToFallback)
                {
                    failureReason =
                        $"首选控制组与控制组1分配均未通过校验：" +
                        $"preferredIndex={preferredIndex}，" +
                        $"controlGroups={tracker.controlGroups.Count}。";
                }

                return assignedToFallback;
            }
            catch (Exception exception)
            {
                failureReason = $"分配过程抛出异常：{exception}";
                return false;
            }
        }

        private static void RestoreAssignedTick(
            MechanitorControlGroup? group,
            Pawn mech,
            int assignedTick)
        {
            List<AssignedMech>? assigned = group?.AssignedMechs;
            if (assigned == null)
            {
                return;
            }

            for (int i = 0; i < assigned.Count; i++)
            {
                if (ReferenceEquals(assigned[i]?.pawn, mech))
                {
                    assigned[i].tickAssigned = assignedTick;
                    return;
                }
            }
        }

        private static bool TrySetMechFaction(
            Pawn mech,
            Faction? faction,
            out string failureReason)
        {
            failureReason = string.Empty;
            try
            {
                bool drafted = mech.Drafted;
                List<MechFusionAllowedAreaSnapshot> allowedAreas =
                    new List<MechFusionAllowedAreaSnapshot>();
                CaptureAllowedAreas(mech, allowedAreas);
                if (!ReferenceEquals(mech.Faction, faction))
                {
                    mech.SetFaction(faction);
                    RestoreAllowedAreas(mech, allowedAreas);
                    if (mech.drafter != null && mech.Drafted != drafted)
                    {
                        mech.drafter.Drafted = drafted;
                    }
                }

                if (ReferenceEquals(mech.Faction, faction))
                {
                    return true;
                }

                failureReason =
                    "SetFaction 后派系校验失败：" +
                    $"expected={faction?.def?.defName ?? "null"}，" +
                    $"actual={mech.Faction?.def?.defName ?? "null"}。";
                return false;
            }
            catch (Exception exception)
            {
                failureReason = $"派系同步过程抛出异常：{exception}";
                return false;
            }
        }

        private static void RestoreMechSettings(
            MechFusionControlledMechSnapshot snapshot)
        {
            Pawn? mech = snapshot.pawn;
            if (mech == null)
            {
                return;
            }

            RestoreAllowedAreas(mech, snapshot.allowedAreas);
            if (snapshot.autoRepairAvailable)
            {
                CompMechRepairable? repairable =
                    mech.TryGetComp<CompMechRepairable>();
                if (repairable != null)
                {
                    repairable.autoRepair = snapshot.autoRepair;
                }
            }

            if (mech.drafter != null && mech.Drafted != snapshot.drafted)
            {
                try
                {
                    mech.drafter.Drafted = snapshot.drafted;
                }
                catch (Exception ex)
                {
                    Log.Warning(
                        "[MAP-机械族机械师] 恢复机控同调机械体征召状态失败：" +
                        $"pawn={mech.ThingID}：{ex}");
                }
            }
        }

        private static void CaptureAllowedAreas(
            Pawn mech,
            List<MechFusionAllowedAreaSnapshot> result)
        {
            if (mech.playerSettings == null)
            {
                return;
            }

            Dictionary<Map, Area>? allowed = AllowedAreasField(mech.playerSettings);
            if (allowed == null)
            {
                return;
            }

            foreach (KeyValuePair<Map, Area> pair in allowed)
            {
                if (pair.Key != null && pair.Value != null)
                {
                    result.Add(new MechFusionAllowedAreaSnapshot
                    {
                        map = pair.Key,
                        area = pair.Value
                    });
                }
            }
        }

        private static void RestoreAllowedAreas(
            Pawn mech,
            List<MechFusionAllowedAreaSnapshot> snapshots)
        {
            if (mech.playerSettings == null)
            {
                return;
            }

            ref Dictionary<Map, Area> allowed =
                ref AllowedAreasField(mech.playerSettings);
            allowed ??= new Dictionary<Map, Area>();
            allowed.Clear();
            for (int i = 0; i < snapshots.Count; i++)
            {
                MechFusionAllowedAreaSnapshot? snapshot = snapshots[i];
                if (snapshot?.map != null && snapshot.area != null)
                {
                    allowed[snapshot.map] = snapshot.area;
                }
            }
        }

        private static void ResolveRestoreFailure(
            MechFusionSession session,
            MechFusionControlledMechSnapshot snapshot,
            Pawn? overseer,
            int preferredIndex,
            string failureReason)
        {
            Pawn? mech = snapshot.pawn;
            if (mech != null && !mech.Destroyed && !mech.Discarded)
            {
                LogTransferDiagnostic(
                    session,
                    "解除合体回转",
                    overseer,
                    mech,
                    preferredIndex,
                    failureReason);
                TryDisconnectCompletely(mech);
                LogTransferFailure(mech);
            }

            snapshot.restoreResolved = true;
        }

        private static void MarkAllTransferFailures(
            MechFusionMechanitorSnapshot snapshot)
        {
            foreach (MechFusionControlledMechSnapshot mechSnapshot
                     in EnumerateMechs(snapshot))
            {
                if (mechSnapshot.transferFailed)
                {
                    continue;
                }

                mechSnapshot.transferFailed = true;
                Pawn? mech = mechSnapshot.pawn;
                if (mech != null && !mech.Destroyed && !mech.Discarded)
                {
                    TryDisconnectCompletely(mech);
                    LogTransferFailure(mech);
                }
            }

            snapshot.transferApplied = true;
        }

        private static void DisconnectAllOverseenBy(Pawn? overseer)
        {
            if (overseer?.mechanitor == null)
            {
                return;
            }

            List<Pawn> mechs = CollectAssignedAndOverseen(
                overseer.mechanitor);
            for (int i = 0; i < mechs.Count; i++)
            {
                TryDisconnectCompletely(mechs[i]);
            }
        }

        private static List<Pawn> CollectAssignedAndOverseen(
            Pawn_MechanitorTracker tracker)
        {
            var result = new List<Pawn>();
            var seen = new HashSet<Pawn>();
            List<MechanitorControlGroup>? groups = tracker.controlGroups;
            if (groups != null)
            {
                for (int i = 0; i < groups.Count; i++)
                {
                    List<AssignedMech>? assigned = groups[i]?.AssignedMechs;
                    if (assigned == null)
                    {
                        continue;
                    }

                    for (int j = 0; j < assigned.Count; j++)
                    {
                        Pawn? mech = assigned[j]?.pawn;
                        if (mech != null && seen.Add(mech))
                        {
                            result.Add(mech);
                        }
                    }
                }
            }

            List<Pawn>? overseen = tracker.OverseenPawns;
            if (overseen != null)
            {
                for (int i = 0; i < overseen.Count; i++)
                {
                    Pawn? mech = overseen[i];
                    if (mech != null && seen.Add(mech))
                    {
                        result.Add(mech);
                    }
                }
            }

            return result;
        }

        private static void TryDisconnectCompletely(Pawn mech)
        {
            try
            {
                DisconnectCompletely(mech);
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "[MAP-机械族机械师] 清理失败转移机械体监管状态时发生异常：" +
                    $"pawn={mech.ThingID}：{ex}");
            }
        }

        private static void DisconnectCompletely(Pawn mech)
        {
            List<Pawn> relatedOverseers = new List<Pawn>();
            List<DirectPawnRelation>? relations = mech.relations?.DirectRelations;
            if (relations != null)
            {
                for (int i = 0; i < relations.Count; i++)
                {
                    DirectPawnRelation relation = relations[i];
                    if (relation.def == PawnRelationDefOf.Overseer
                        && relation.otherPawn != null
                        && !relatedOverseers.Contains(relation.otherPawn))
                    {
                        relatedOverseers.Add(relation.otherPawn);
                    }
                }
            }

            for (int i = 0; i < relatedOverseers.Count; i++)
            {
                Pawn overseer = relatedOverseers[i];
                if (overseer.relations?.DirectRelationExists(
                        PawnRelationDefOf.Overseer,
                        mech) == true)
                {
                    overseer.relations.TryRemoveDirectRelation(
                        PawnRelationDefOf.Overseer,
                        mech);
                }
                else
                {
                    mech.relations?.TryRemoveDirectRelation(
                        PawnRelationDefOf.Overseer,
                        overseer);
                }

                overseer.mechanitor?.UnassignPawnFromAnyControlGroup(mech);
                overseer.mechanitor?.Notify_BandwidthChanged();
            }

            mech.OverseerSubject?.Notify_DisconnectedFromOverseer();
        }

        private static IEnumerable<MechFusionControlledMechSnapshot>
            EnumerateMechs(MechFusionMechanitorSnapshot snapshot)
        {
            for (int i = 0; i < snapshot.sourceGroups.Count; i++)
            {
                List<MechFusionControlledMechSnapshot> mechs =
                    snapshot.sourceGroups[i].mechs;
                for (int j = 0; j < mechs.Count; j++)
                {
                    if (mechs[j] != null)
                    {
                        yield return mechs[j];
                    }
                }
            }
        }

        private static bool HasAssignedMech(MechanitorControlGroup? group)
        {
            List<AssignedMech>? assigned = group?.AssignedMechs;
            if (assigned == null)
            {
                return false;
            }

            for (int i = 0; i < assigned.Count; i++)
            {
                if (assigned[i]?.pawn != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static Hediff? FindSynchronizationHediff(Pawn wearer)
        {
            HediffDef? def = GetSynchronizationDef();
            return def != null
                ? wearer.health?.hediffSet?.GetFirstHediffOfDef(def)
                : null;
        }

        private static HediffDef? GetSynchronizationDef()
        {
            return DefDatabase<HediffDef>.GetNamedSilentFail(
                MechFusionDefNames.MechControlSynchronizationHediffDefName);
        }

        private static bool TryGetActiveSnapshot(
            Pawn? pawn,
            out MechFusionMechanitorSnapshot? snapshot)
        {
            snapshot = null;
            if (!HasTemporaryMechanitorAccess(pawn)
                || !GameComponent_MechFusionSessionRegistry
                    .TryGetSessionForWearer(
                        pawn,
                        out MechFusionSession? session)
                || session?.MechanitorSnapshot == null
                || session.MechanitorSnapshot.controlsRestored)
            {
                return false;
            }

            snapshot = session.MechanitorSnapshot;
            return true;
        }

        private static void LogTransferFailure(Pawn mech)
        {
            Log.Warning($"{mech.ThingID}转移失败");
        }

        private static void LogTransferDiagnostic(
            MechFusionSession? session,
            string phase,
            Pawn? overseer,
            Pawn? mech,
            int preferredIndex,
            string failureReason)
        {
            try
            {
                Pawn_MechanitorTracker? tracker = overseer?.mechanitor;
                Pawn? actualOverseer = mech == null
                    ? null
                    : MAPOverseerRelationDirectionUtility.FindActualOverseer(mech);
                string preferredGroup = preferredIndex >= 0
                    ? (preferredIndex + 1).ToString()
                    : "n/a";
                string canOversee = tracker != null && mech != null
                    ? tracker.CanOverseeSubject(mech).ToString()
                    : "n/a";
                string bandwidthCost = mech != null
                    ? mech.GetStatValue(StatDefOf.BandwidthCost).ToString("0.##")
                    : "n/a";

                Log.Warning(
                    "[MAP-机械族机械师] 机控同调监管迁移诊断：" +
                    $"phase={phase}，session={session?.SessionId ?? "null"}，" +
                    $"overseer={FormatPawnForLog(overseer)}，" +
                    $"overseerFaction={overseer?.Faction?.def?.defName ?? "null"}，" +
                    $"overseerPlayerSafe={overseer?.Faction?.IsPlayerSafe() == true}，" +
                    $"mech={FormatPawnForLog(mech)}，" +
                    $"preferredGroup={preferredGroup}，" +
                    $"temporaryAccess={HasTemporaryMechanitorAccess(overseer)}，" +
                    $"isMechanitor={(overseer != null && MechanitorUtility.IsMechanitor(overseer))}，" +
                    $"tracker={(tracker != null)}，" +
                    $"controlGroups={tracker?.controlGroups?.Count.ToString() ?? "null"}，" +
                    $"totalBandwidth={tracker?.TotalBandwidth.ToString() ?? "n/a"}，" +
                    $"usedBandwidth={tracker?.UsedBandwidth.ToString() ?? "n/a"}，" +
                    $"bandwidthCost={bandwidthCost}，canOversee={canOversee}，" +
                    $"actualOverseer={FormatPawnForLog(actualOverseer)}，" +
                    $"mechFaction={mech?.Faction?.def?.defName ?? "null"}，" +
                    $"reason={failureReason}");
            }
            catch (Exception exception)
            {
                Log.Warning(
                    "[MAP-机械族机械师] 机控同调监管迁移诊断日志生成失败：" +
                    $"phase={phase}，reason={failureReason}，exception={exception}");
            }
        }

        private static string FormatPawnForLog(Pawn? pawn)
        {
            return pawn == null
                ? "null"
                : $"{pawn.LabelShort}（{pawn.ThingID}）";
        }

        private static int CountSnapshotMechs(
            MechFusionMechanitorSnapshot snapshot)
        {
            int count = 0;
            for (int i = 0; i < snapshot.sourceGroups.Count; i++)
            {
                count += snapshot.sourceGroups[i]?.mechs?.Count ?? 0;
            }

            return count;
        }

        private static void AuditTransferResults(
            MechFusionSession session,
            MechFusionMechanitorSnapshot snapshot,
            Pawn wearer,
            Pawn_MechanitorTracker tracker)
        {
            int expected = 0;
            int actual = 0;
            foreach (MechFusionControlledMechSnapshot mechSnapshot
                     in EnumerateMechs(snapshot))
            {
                Pawn? mech = mechSnapshot.pawn;
                if (mech == null
                    || mech.Destroyed
                    || mech.Discarded
                    || mech.Dead
                    || mechSnapshot.transferFailed)
                {
                    continue;
                }

                expected++;
                bool relationCorrect =
                    MAPOverseerRelationDirectionUtility.IsActualOverseerOf(
                        wearer,
                        mech);
                MechanitorControlGroup? group = tracker.GetControlGroup(mech);
                bool controlled = tracker.ControlledPawns?.Contains(mech) == true;
                bool factionCorrect = ReferenceEquals(mech.Faction, wearer.Faction);
                if (relationCorrect && group != null && controlled && factionCorrect)
                {
                    actual++;
                    continue;
                }

                int groupIndex = group == null || tracker.controlGroups == null
                    ? -1
                    : tracker.controlGroups.IndexOf(group);
                LogTransferDiagnostic(
                    session,
                    "合体接管后审计",
                    wearer,
                    mech,
                    groupIndex,
                    $"最终状态不一致：relationCorrect={relationCorrect}，" +
                    $"group={(group != null)}，controlled={controlled}，" +
                    $"factionCorrect={factionCorrect}，" +
                    $"wearerFaction={wearer.Faction?.def?.defName ?? "null"}。");
            }

            Log.Message(
                "[MAP-机械族机械师] 机控同调接管审计完成：" +
                $"session={session.SessionId}，expected={expected}，" +
                $"valid={actual}，failed={expected - actual}。");
        }
    }
}
