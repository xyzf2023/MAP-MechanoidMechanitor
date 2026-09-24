using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor
{
    /// <summary>通用 Pawn 室内产热；复用 CompProperties_HeatPusher 配置与 enabled 存档。</summary>
    public sealed class CompPawnHeatPusher : CompHeatPusher
    {
        public override bool ShouldPushHeatNow
        {
            get
            {
                // 必须是实际生成的 Pawn，不能通过建筑、运输容器或尸体向外产热。
                if (!(parent is Pawn pawn) || !pawn.Spawned || pawn.Destroyed
                    || pawn.Dead || pawn.Downed || pawn.Suspended || pawn.health == null)
                    return false;

                if (!pawn.Awake() || pawn.IsSelfShutdown() || pawn.IsDeactivated()
                    || MechanoidMechanitorSelfWorkModeUtility.IsSelfShutdown(pawn)
                    || pawn.TryGetComp<CompCanBeDormant>()?.Awake == false
                    || pawn.GetLord()?.CurLordToil is LordToil_Sleep)
                    return false;

                Room? room = pawn.Position.GetRoom(pawn.Map);
                return room != null && !room.UsesOutdoorTemperature && base.ShouldPushHeatNow;
            }
        }

        public override void CompTick()
        {
            // 不调用基类产热入口：需按剩余温差限量，保证本组件不会越过温度上限。
            if (!parent.IsHashIntervalTick(60) || !ShouldPushHeatNow) return;

            Room? room = parent.Position.GetRoom(parent.Map);
            if (room == null || room.UsesOutdoorTemperature || room.CellCount <= 0) return;

            float remainingHeat = (Props.heatPushMaxTemperature - room.Temperature) * room.CellCount;
            float heat = Mathf.Min(Props.heatPerSecond, remainingHeat);
            if (heat > 0f) room.PushHeat(heat);
        }

        public override void CompTickRare()
        {
            // Pawn 每 250 tick 还会主动调用此入口；只由 CompTick 结算，避免双倍产热。
        }
    }
}
