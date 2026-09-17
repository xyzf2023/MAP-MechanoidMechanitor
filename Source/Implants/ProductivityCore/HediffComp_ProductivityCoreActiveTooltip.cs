using System.Text;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class HediffComp_ProductivityCoreActiveTooltip : HediffComp
    {
        private const string KeyPrefix =
            "MAP_MechanoidMechanitor.Implants.ProductivityCore.ActiveTooltip.";

        public override string CompTipStringExtra
        {
            get
            {
                if (Pawn == null)
                {
                    return string.Empty;
                }

                int level = ProductivityCoreUtility.GetEffectiveLevelForWorker(Pawn);
                float workSpeedOffset = level * ProductivityCoreUtility.WorkSpeedOffsetPerLevel;
                StringBuilder builder = new StringBuilder();
                builder.AppendLine((KeyPrefix + "WorkSpeed")
                    .Translate(workSpeedOffset.ToStringPercent()).ToString());
                builder.AppendLine((KeyPrefix + "Quality")
                    .Translate(level).ToString());
                builder.AppendLine((KeyPrefix + "IgnoreSkillRequirements").Translate().ToString());
                builder.AppendLine((KeyPrefix + "NoFoodPoisoning").Translate().ToString());
                builder.AppendLine((KeyPrefix + "NoSurgeryFailure").Translate().ToString());
                return builder.ToString().TrimEnd('\r', '\n');
            }
        }
    }

    public sealed class HediffCompProperties_ProductivityCoreActiveTooltip : HediffCompProperties
    {
        public HediffCompProperties_ProductivityCoreActiveTooltip()
        {
            compClass = typeof(HediffComp_ProductivityCoreActiveTooltip);
        }
    }
}
