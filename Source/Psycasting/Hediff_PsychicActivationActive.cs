using RimWorld;
using System.Text;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class Hediff_PsychicActivationActive : HediffWithComps
    {
        public override string TipStringExtra
        {
            get
            {
                StringBuilder stringBuilder = new StringBuilder();
                string baseTip = base.TipStringExtra;
                if (!baseTip.NullOrEmpty())
                {
                    stringBuilder.Append(baseTip);
                    stringBuilder.AppendLine();
                }

                float bonus = PsychicCoreUtility.GetPsychicActivationBonusMultiplier(
                    pawn,
                    PsychicCoreUtility.GetPsychicCoreLevel(pawn));
                if (bonus > 0f)
                {
                    stringBuilder.AppendLine(
                        " - 精神力自动恢复："
                        + PsychicCoreUtility.FormatPsyfocusPercent(
                            PsychicCoreUtility.GetPsyfocusRecoveryPerHour(bonus))
                        + "/小时");
                }

                return stringBuilder.ToString().TrimEnd('\n', '\r');
            }
        }
    }
}
