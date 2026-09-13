using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// session 级临时飞行资格。源机械族通过统一能力入口或飞行授权注册表
    /// 拥有飞行时，合体期间目标人类可以飞行；解除合体与读档修复时撤销。
    /// 不写入目标人类永久 Flight 能力，不伪造先天飞行记录。
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
                && GameComponent_MechanicalFlightRegistry.TryAuthorize(wearer))
            {
                grantedByFusion = true;
            }

            session.SetTemporaryFlightState(true, grantedByFusion);
        }

        internal static void RevokeTemporaryFlight(MechFusionSession session)
        {
            if (session == null || !session.FlightAuthorizationGrantedByFusion)
            {
                return;
            }

            Pawn? wearer = session.WearerPawn;
            if (wearer == null || wearer.Destroyed || wearer.Discarded)
            {
                session.SetTemporaryFlightState(
                    session.TemporaryFlightAuthorized,
                    false);
                return;
            }

            if (!GameComponent_MechanicalFlightRegistry.HasAuthorizationRecord(wearer))
            {
                session.SetTemporaryFlightState(
                    session.TemporaryFlightAuthorized,
                    false);
                return;
            }

            if (MechanicalFlightUtility.IsAirborne(wearer))
            {
                MechanicalFlightUtility.TryBeginLanding(wearer);
                if (MechanicalFlightUtility.IsAirborne(wearer))
                {
                    // 保留授权与标记，等待统一解除流程在地面后再次撤销。
                    return;
                }
            }

            GameComponent_MechanicalFlightRegistry.TryRevokeAuthorization(wearer);
            session.SetTemporaryFlightState(
                session.TemporaryFlightAuthorized,
                false);
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

            if (session.TemporaryFlightAuthorized)
            {
                if (!GameComponent_MechanicalFlightRegistry.HasAuthorizationRecord(
                        wearer)
                    && SourceGrantsFlight(source)
                    && GameComponent_MechanicalFlightRegistry.TryAuthorize(wearer))
                {
                    session.SetTemporaryFlightState(true, true);
                }

                return;
            }

            RevokeTemporaryFlight(session);
        }
    }
}
