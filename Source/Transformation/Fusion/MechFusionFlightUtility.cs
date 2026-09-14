using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// session 级临时飞行资格。源机械族通过统一能力入口或飞行授权注册表
    /// 拥有飞行时，合体期间目标人类可以飞行；解除合体与读档修复时撤销。
    /// 不写入目标人类永久 Flight 能力，不伪造先天飞行记录。
    /// 授权清理不依赖 wearer 是否可用：只要注册表记录存在就必须撤销本次合体
    /// 创建的那一条，不能留下永久残留，也不能误撤销其他来源的授权。
    /// </summary>
    internal static class MechFusionFlightUtility
    {
        internal static bool SourceGrantsFlight(Pawn? source)
        {
            return source != null
                && (MechanoidMechanitorCapabilityUtility.HasCapability(
                        source,
                        MechanoidMechanitorCapability.Flight)
                    || GameComponent_MechanicalFlightRegistry.IsAuthorized(source));
        }

        internal static void ApplyTemporaryFlight(
            MechFusionSession session,
            Pawn source,
            Pawn wearer)
        {
            if (session == null || source == null || wearer == null)
            {
                return;
            }

            if (!SourceGrantsFlight(source))
            {
                session.SetTemporaryFlightState(false, false);
                return;
            }

            bool grantedByFusion = false;
            if (!GameComponent_MechanicalFlightRegistry.HasAuthorizationRecord(wearer)
                && GameComponent_MechanicalFlightRegistry.TryAuthorize(
                    wearer,
                    source: MechanicalFlightAuthorizationSource.TemporaryFusion))
            {
                grantedByFusion = true;
            }

            session.SetTemporaryFlightState(true, grantedByFusion);
        }

        /// <summary>
        /// 返回 true 表示本次合体创建的临时飞行授权已经无需再处理：
        /// 未由本次合体创建、记录已不存在、或已成功撤销。
        /// 返回 false 表示飞行记录仍在活动状态，必须等待下一轮重试。
        /// </summary>
        internal static bool TryRevokeTemporaryFlight(MechFusionSession session)
        {
            if (session == null || !session.FlightAuthorizationGrantedByFusion)
            {
                return true;
            }

            Pawn? wearer = session.WearerPawn;
            if (wearer == null)
            {
                session.SetTemporaryFlightState(
                    session.TemporaryFlightAuthorized,
                    false);
                return true;
            }

            if (!GameComponent_MechanicalFlightRegistry.HasAuthorizationRecord(wearer))
            {
                session.SetTemporaryFlightState(
                    session.TemporaryFlightAuthorized,
                    false);
                return true;
            }

            if (!wearer.Destroyed
                && !wearer.Discarded
                && MechanicalFlightUtility.IsAirborne(wearer))
            {
                MechanicalFlightUtility.TryBeginLanding(wearer);
                if (MechanicalFlightUtility.IsAirborne(wearer))
                {
                    // 保留授权与标记，等待统一解除流程在地面或坠毁结束后再次撤销。
                    return false;
                }
            }

            bool revoked;
            if (GameComponent_MechanicalFlightRegistry.HasAuthorizationSource(
                    wearer,
                    MechanicalFlightAuthorizationSource.TemporaryFusion))
            {
                revoked = GameComponent_MechanicalFlightRegistry
                    .TryRemoveAuthorizationSource(
                        wearer,
                        MechanicalFlightAuthorizationSource.TemporaryFusion);
            }
            else
            {
                // 兼容旧存档：旧版合体临时记录没有来源字段，但会话能证明其归属。
                revoked = GameComponent_MechanicalFlightRegistry
                    .TryRevokeAuthorization(wearer);
            }

            if (!revoked
                && GameComponent_MechanicalFlightRegistry
                    .HasAuthorizationRecord(wearer))
            {
                return false;
            }

            session.SetTemporaryFlightState(
                session.TemporaryFlightAuthorized,
                false);
            return true;
        }

        /// <summary>
        /// 无有效飞行记录却仍处于原版飞行状态时的保底收尾，避免 Pawn
        /// 永久停留在“无飞行记录的空中”。不做任何爆炸或塌方结算。
        /// </summary>
        internal static void EnsureVanillaFlightEnds(Pawn? pawn)
        {
            if (pawn?.flight?.Flying != true)
            {
                return;
            }

            pawn.pather?.StopDead();
            MechanicalFlightStraightPathPatch.ClearMotion(pawn);
            pawn.flight.ForceLand();
        }

        internal static bool RequiresImmediateCrash(MechFusionExitReason reason)
        {
            return reason == MechFusionExitReason.EnergyDepleted
                || reason == MechFusionExitReason.StabilityDepleted
                || reason == MechFusionExitReason.HumanDeathOrDowned
                || reason == MechFusionExitReason.ApparelLost
                || reason == MechFusionExitReason.LoadRepair;
        }

        internal static void RepairAfterLoad(MechFusionSession session)
        {
            if (session == null)
            {
                return;
            }

            Pawn? wearer = session.WearerPawn;
            Pawn? source = session.SourcePawn;
            if (wearer == null || source == null)
            {
                return;
            }

            if (!session.TemporaryFlightAuthorized)
            {
                TryRevokeTemporaryFlight(session);
                return;
            }

            if (!session.FlightAuthorizationGrantedByFusion)
            {
                // 授权原本属于 wearer 自身或其他来源，绝不能撤销。
                return;
            }

            if (GameComponent_MechanicalFlightRegistry.HasAuthorizationRecord(wearer))
            {
                return;
            }

            if (SourceGrantsFlight(source)
                && GameComponent_MechanicalFlightRegistry.TryAuthorize(
                    wearer,
                    source: MechanicalFlightAuthorizationSource.TemporaryFusion))
            {
                session.SetTemporaryFlightState(true, true);
                return;
            }

            // 注册表中已经不存在本次合体创建的授权，无需也无法再撤销。
            session.SetTemporaryFlightState(false, false);
        }
    }
}
