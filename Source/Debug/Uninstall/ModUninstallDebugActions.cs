using System.Collections.Generic;
using LudeonTK;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class ModUninstallDebugActions
    {
        // DebugAction 的属性标签不会自动翻译；动态节点在显示时读取翻译键。
        [DebugActionYielder]
        private static IEnumerable<DebugActionNode> UninstallActions()
        {
            yield return new DebugActionNode(
                "MAP_MechanoidMechanitor.Uninstall.Action",
                DebugActionType.Action,
                ConfirmPreparation)
            {
                category = "MAP-机械族机械师",
                labelGetter = () => "MAP_MechanoidMechanitor.Uninstall.Action".Translate(),
                sourceAttribute = new DebugActionAttribute
                {
                    allowedGameStates = AllowedGameStates.Playing
                }
            };
        }

        private static void ConfirmPreparation()
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "MAP_MechanoidMechanitor.Uninstall.Confirmation".Translate(),
                ModUninstallPreparation.Execute,
                destructive: true));
        }
    }
}