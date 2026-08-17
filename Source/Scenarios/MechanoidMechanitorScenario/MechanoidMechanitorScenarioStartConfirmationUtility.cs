using System;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 统一封装“以当前设置开始游戏？”的最终确认弹窗。复用原版 Dialog_MessageBox，
    /// 不自行实现 Window，保留原版 Enter 确认 / Esc 取消 的键盘行为。
    /// </summary>
    internal static class MechanoidMechanitorScenarioStartConfirmationUtility
    {
        public static void Show(Action? confirmedAction)
        {
            if (confirmedAction == null)
            {
                return;
            }

            Dialog_MessageBox dialog = Dialog_MessageBox.CreateConfirmation(
                "MAP_MechanoidMechanitor.Scenario.StartConfirmation.Text".Translate(),
                confirmedAction);
            dialog.buttonAText =
                "MAP_MechanoidMechanitor.Scenario.StartConfirmation.Confirm".Translate();
            dialog.buttonBText =
                "MAP_MechanoidMechanitor.Scenario.StartConfirmation.Cancel".Translate();
            Find.WindowStack.Add(dialog);
        }
    }
}
