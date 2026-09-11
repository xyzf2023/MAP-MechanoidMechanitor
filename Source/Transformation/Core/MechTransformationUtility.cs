using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 供合体、建筑转换及后续兼容层共用的只读查询入口。
    /// </summary>
    public static class MechTransformationUtility
    {
        public static bool IsInPawnForm(Pawn? pawn)
        {
            return !GameComponent_MechTransformationRegistry.TryGetRecord(
                    pawn,
                    out MechTransformationRecord? record)
                || record == null
                || (!record.TransitionInProgress
                    && record.CurrentForm == MechTransformationForm.Pawn);
        }

        public static bool IsTransitionInProgress(Pawn? pawn)
        {
            return GameComponent_MechTransformationRegistry.TryGetRecord(
                    pawn,
                    out MechTransformationRecord? record)
                && record?.TransitionInProgress == true;
        }

        public static bool TryResolveSourcePawn(
            Thing? thing,
            out Pawn? sourcePawn)
        {
            sourcePawn = null;
            if (thing is Pawn pawn)
            {
                sourcePawn = pawn;
                return true;
            }

            CompMechFormCarrier? carrier =
                thing?.TryGetComp<CompMechFormCarrier>();
            if (carrier?.Committed != true
                || carrier.SourcePawn == null
                || carrier.SourcePawn.Discarded)
            {
                return false;
            }

            sourcePawn = carrier.SourcePawn;
            return true;
        }

        public static bool TryGetCurrentRepresentation(
            Pawn? pawn,
            out Thing? representation)
        {
            representation = null;
            if (pawn == null || pawn.Discarded)
            {
                return false;
            }

            if (!GameComponent_MechTransformationRegistry.TryGetRecord(
                    pawn,
                    out MechTransformationRecord? record)
                || record == null
                || record.CurrentForm == MechTransformationForm.Pawn)
            {
                if (pawn.Destroyed)
                {
                    return false;
                }

                representation = pawn;
                return true;
            }

            Thing? carrier = record.ExternalCarrier;
            if (carrier == null || carrier.Destroyed)
            {
                return false;
            }

            representation = carrier;
            return true;
        }
    }
}
