using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class StatPart_ProductivityCoreWorkSpeedOffset : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            if (TryGetOffset(req, out float offset))
            {
                val += offset;
            }
        }

        public override string ExplanationPart(StatRequest req)
        {
            return TryGetOffset(req, out float offset)
                ? "MAP_MechanoidMechanitor.Implants.ProductivityCore.WorkSpeedOffset"
                    .Translate(offset.ToStringPercent())
                : null!;
        }

        private static bool TryGetOffset(StatRequest req, out float offset)
        {
            offset = 0f;
            if (!ModsConfig.BiotechActive
                || !req.HasThing
                || req.Thing is not Pawn pawn)
            {
                return false;
            }

            int level = ProductivityCoreUtility.GetLevel(pawn);
            if (level <= 0)
            {
                return false;
            }

            offset = level * ProductivityCoreUtility.WorkSpeedOffsetPerLevel;
            return offset != 0f;
        }
    }
}
