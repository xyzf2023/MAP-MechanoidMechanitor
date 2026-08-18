using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 虫族追猎进行中时显示的告警。
    /// 普通 AlertsReadout 会自动实例化所有非 Alert_Custom 的 Alert 子类。
    /// 不继承 Alert_Critical，避免其 AlertActiveUpdate 自动额外发送“critical alert”消息；
    /// 仅借用其红色背景。
    /// </summary>
    public sealed class Alert_InsectPursuit : Alert
    {
        public Alert_InsectPursuit()
        {
            defaultPriority = AlertPriority.Critical;
        }

        protected override Color BGColor => Alert_Critical.BgColor();

        public override AlertReport GetReport()
        {
            if (Current.Game == null)
            {
                return false;
            }

            GameComponent_MechanoidMechanitorInsectPursuitManager? manager =
                GameComponent_MechanoidMechanitorInsectPursuitManager.GetManager();
            if (manager == null)
            {
                return false;
            }

            Map? current = Find.CurrentMap;
            if (current == null)
            {
                return false;
            }

            if (!MechanoidMechanitorInsectPursuitUtility.IsActiveHuntOnMap(
                manager,
                current))
            {
                return false;
            }

            return AlertReport.Active;
        }

        public override string GetLabel()
        {
            int remainingHours = 0;
            GameComponent_MechanoidMechanitorInsectPursuitManager? manager =
                GameComponent_MechanoidMechanitorInsectPursuitManager.GetManager();
            if (manager != null)
            {
                remainingHours = manager.GetActiveHuntRemainingHours();
            }

            return "MAP_MechanoidMechanitor.InsectPursuit.Alert.Label"
                .Translate(remainingHours);
        }

        public override TaggedString GetExplanation()
        {
            return "MAP_MechanoidMechanitor.InsectPursuit.Alert.Explanation".Translate();
        }
    }
}
