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
            Paragraph(rect, ref y, "合体中：自身分配已冻结", Warning);
            Paragraph(rect, ref y, "本次合体沿用开始时的自身档位和特化，其净成本仍占用源机械族预算。"
                + "解除合体后恢复编辑与动态调整；其他机械体仍可正常管理。", TextSecondary);
            DrawCurrentEffects(rect, ref y, registry, target, snapshot);
        }
    }
}
