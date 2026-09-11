using System;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 一台真实 Pawn 的通用形态记录。这里只保存身份与形态链接，
    /// 不保存能源、飞行、武器、维修或其他具体能力数据。
    /// </summary>
    public sealed class MechTransformationRecord : IExposable
    {
        private string? transformationId;
        private Pawn? sourcePawn;
        private MechTransformationForm currentForm;
        private Thing? externalCarrier;
        private bool transitionInProgress;
        private MechTransformationForm pendingForm;

        public string TransformationId
        {
            get
            {
                EnsureInitialized();
                return transformationId!;
            }
        }

        public Pawn? SourcePawn => sourcePawn;

        public MechTransformationForm CurrentForm => currentForm;

        public Thing? ExternalCarrier => externalCarrier;

        public bool TransitionInProgress => transitionInProgress;

        public MechTransformationForm PendingForm => pendingForm;

        public MechTransformationRecord()
        {
        }

        internal MechTransformationRecord(Pawn pawn)
        {
            sourcePawn = pawn;
            currentForm = MechTransformationForm.Pawn;
            pendingForm = MechTransformationForm.Pawn;
            EnsureInitialized();
        }

        internal void EnsureInitialized()
        {
            if (string.IsNullOrEmpty(transformationId))
            {
                transformationId = Guid.NewGuid().ToString("N");
            }
        }

        internal void RegenerateIdentity()
        {
            transformationId = Guid.NewGuid().ToString("N");
        }

        internal bool BeginTransition(MechTransformationForm targetForm)
        {
            if (transitionInProgress)
            {
                return false;
            }

            transitionInProgress = true;
            pendingForm = targetForm;
            return true;
        }

        internal void CommitTransition(
            MechTransformationForm targetForm,
            Thing? carrier)
        {
            currentForm = targetForm;
            externalCarrier = carrier;
            pendingForm = targetForm;
            transitionInProgress = false;
        }

        internal void CancelTransition()
        {
            pendingForm = currentForm;
            transitionInProgress = false;
        }

        internal void ClearDestroyedCarrier()
        {
            if (externalCarrier?.Destroyed == true)
            {
                externalCarrier = null;
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref transformationId, "transformationId");
            Scribe_References.Look(ref sourcePawn, "sourcePawn");
            Scribe_Values.Look(
                ref currentForm,
                "currentForm",
                MechTransformationForm.Pawn);
            Scribe_References.Look(ref externalCarrier, "externalCarrier");
            Scribe_Values.Look(ref transitionInProgress, "transitionInProgress");
            Scribe_Values.Look(
                ref pendingForm,
                "pendingForm",
                MechTransformationForm.Pawn);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureInitialized();
            }
        }
    }
}
