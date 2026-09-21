using System;
using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 将能力/按钮请求延迟到下一 tick，避免在能力 Job 或建筑销毁回调中直接替换实体。
    /// 按钮请求是短暂运行态；建筑销毁后的恢复请求保存完整快照，直到恢复成功。
    /// </summary>
    public sealed class GameComponent_MechBuildingConversionQueue : GameComponent
    {
        private readonly HashSet<Pawn> PendingConversions =
            new HashSet<Pawn>();
        private readonly HashSet<Thing> PendingRestores =
            new HashSet<Thing>();
        private List<MechBuildingEmergencyRestoreRecord> PendingEmergencyRestores =
            new List<MechBuildingEmergencyRestoreRecord>();

        public GameComponent_MechBuildingConversionQueue(Game game)
        {
        }

        public static bool TryQueueConversion(
            Pawn? pawn,
            out string? failureReason)
        {
            if (!MechBuildingConversionService.CanConvert(
                    pawn,
                    out failureReason))
            {
                return false;
            }

            GameComponent_MechBuildingConversionQueue? queue =
                Current.Game?.GetComponent<GameComponent_MechBuildingConversionQueue>();
            if (queue == null)
            {
                failureReason = "建筑形态转换队列不可用。";
                return false;
            }

            queue.PendingConversions.Add(pawn!);
            return true;
        }

        public static bool TryQueueRestore(
            Thing? carrier,
            out string? failureReason)
        {
            if (!MechBuildingConversionService.CanRestore(
                    carrier,
                    allowDestroyedCarrier: false,
                    out failureReason))
            {
                return false;
            }

            GameComponent_MechBuildingConversionQueue? queue =
                Current.Game?.GetComponent<GameComponent_MechBuildingConversionQueue>();
            if (queue == null)
            {
                failureReason = "建筑形态转换队列不可用。";
                return false;
            }

            queue.PendingRestores.Add(carrier!);
            return true;
        }

        public static void QueueEmergencyRestore(
            Thing carrier,
            Pawn sourcePawn,
            Map map,
            IntVec3 position,
            Rot4 rotation)
        {
            if (carrier == null || sourcePawn == null || map == null)
            {
                return;
            }

            GameComponent_MechBuildingConversionQueue? queue =
                Current.Game?.GetComponent<GameComponent_MechBuildingConversionQueue>();
            if (queue == null)
            {
                return;
            }

            for (int i = 0; i < queue.PendingEmergencyRestores.Count; i++)
            {
                MechBuildingEmergencyRestoreRecord existing = queue.PendingEmergencyRestores[i];
                if (ReferenceEquals(existing.SourcePawn, sourcePawn))
                {
                    return;
                }
            }

            CompMechBuildingForm? buildingState = carrier.TryGetComp<CompMechBuildingForm>();
            if (buildingState == null
                || !GameComponent_MechTransformationRegistry.TryGetRecord(sourcePawn, out MechTransformationRecord? record)
                || record == null
                || record.CurrentForm != MechTransformationForm.Building
                || !ReferenceEquals(record.ExternalCarrier, carrier))
            {
                return;
            }

            buildingState.EnsureSourceStateForRecovery(sourcePawn);
            bool hasDurability = buildingState.TryGetDurabilityFractions(
                true, out float initialFraction, out float currentFraction);
            queue.PendingEmergencyRestores.Add(new MechBuildingEmergencyRestoreRecord
            {
                SourceState = buildingState.SourceState.CreateCopy(),
                TransformationId = record.TransformationId,
                CarrierId = carrier.thingIDNumber,
                Map = map,
                Position = position,
                Rotation = rotation,
                HasDurabilitySnapshot = hasDurability,
                InitialHealthFraction = initialFraction,
                CurrentHealthFraction = currentFraction,
                InitialRepairableDamage = buildingState.InitialRepairableDamage
            });
        }

        internal static bool HasPendingRecovery(MechTransformationRecord record)
        {
            GameComponent_MechBuildingConversionQueue? queue =
                Current.Game?.GetComponent<GameComponent_MechBuildingConversionQueue>();
            return queue?.PendingEmergencyRestores.Exists(entry =>
                ReferenceEquals(entry.SourcePawn, record.SourcePawn)
                && entry.TransformationId == record.TransformationId) == true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref PendingEmergencyRestores, "pendingBuildingEmergencyRestores", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                PendingEmergencyRestores ??= new List<MechBuildingEmergencyRestoreRecord>();
                PendingEmergencyRestores.RemoveAll(entry => entry?.SourcePawn == null || entry.SourcePawn.Discarded);
            }
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            ClearQueues();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            PendingConversions.Clear();
            PendingRestores.Clear();
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            if (PendingConversions.Count == 0
                && PendingRestores.Count == 0
                && PendingEmergencyRestores.Count == 0)
            {
                return;
            }

            Pawn[] conversions = new Pawn[PendingConversions.Count];
            PendingConversions.CopyTo(conversions);
            PendingConversions.Clear();
            for (int i = 0; i < conversions.Length; i++)
            {
                MechBuildingConversionService.TryConvert(
                    conversions[i],
                    sendFailureMessage: true);
            }

            Thing[] restores = new Thing[PendingRestores.Count];
            PendingRestores.CopyTo(restores);
            PendingRestores.Clear();
            for (int i = 0; i < restores.Length; i++)
            {
                MechBuildingConversionService.TryRestore(
                    restores[i],
                    sendFailureMessage: true);
            }

            MechBuildingEmergencyRestoreRecord[] emergencyRestores =
                PendingEmergencyRestores.ToArray();
            for (int i = 0; i < emergencyRestores.Length; i++)
            {
                MechBuildingEmergencyRestoreRecord entry = emergencyRestores[i];
                int now = Find.TickManager.TicksGame;
                if (now < entry.NextAttemptTick)
                {
                    continue;
                }

                entry.NextAttemptTick = now + 60;
                try
                {
                    if (MechBuildingConversionService.TryEmergencyRestore(entry))
                    {
                        PendingEmergencyRestores.Remove(entry);
                    }
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[MAP-机械族机械师] 建筑紧急恢复异常，已保留恢复快照：" + ex,
                        entry.CarrierId ^ 0x4D424552);
                }
            }
        }

        private void ClearQueues()
        {
            PendingConversions.Clear();
            PendingRestores.Clear();
            PendingEmergencyRestores.Clear();
        }
    }
}
