using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 将能力/按钮请求延迟到下一 tick，避免在能力 Job 或建筑销毁回调中直接替换实体。
    /// 队列是短暂运行态，不写入存档；真正的稳定状态由形态记录表保存。
    /// </summary>
    public sealed class GameComponent_MechBuildingConversionQueue : GameComponent
    {
        private sealed class PendingEmergencyRestore
        {
            public Thing Carrier = null!;
            public Pawn SourcePawn = null!;
            public Map Map = null!;
            public IntVec3 Position;
            public Rot4 Rotation;
        }

        private static readonly HashSet<Pawn> PendingConversions =
            new HashSet<Pawn>();
        private static readonly HashSet<Thing> PendingRestores =
            new HashSet<Thing>();
        private static readonly List<PendingEmergencyRestore> PendingEmergencyRestores =
            new List<PendingEmergencyRestore>();

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

            PendingConversions.Add(pawn!);
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

            PendingRestores.Add(carrier!);
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

            for (int i = 0; i < PendingEmergencyRestores.Count; i++)
            {
                PendingEmergencyRestore existing = PendingEmergencyRestores[i];
                if (ReferenceEquals(existing.Carrier, carrier)
                    || ReferenceEquals(existing.SourcePawn, sourcePawn))
                {
                    return;
                }
            }

            PendingEmergencyRestores.Add(new PendingEmergencyRestore
            {
                Carrier = carrier,
                SourcePawn = sourcePawn,
                Map = map,
                Position = position,
                Rotation = rotation
            });
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            ClearQueues();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            ClearQueues();
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

            PendingEmergencyRestore[] emergencyRestores =
                PendingEmergencyRestores.ToArray();
            PendingEmergencyRestores.Clear();
            for (int i = 0; i < emergencyRestores.Length; i++)
            {
                PendingEmergencyRestore entry = emergencyRestores[i];
                MechBuildingConversionService.TryEmergencyRestore(
                    entry.Carrier,
                    entry.SourcePawn,
                    entry.Map,
                    entry.Position,
                    entry.Rotation);
            }
        }

        private static void ClearQueues()
        {
            PendingConversions.Clear();
            PendingRestores.Clear();
            PendingEmergencyRestores.Clear();
        }
    }
}
