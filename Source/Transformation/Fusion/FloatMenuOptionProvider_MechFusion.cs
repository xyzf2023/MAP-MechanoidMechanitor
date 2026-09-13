using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体的双向右键入口：
    /// 选中有资格的机械族右键合法人类，或选中合法人类右键有资格机械族。
    /// 配对资格与实际执行始终交给统一 Validator 和延迟队列。
    /// </summary>
    public sealed class FloatMenuOptionProvider_MechFusion
        : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;

        protected override bool Undrafted => true;

        protected override bool Multiselect => false;

        protected override bool MechanoidCanDo => true;

        protected override bool AppliesInt(FloatMenuContext context)
        {
            return ModsConfig.BiotechActive
                && context.FirstSelectedPawn != null;
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(
            Pawn clickedPawn,
            FloatMenuContext context)
        {
            Pawn? selected = context.FirstSelectedPawn;
            if (!TryResolvePair(
                    selected,
                    clickedPawn,
                    out Pawn? source,
                    out Pawn? wearer)
                || source == null
                || wearer == null)
            {
                yield break;
            }

            string label =
                "MAP_MechanoidMechanitor.Fusion.Gizmo.Label".Translate();
            if (!MechFusionValidator.CanStart(
                    source,
                    wearer,
                    out string? failureReason))
            {
                yield return new FloatMenuOption(
                    label + ": " + (failureReason ?? string.Empty),
                    null);
                yield break;
            }

            yield return new FloatMenuOption(
                label,
                delegate
                {
                    if (!GameComponent_MechFusionSessionRegistry.TryQueueStart(
                            source,
                            wearer,
                            sendFailureMessage: true,
                            out string? queueFailure)
                        && !string.IsNullOrEmpty(queueFailure))
                    {
                        Messages.Message(
                            queueFailure!,
                            source,
                            MessageTypeDefOf.RejectInput,
                            historical: false);
                    }
                });
        }

        private static bool TryResolvePair(
            Pawn? selected,
            Pawn? clicked,
            out Pawn? source,
            out Pawn? wearer)
        {
            source = null;
            wearer = null;
            if (selected == null
                || clicked == null
                || ReferenceEquals(selected, clicked))
            {
                return false;
            }

            if (MechFusionEligibilityUtility.HasFusionEligibility(selected)
                && clicked.RaceProps.Humanlike)
            {
                source = selected;
                wearer = clicked;
                return true;
            }

            if (selected.RaceProps.Humanlike
                && MechFusionEligibilityUtility.HasFusionEligibility(clicked))
            {
                source = clicked;
                wearer = selected;
                return true;
            }

            return false;
        }
    }
}
