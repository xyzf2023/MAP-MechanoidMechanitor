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
            if (registry == null || pawn == null)
            {
                return false;
            }

            registry.EnsureIndexes();
            return registry.recordByPawn!.TryGetValue(pawn, out record);
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
                failureReason = "原始机械体不可用。";
                return false;
            }

            record = GetOrCreateRecord(pawn);
            if (record == null)
            {
                failureReason = "形态记录管理器不可用。";
                return false;
            }

            if (record.TransitionInProgress)
            {
                failureReason = "机械体正在进行另一项形态转换。";
                return false;
            }

            if (record.CurrentForm == targetForm)
            {
                failureReason = "机械体已经处于目标形态。";
                return false;
            }

            if (record.CurrentForm != MechTransformationForm.Pawn
                && targetForm != MechTransformationForm.Pawn)
            {
                failureReason = "必须先恢复为机械体形态，才能切换到另一种外部形态。";
                return false;
            }

            Thing? carrier = record.ExternalCarrier;
            if (record.CurrentForm == MechTransformationForm.Pawn
                && carrier != null
                && !carrier.Destroyed)
            {
                failureReason = "机械体仍链接到一个外部形态载体。";
                return false;
            }

            if (record.CurrentForm != MechTransformationForm.Pawn
                && (carrier == null || carrier.Destroyed))
            {
                failureReason = "当前形态缺少有效的外部载体。";
                return false;
            }

            if (!record.BeginTransition(targetForm))
            {
                failureReason = "无法锁定本次形态转换。";
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
                failureReason = "原始机械体不可用。";
                return false;
            }

            if (!TryGetRecord(pawn, out record) || record == null)
            {
                failureReason = "不存在可恢复的形态记录。";
                return false;
            }

            if (record.TransitionInProgress)
            {
                failureReason = "机械体正在进行另一项形态转换。";
                return false;
            }

            if (record.CurrentForm == MechTransformationForm.Pawn)
            {
                failureReason = "机械体已经处于 Pawn 形态。";
                return false;
            }

            if (expectedCarrier != null
                && !ReferenceEquals(record.ExternalCarrier, expectedCarrier))
            {
                failureReason = "待恢复载体与形态记录不一致。";
                return false;
            }

            if (!record.BeginTransition(MechTransformationForm.Pawn))
            {
                failureReason = "无法锁定本次紧急恢复。";
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
                failureReason = "不存在等待提交的形态转换。";
                return false;
            }

            MechTransformationForm targetForm = record.PendingForm;
            if (targetForm == MechTransformationForm.Pawn)
            {
                if (targetCarrier != null)
                {
                    failureReason = "恢复机械体形态时不应指定新的外部载体。";
                    return false;
                }

                Thing? previousCarrier = record.ExternalCarrier;
                record.CommitTransition(MechTransformationForm.Pawn, null);
                previousCarrier?.TryGetComp<CompMechFormCarrier>()?.ClearLink();
                return true;
            }

            if (targetCarrier == null || targetCarrier.Destroyed)
            {
                failureReason = "目标形态载体不可用。";
                return false;
            }

            CompMechFormCarrier? carrierComp =
                targetCarrier.TryGetComp<CompMechFormCarrier>();
            if (carrierComp == null)
            {
                failureReason = "目标载体缺少通用形态链接组件。";
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

        private void RebuildIndexes(bool validate)
        {
            recordByPawn = new Dictionary<Pawn, MechTransformationRecord>();
            recordById = new Dictionary<string, MechTransformationRecord>(
                StringComparer.Ordinal);

            for (int i = records.Count - 1; i >= 0; i--)
            {
                MechTransformationRecord? record = records[i];
                Pawn? pawn = record?.SourcePawn;
                if (record == null || pawn == null)
                {
                    records.RemoveAt(i);
                    continue;
                }

                record.EnsureInitialized();
                if (recordByPawn.ContainsKey(pawn))
                {
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

                if (validate)
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
