using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed partial class Dialog_DataProcessingAllocationDashboard
    {
        private void DrawFrozenSelfDetails(Rect rect, ref float y,
            GameComponent_DataProcessingAllocationRegistry registry, Pawn target,
            DataProcessingTargetUISnapshot snapshot)
        {
            Paragraph(rect, ref y, "MAP_MechanoidMechanitor.DataProcessing.FusionFrozenSelf".Translate(), Warning);
            Paragraph(rect, ref y, "MAP_MechanoidMechanitor.DataProcessing.FusionFrozenSelfDetails".Translate(), TextSecondary);
            DrawCurrentEffects(rect, ref y, registry, target, snapshot);
        }
    }
}
