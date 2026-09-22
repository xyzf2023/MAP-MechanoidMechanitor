using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 指挥距离的只读解析。来源保留在原组件/注册表，不缓存关系派生能力。
    /// 跨地图指监管者与机械体可不同图；指令目标必须仍在机械体所在地图。
    /// </summary>
    public static class MechCommandRangeUtility
    {
        private const MechanoidMechanitorCapability Unlimited =
            MechanoidMechanitorCapability.CommandRangeBypass | MechanoidMechanitorCapability.CrossMapCommand;

        internal static MechanoidMechanitorCapability ResolveCapabilities(Pawn pawn)
        {
            if (!ModsConfig.BiotechActive || pawn.health == null || pawn.Dead || pawn.Destroyed
                || pawn.RaceProps?.IsMechanoid != true || pawn.Faction?.IsPlayerSafe() != true)
                return MechanoidMechanitorCapability.None;

            if (AutonomousMechUtility.IsPlayerAutonomousMech(pawn)
                || DataProcessingAllocationUtility.HasCommandRangeBypass(pawn))
                return Unlimited;

            Pawn? overseer = MAPOverseerRelationDirectionUtility.FindActualOverseer(pawn);
            if (overseer == null || overseer.Destroyed || overseer.Dead
                || overseer.Faction != pawn.Faction)
                return MechanoidMechanitorCapability.None;

            if (QuantumCommunicatorUtility.HasEffect(overseer))
                return Unlimited;

            // 隐者型先天豁免只有在实际受控时生效；超带宽仍由受控名单决定。
            if (overseer.mechanitor?.ControlledPawns?.Contains(pawn) != true)
                return MechanoidMechanitorCapability.None;

            CompMechCommandRange? comp = pawn.GetComp<CompMechCommandRange>();
            MechanoidMechanitorCapability result = MechanoidMechanitorCapability.None;
            if (comp != null)
                result |= MechanoidMechanitorCapability.CommandRangeBypass
                    | (comp.Props.allowCrossMapCommand ? MechanoidMechanitorCapability.CrossMapCommand : 0);

            // 兼容尚未迁移的第三方 XML。内置 Def 已改为独立范围组件。
            if (pawn.GetComp<CompMAPMechanitorNode>()?.NodeProps?.ignoreExternalOverseerCommandRange == true)
                result |= Unlimited;
            return result;
        }

        public static bool TryEvaluate(Pawn? mech, LocalTargetInfo target, out bool inRange)
        {
            inRange = false;
            if (mech == null || !ModsConfig.BiotechActive || mech.RaceProps?.IsMechanoid != true
                || mech.Faction?.IsPlayerSafe() != true)
                return false;

            Map? targetMap = mech.MapHeld;
            if (targetMap == null || !target.IsValid || !target.Cell.InBounds(targetMap)
                || (target.HasThing && target.Thing.MapHeld != targetMap))
                return true;

            if (MechanoidMechanitorCapabilityUtility.HasCapability(
                mech, MechanoidMechanitorCapability.CommandRangeBypass))
            {
                Pawn? overseer = MAPOverseerRelationDirectionUtility.FindActualOverseer(mech);
                inRange = overseer == null || overseer.MapHeld == targetMap
                    || MechanoidMechanitorCapabilityUtility.HasCapability(
                        mech, MechanoidMechanitorCapability.CrossMapCommand);
                return true;
            }

            // 有限距离的代理位置只在没有距离豁免时使用，不能覆盖隐者的先天豁免。
            if (ProxySubchainUtility.TryGetHeldCommandOrigin(mech, out Map? commandMap, out IntVec3 origin))
            {
                inRange = commandMap == targetMap && origin.DistanceToSquared(target.Cell) < 620.01f;
                return true;
            }
            return false;
        }
    }
}
