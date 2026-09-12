using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_MechFormCarrier : CompProperties
    {
        public CompProperties_MechFormCarrier()
        {
            compClass = typeof(CompMechFormCarrier);
        }
    }

    /// <summary>
    /// 建筑或合体外甲上的轻量链接。载体不复制原始 Pawn 数据。
    /// </summary>
    public sealed class CompMechFormCarrier : ThingComp
    {
        private Pawn? sourcePawn;
        private string? transformationId;
        private MechTransformationForm carrierForm;
        private bool committed;

        public Pawn? SourcePawn => sourcePawn;

        public MechTransformationForm CarrierForm => carrierForm;

        public bool Committed => committed;

        internal bool TryBind(
            Pawn pawn,
            string identity,
            MechTransformationForm form,
            out string? failureReason)
        {
            failureReason = null;
            if (pawn == null
                || pawn.Destroyed
                || pawn.Discarded
                || string.IsNullOrEmpty(identity)
                || form == MechTransformationForm.Pawn)
            {
                failureReason = "载体绑定参数无效。";
                return false;
            }

            if (committed
                && (!ReferenceEquals(sourcePawn, pawn)
                    || transformationId != identity
                    || carrierForm != form))
            {
                failureReason = "载体已经绑定到其他机械体或形态。";
                return false;
            }

            sourcePawn = pawn;
            transformationId = identity;
            carrierForm = form;
            committed = true;
            return true;
        }

        internal void ClearLink()
        {
            sourcePawn = null;
            transformationId = null;
            carrierForm = MechTransformationForm.Pawn;
            committed = false;
        }

        internal bool Matches(MechTransformationRecord record)
        {
            return committed
                && record != null
                && ReferenceEquals(sourcePawn, record.SourcePawn)
                && transformationId == record.TransformationId
                && carrierForm == record.CurrentForm;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref sourcePawn, "sourcePawn");
            Scribe_Values.Look(ref transformationId, "transformationId");
            Scribe_Values.Look(
                ref carrierForm,
                "carrierForm",
                MechTransformationForm.Pawn);
            Scribe_Values.Look(ref committed, "committed");
        }
    }
}
