using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>按由轻到重的顺序比较事件严重程度。</summary>
    internal enum WheelOfFateIncidentSeverity
    {
        Unknown = -1,
        Positive,
        Neutral,
        Negative,
        SevereNegative
    }

    internal static class WheelOfFateIncidentSeverityUtility
    {
        /// <summary>
        /// 信件类型优先；未声明信件时使用因果扰动主题的补充名单，
        /// 其余威胁类别按大小识别，仍无法识别时标记为未知。
        /// </summary>
        internal static WheelOfFateIncidentSeverity GetSeverity(IncidentDef incident)
        {
            LetterDef? letter = incident.letterDef;
            if (letter == LetterDefOf.PositiveEvent) return WheelOfFateIncidentSeverity.Positive;
            if (letter == LetterDefOf.NeutralEvent) return WheelOfFateIncidentSeverity.Neutral;
            if (letter == LetterDefOf.NegativeEvent || letter == LetterDefOf.ThreatSmall)
                return WheelOfFateIncidentSeverity.Negative;
            if (letter == LetterDefOf.ThreatBig) return WheelOfFateIncidentSeverity.SevereNegative;

            if (letter == null)
            {
                StoryThemeDef? theme = DefDatabase<StoryThemeDef>.GetNamedSilentFail("MAP_Theme_CausalDisturbance");
                if (theme?.positiveRandomIncidents != null && theme.positiveRandomIncidents.Contains(incident))
                    return WheelOfFateIncidentSeverity.Positive;
                if (theme?.neutralRandomIncidents != null && theme.neutralRandomIncidents.Contains(incident))
                    return WheelOfFateIncidentSeverity.Neutral;
                if (theme?.negativeRandomIncidents != null && theme.negativeRandomIncidents.Contains(incident))
                    return WheelOfFateIncidentSeverity.Negative;
            }

            if (incident.category == IncidentCategoryDefOf.ThreatBig)
                return WheelOfFateIncidentSeverity.SevereNegative;
            if (incident.category == IncidentCategoryDefOf.ThreatSmall)
                return WheelOfFateIncidentSeverity.Negative;
            return WheelOfFateIncidentSeverity.Unknown;
        }
    }
}
