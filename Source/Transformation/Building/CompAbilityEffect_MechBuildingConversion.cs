using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 能力层只负责发出请求；实际收存 Pawn 和生成建筑会延迟到下一游戏 tick。
    /// </summary>
    public sealed class CompAbilityEffect_MechBuildingConversion : CompAbilityEffect
    {
        public override bool GizmoDisabled(out string reason)
        {
            if (!MechBuildingConversionService.CanConvert(
                    parent.pawn,
                    out string? failureReason))
            {
                reason = failureReason
                    ?? "MAP_MechanoidMechanitor.Transformation.Building.Unavailable".Translate();
                return true;
            }

            reason = string.Empty;
            return false;
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn? pawn = parent.pawn;
            if (!GameComponent_MechBuildingConversionQueue
                    .TryQueueConversion(pawn, out string? failureReason))
            {
                Messages.Message(
                    failureReason
                        ?? "MAP_MechanoidMechanitor.Transformation.Building.Unavailable".Translate(),
                    pawn,
                    MessageTypeDefOf.RejectInput,
                    historical: false);
            }
        }
    }
}
