using System;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed partial class Dialog_DataProcessingAllocationDashboard
    {
        private sealed class StepInputDraft
        {
            public string control = string.Empty;
            public string text = string.Empty;
            public string originalText = string.Empty;
            public int minimum;
            public Pawn? target;
            public Action<int> save = null!;
            public Rect screenRect;
        }

        private StepInputDraft? activeStepInput;
        private bool stepInputDrawn;

        public override void WindowOnGUI()
        {
            // 在页面按钮、全局开关等动作发生前保存旧目标输入，包括点击窗口外部。
            if (activeStepInput != null && Event.current.type == EventType.MouseDown
                && !activeStepInput.screenRect.Contains(GUIUtility.GUIToScreenPoint(Event.current.mousePosition)))
                FinishStepInput();
            base.WindowOnGUI();
        }

        public override void Close(bool doCloseSound = true)
        {
            FinishStepInput();
            RequestBatchExit(() => base.Close(doCloseSound));
        }

        public override void OnCancelKeyPressed()
        {
            if (activeStepInput != null)
            {
                FinishStepInput(save: false);
                Event.current.Use();
                return;
            }
            base.OnCancelKeyPressed();
        }

        private void FinishStepInput(bool save = true)
        {
            var draft = activeStepInput;
            activeStepInput = null; // 先摘除，防止 setter 或关闭流程重入而重复提交。
            if (draft == null) return;
            if (Event.current != null) GUI.FocusControl(null);
            if (!save || draft.text == draft.originalText || !IsOverseerValid()) return;
            var registry = GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;
            if (registry == null) return;
            if (draft.target != null && (draft.target.Dead || draft.target.Destroyed || draft.target.Discarded
                || !registry.IsValidAllocationPairForList(overseer, draft.target))) return;
            if (!int.TryParse(draft.text, out int percent) || percent < 0) return;
            int next = Mathf.Max(draft.minimum, (int)Math.Round(percent / 5.0, MidpointRounding.AwayFromZero));
            draft.save(next);
        }

        private void FinishUnfocusedStepInput()
        {
            if (activeStepInput != null && (!stepInputDrawn || GUI.GetNameOfFocusedControl() != activeStepInput.control))
                FinishStepInput();
        }

        private void DrawStepEditor(Rect rect, ref float y, string label, int steps, Action<int> setter,
            int minimum = 0, Func<int>? readValue = null)
        {
            string control = "MAP_DP_" + inputScope + ":" + label;
            // 宽窗口一行完成标签与输入，小窗口才换行。
            bool stacked = rect.width < 390f;
            const float controlsWidth = 172f;
            if (stacked) Paragraph(rect, ref y, label, TextMain);
            else
            {
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.MiddleLeft; GUI.color = TextMain;
                Widgets.Label(new Rect(rect.x, y, rect.width - controlsWidth - 8f, 28f), label);
                Text.Anchor = TextAnchor.UpperLeft;
            }
            float x = stacked ? rect.x : rect.xMax - controlsWidth;
            var minus = new Rect(x, y, 30f, 28f);
            var field = new Rect(x + 36f, y, 82f, 28f);
            var plus = new Rect(x + 142f, y, 30f, 28f);
            string value = activeStepInput?.control == control ? activeStepInput.text : (steps * 5L).ToString();
            bool enter = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return
                && GUI.GetNameOfFocusedControl() == control;
            GUI.color = Color.white; Text.Font = GameFont.Small;
            GUI.SetNextControlName(control);
            string nextText = Widgets.TextField(field, value, 9);
            if (GUI.GetNameOfFocusedControl() == control)
            {
                if (activeStepInput?.control != control)
                {
                    FinishStepInput();
                    GUI.FocusControl(control);
                    activeStepInput = new StepInputDraft
                    {
                        control = control, originalText = value, text = value, minimum = minimum,
                        target = inputScope.StartsWith("Target:", StringComparison.Ordinal) ? selectedTarget : null,
                        save = setter
                    };
                }
                activeStepInput.text = nextText;
                activeStepInput.screenRect = new Rect(GUIUtility.GUIToScreenPoint(field.position), field.size);
                stepInputDrawn = true;
            }
            GUI.color = TextSecondary;
            Widgets.Label(new Rect(field.xMax + 2f, y + 4f, 20f, 24f), "%");
            if (enter) { FinishStepInput(); Event.current.Use(); }
            if (DrawMiniButton(minus, "−", steps > minimum))
            {
                FinishStepInput();
                setter(Mathf.Max(minimum, (readValue?.Invoke() ?? steps) - 1));
            }
            if (DrawMiniButton(plus, "+", steps < int.MaxValue / 5))
            {
                FinishStepInput();
                int actual = readValue?.Invoke() ?? steps;
                if (actual < int.MaxValue / 5) setter(actual + 1);
            }
            TooltipHandler.TipRegion(field, L("InputHelp"));
            TooltipHandler.TipRegion(minus, "−5%");
            TooltipHandler.TipRegion(plus, "+5%");
            y += 36f;
        }
    }
}
