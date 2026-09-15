using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed partial class Dialog_DataProcessingAllocationDashboard
    {
        private void DrawGlobalSettings(Rect rect, GameComponent_DataProcessingAllocationRegistry registry)
        {
            inputScope = "Budget";
            DrawScrollable(rect, "Budget", view =>
            {
                float y = 0f;
                Section(view, ref y, L("BudgetPage"));
                var global = registry.FindDynamicAllocationRecordForUI(overseer);
                var budget = registry.GetBudgetSnapshotForUI(overseer);
                if (!budget.available) Paragraph(view, ref y, L("PendingHelp"), Warning);
                if (global != null)
                {
                    DrawStepEditor(view, ref y, L("Reserve"), global.minConsciousnessPercent / 5,
                        value => registry.SetDynamicMinConsciousnessPercent(overseer, Mathf.Clamp(value * 5, 55, 1000)), 11,
                        () => global.minConsciousnessPercent / 5);
                }
                if (!registry.IsDynamicAllocationEnabled(overseer)) Paragraph(view, ref y, L("ReservePaused"), Warning);
                TooltipHandler.TipRegion(new Rect(view.x, 0f, view.width, y), L("BudgetHelp"));
                Section(view, ref y, L("BudgetBreakdown"));
                if (budget.available)
                {
                    DrawInfoRow(view, ref y, L("Base"), budget.baseProcessing.ToStringPercent(), TextMain);
                    DrawInfoRow(view, ref y, L("Current"), budget.current.ToStringPercent(), Accent);
                    DrawInfoRow(view, ref y, L("FixedCost"), budget.fixedCost.ToStringPercent(), TextMain);
                    DrawInfoRow(view, ref y, L("DynamicCost"), budget.dynamicCost.ToStringPercent(), TextMain);
                    DrawInfoRow(view, ref y, L("SelfReturn"), budget.selfReturn.ToStringPercent(), Accent);
                    DrawInfoRow(view, ref y, L("Margin"), (budget.current - budget.threshold).ToStringPercent(), Warning);
                }
                DrawInfoRow(view, ref y, L("Deficit"), Percent(budget.unmetSteps), budget.limitedCount > 0 ? Warning : TextMain);
                if (budget.pendingCount > 0) Paragraph(view, ref y, L("Pending") + " · " + budget.pendingCount + L("Targets"), Warning);
                return y;
            });
        }

        private void DrawDefaultsPage(Rect rect, GameComponent_DataProcessingAllocationRegistry registry)
        {
            inputScope = "Defaults";
            DrawScrollable(rect, "Defaults", view =>
            {
                float y = 0f;
                if (registry.FindDynamicAllocationRecordForUI(overseer) == null)
                    return y;
                DrawGlobalDefaultsSection(view, ref y, registry);
                return y;
            });
        }
    }
}
