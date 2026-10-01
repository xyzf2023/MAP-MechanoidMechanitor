using System;
using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 通用形态记录的唯一管理入口。记录按需创建，因此不会给所有原版或第三方机械体
    /// 注入 ThingComp，也能覆盖后天升格为机械族机械师的 Pawn。
    /// </summary>
    public sealed class GameComponent_MechTransformationRegistry : GameComponent
    {
        private List<MechTransformationRecord> records =
            new List<MechTransformationRecord>();

        private Dictionary<Pawn, MechTransformationRecord>? recordByPawn;
        private Dictionary<string, MechTransformationRecord>? recordById;

        public GameComponent_MechTransformationRegistry(Game game)
        {
        }

        private static GameComponent_MechTransformationRegistry? CurrentRegistry
        {
            get
            {
                if (Current.Game == null)
                {
                    return null;
                }

                return Current.Game
                    .GetComponent<GameComponent_MechTransformationRegistry>();
            }
        }

        public static bool TryGetRecord(
            Pawn? pawn,
            out MechTransformationRecord? record)
        {
            record = null;
            GameComponent_MechTransformationRegistry? registry = CurrentRegistry;
            if (registry == null || pawn == null || pawn.Discarded)
            {
                return false;
            }

            registry.EnsureIndexes();
            return registry.recordByPawn!.TryGetValue(pawn, out record);
        }

        /// <summary>仅在读档安全阶段修复有效建筑形态的暂存身份，覆盖所有机械体。</summary>
        internal static void RestoreBuildingSourcesAfterLoad()
        {
            GameComponent_MechTransformationRegistry? registry = CurrentRegistry;
            if (registry == null)
                return;

            registry.EnsureIndexes();
            // 回调可能访问注册表；使用索引快照，重复执行不重建身份或能源数据。
            var snapshot = new List<MechTransformationRecord>(registry.recordByPawn!.Values);
            foreach (MechTransformationRecord record in snapshot)
            {
                Pawn? source = record.SourcePawn;
                Thing? carrier = record.ExternalCarrier;
                if (record.CurrentForm != MechTransformationForm.Building
                    || record.TransitionInProgress
                    || source == null || source.Spawned || source.health == null || source.Dead
                    || source.Destroyed || source.Discarded
                    || carrier == null || carrier.Destroyed || carrier.Discarded)
                    continue;

                CompMechFormCarrier? link = carrier.TryGetComp<CompMechFormCarrier>();
                CompMechBuildingForm? form = carrier.TryGetComp<CompMechBuildingForm>();
                if (link == null || !link.Matches(record) || form == null)
                    continue;

                if (form.StoredSourcePawn != null && !ReferenceEquals(form.StoredSourcePawn, source))
                {
                    Log.ErrorOnce("[MAP-机械族机械师] 建筑快照与形态注册表的源 Pawn 不一致，未改写暂存身份："
                        + carrier.ThingID, carrier.thingIDNumber ^ 0x4D42534D);
                    continue;
                }

                form.SourceState.EnsureStored(source, carrier.Faction);
                MechFusionSourceUtility.ApplyDormantGuard(source);
            }
        }

        public static MechTransformationRecord? GetOrCreateRecord(Pawn? pawn)
        {
            GameComponent_MechTransformationRegistry? registry = CurrentRegistry;
            if (registry == null
                || pawn == null
                || pawn.Destroyed
                || pawn.Discarded
                || pawn.Dead)
            {
                return null;
            }

            registry.EnsureIndexes();
            if (registry.recordByPawn!.TryGetValue(
                    pawn,
                    out MechTransformationRecord? existing))
            {
                return existing;
            }

            MechTransformationRecord created =
                new MechTransformationRecord(pawn);
            registry.records.Add(created);
            registry.recordByPawn[pawn] = created;
            registry.recordById![created.TransformationId] = created;
            return created;
        }

        public static bool TryBeginTransition(
            Pawn? pawn,
            MechTransformationForm targetForm,
            out MechTransformationRecord? record,
            out string? failureReason)
        {
            record = null;
            failureReason = null;
            if (pawn == null
                || pawn.Destroyed
                || pawn.Discarded
                || pawn.Dead)
            {
                failureReason = "MAP_MechanoidMechanitor.Transformation.SourceUnavailable".Translate();
                return false;
            }

            record = GetOrCreateRecord(pawn);
            if (record == null)
            {
                failureReason = "MAP_MechanoidMechanitor.Transformation.RegistryUnavailable".Translate();
                return false;
            }

            if (record.TransitionInProgress)
            {
                failureReason = "MAP_MechanoidMechanitor.Transformation.OtherTransition".Translate();
                return false;
            }

            if (record.CurrentForm == targetForm)
            {
                failureReason = "MAP_MechanoidMechanitor.Transformation.AlreadyTargetForm".Translate();
                return false;
            }

            if (record.CurrentForm != MechTransformationForm.Pawn
                && targetForm != MechTransformationForm.Pawn)
            {
                failureReason = "MAP_MechanoidMechanitor.Transformation.MustRestoreBeforeSwitch".Translate();
                return false;
            }

            Thing? carrier = record.ExternalCarrier;
            if (record.CurrentForm == MechTransformationForm.Pawn
                && carrier != null
                && !carrier.Destroyed)
            {
                failureReason = "MAP_MechanoidMechanitor.Transformation.ExistingCarrierLink".Translate();
                return false;
            }

            if (record.CurrentForm != MechTransformationForm.Pawn
                && (carrier == null || carrier.Destroyed))
            {
                failureReason = "MAP_MechanoidMechanitor.Transformation.CurrentCarrierMissing".Translate();
                return false;
            }

            if (!record.BeginTransition(targetForm))
            {
                failureReason = "MAP_MechanoidMechanitor.Transformation.TransitionLockFailed".Translate();
                return false;
            }

            return true;
        }

        /// <summary>
        /// 仅供载体已经损毁或丢失后的保底恢复使用。正常转换仍应走严格的
        /// TryBeginTransition，不能借此绕过有效载体核对。
        /// </summary>
        internal static bool TryBeginRecoveryToPawn(
            Pawn? pawn,
            Thing? expectedCarrier,
            out MechTransformationRecord? record,
            out string? failureReason)
        {
            record = null;
            failureReason = null;
            if (pawn == null || pawn.Destroyed || pawn.Discarded)
            {
                failureReason = "MAP_MechanoidMechanitor.Transformation.SourceUnavailable".Translate();
                return false;
            }

            if (!TryGetRecord(pawn, out record) || record == null)
            {
                failureReason = "MAP_MechanoidMechanitor.Transformation.RecordMissing".Translate();
                return false;
            }

            if (record.TransitionInProgress)
            {
                failureReason = "MAP_MechanoidMechanitor.Transformation.OtherTransition".Translate();
                return false;
            }

            if (record.CurrentForm == MechTransformationForm.Pawn)
            {
                failureReason = "MAP_MechanoidMechanitor.Transformation.AlreadyPawnForm".Translate();
                return false;
            }

            if (expectedCarrier != null
                && !ReferenceEquals(record.ExternalCarrier, expectedCarrier))
            {
                failureReason = "MAP_MechanoidMechanitor.Transformation.RecoveryCarrierMismatch".Translate();
                return false;
            }

            if (!record.BeginTransition(MechTransformationForm.Pawn))
            {
                failureReason = "MAP_MechanoidMechanitor.Transformation.RecoveryLockFailed".Translate();
                return false;
            }

            return true;
        }

        public static bool TryCommitTransition(
            Pawn? pawn,
            Thing? targetCarrier,
            out string? failureReason)
        {
            failureReason = null;
            if (!TryGetRecord(pawn, out MechTransformationRecord? record)
                || record == null
                || !record.TransitionInProgress)
            {
                failureReason = "MAP_MechanoidMechanitor.Transformation.NoPendingTransition".Translate();
                return false;
            }

            MechTransformationForm targetForm = record.PendingForm;
            if (targetForm == MechTransformationForm.Pawn)
            {
                if (targetCarrier != null)
                {
                    failureReason = "MAP_MechanoidMechanitor.Transformation.RestoreCannotAssignCarrier".Translate();
                    return false;
                }

                Thing? previousCarrier = record.ExternalCarrier;
                record.CommitTransition(MechTransformationForm.Pawn, null);
                previousCarrier?.TryGetComp<CompMechFormCarrier>()?.ClearLink();
                return true;
            }

            if (targetCarrier == null || targetCarrier.Destroyed)
            {
                failureReason = "MAP_MechanoidMechanitor.Transformation.TargetCarrierUnavailable".Translate();
                return false;
            }

            CompMechFormCarrier? carrierComp =
                targetCarrier.TryGetComp<CompMechFormCarrier>();
            if (carrierComp == null)
            {
                failureReason = "MAP_MechanoidMechanitor.Transformation.TargetCarrierMissingComp".Translate();
                return false;
            }

            if (!carrierComp.TryBind(
                    pawn!,
                    record.TransformationId,
                    targetForm,
                    out failureReason))
            {
                return false;
            }

            record.CommitTransition(targetForm, targetCarrier);
            return true;
        }

        public static bool TryCancelTransition(Pawn? pawn)
        {
            if (!TryGetRecord(pawn, out MechTransformationRecord? record)
                || record == null
                || !record.TransitionInProgress)
            {
                return false;
            }

            record.CancelTransition();
            return true;
        }

        /// <summary>
        /// 仅供合体异常收束使用：形态记录已经损坏（载体已销毁或丢失、恢复流程无法
        /// 通过严格入口）时，强行把记录恢复到 Pawn 形态并解除载体链接。
        /// 不创建、不移动、不恢复任何 Pawn 实例。
        /// </summary>
        internal static bool TryForceRestorePawnForm(Pawn? pawn)
        {
            if (!TryGetRecord(pawn, out MechTransformationRecord? record)
                || record == null)
            {
                return false;
            }

            if (record.TransitionInProgress)
            {
                record.CancelTransition();
            }

            Thing? previousCarrier = record.ExternalCarrier;
            if (record.CurrentForm != MechTransformationForm.Pawn
                || previousCarrier != null)
            {
                record.CommitTransition(MechTransformationForm.Pawn, null);
            }

            previousCarrier?.TryGetComp<CompMechFormCarrier>()?.ClearLink();
            return true;
        }

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                RemoveDiscardedRecords();
            }

            Scribe_Collections.Look(
                ref records,
                "mechTransformationRecords",
                LookMode.Deep);
            records ??= new List<MechTransformationRecord>();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                recordByPawn = null;
                recordById = null;
            }
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            EnsureIndexes();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            RebuildIndexes(validate: true);
        }

        private void EnsureIndexes()
        {
            if (recordByPawn == null || recordById == null)
            {
                RebuildIndexes(validate: false);
            }
        }

        internal static void NotifyPawnDiscarded(Pawn pawn)
        {
            if (pawn.Discarded)
            {
                CurrentRegistry?.RemoveDiscardedRecords();
            }
        }

        private void RemoveDiscardedRecords()
        {
            // 死亡或 Destroyed 的尸体仍可能复活，只有永久 Discarded 才注销。
            if (records.RemoveAll(record => record?.SourcePawn == null
                    || record.SourcePawn.Discarded) > 0)
            {
                recordByPawn = null;
                recordById = null;
            }
        }

        private void RebuildIndexes(bool validate)
        {
            recordByPawn = new Dictionary<Pawn, MechTransformationRecord>();
            recordById = new Dictionary<string, MechTransformationRecord>(
                StringComparer.Ordinal);

            for (int i = records.Count - 1; i >= 0; i--)
            {
                MechTransformationRecord? record = records[i];
                Pawn? pawn = record?.SourcePawn;
                if (record == null || pawn == null || pawn.Discarded)
                {
                    records.RemoveAt(i);
                    continue;
                }

                record.EnsureInitialized();
                if (recordByPawn.TryGetValue(pawn, out MechTransformationRecord? indexed))
                {
                    // 只删除全部持久字段相同的副本；不同身份/载体可能仍拥有恢复凭据，
                    // 无法仅凭列表顺序判断哪份权威，不擅自断开链接或删除其数据。
                    if (record.TransformationId == indexed.TransformationId
                        && record.CurrentForm == indexed.CurrentForm
                        && ReferenceEquals(record.ExternalCarrier, indexed.ExternalCarrier)
                        && record.TransitionInProgress == indexed.TransitionInProgress
                        && record.PendingForm == indexed.PendingForm)
                    {
                        records.RemoveAt(i);
                        continue;
                    }
                    Log.Error(
                        "[MAP-机械族机械师] 发现同一 Pawn 的重复形态记录，" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}）。已保留当前索引中的记录，重复项未自动删除。");
                    continue;
                }

                if (recordById.ContainsKey(record.TransformationId))
                {
                    if (record.CurrentForm == MechTransformationForm.Pawn
                        && (record.ExternalCarrier == null
                            || record.ExternalCarrier.Destroyed))
                    {
                        record.RegenerateIdentity();
                    }
                    else
                    {
                        Log.Error(
                            "[MAP-机械族机械师] 发现外部形态使用重复身份ID，" +
                            $"pawn={pawn.LabelShort}（{pawn.ThingID}），" +
                            $"identity={record.TransformationId}。为避免断开载体链接，未自动改写。");
                        continue;
                    }
                }

                recordByPawn[pawn] = record;
                recordById[record.TransformationId] = record;
            }

            // 先比较原始持久字段并完成索引，再取消过渡锁/清除已毁载体。
            // 否则先校验的记录会与尚未校验的相同副本呈现不同状态。
            if (validate)
            {
                foreach (MechTransformationRecord record in recordByPawn.Values)
                {
                    ValidateLoadedRecord(record);
                }
            }
        }

        private static void ValidateLoadedRecord(MechTransformationRecord record)
        {
            Pawn? pawn = record.SourcePawn;
            if (pawn == null)
            {
                return;
            }

            if (record.TransitionInProgress)
            {
                record.CancelTransition();
                Log.Warning(
                    "[MAP-机械族机械师] 读取存档时发现未完成的形态转换锁，" +
                    $"已回退到最近一次稳定形态：pawn={pawn.LabelShort}（{pawn.ThingID}）。");
            }

            record.ClearDestroyedCarrier();
            Thing? carrier = record.ExternalCarrier;
            if (record.CurrentForm == MechTransformationForm.Pawn)
            {
                if (carrier != null)
                {
                    Log.Error(
                        "[MAP-机械族机械师] Pawn 形态仍链接到有效外部载体，" +
                        $"已保留双方数据等待专用恢复流程处理：pawn={pawn.LabelShort}（{pawn.ThingID}）。");
                }

                return;
            }

            if (carrier == null)
            {
                if (record.CurrentForm == MechTransformationForm.Building
                    && GameComponent_MechBuildingConversionQueue.HasPendingRecovery(record))
                {
                    return;
                }

                Log.Error(
                    "[MAP-机械族机械师] 外部形态缺少载体，原始 Pawn 数据仍被保留：" +
                    $"pawn={pawn.LabelShort}（{pawn.ThingID}），form={record.CurrentForm}。");
                return;
            }

            CompMechFormCarrier? carrierComp =
                carrier.TryGetComp<CompMechFormCarrier>();
            if (carrierComp == null || !carrierComp.Matches(record))
            {
                Log.Error(
                    "[MAP-机械族机械师] 外部形态载体链接不一致，未自动删除任何对象：" +
                    $"pawn={pawn.LabelShort}（{pawn.ThingID}），" +
                    $"form={record.CurrentForm}，carrier={carrier.ThingID}。");
            }

            if (pawn.Spawned)
            {
                Log.Error(
                    "[MAP-机械族机械师] 原始 Pawn 与外部形态载体同时处于活动状态，" +
                    $"未自动删除任何对象：pawn={pawn.LabelShort}（{pawn.ThingID}），" +
                    $"form={record.CurrentForm}。");
            }
        }
    }
}
