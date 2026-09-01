using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechForceSupportOrder
    {
        public const int MinThreatPoints = 1;

        public const int DefaultThreatPoints = 1000;

        public const int LargeRequestWarningThreshold = 20000;

        /// <summary>
        /// 评级感知的有效威胁点上限：接管主脑后完全开放（int.MaxValue），
        /// 否则按当前评级等级限制。UI 与校验必须引用此方法。
        /// </summary>
        public static int EffectiveMaxThreatPoints() =>
            PurgeDirectiveRatingUtility.EffectiveForceSupportMaxThreat();

        private int threatPoints = DefaultThreatPoints;

        private string threatPointsBuffer = DefaultThreatPoints.ToString();

        private PawnGroupMaker? selectedGroupMaker;

        private int revision;

        public int ThreatPoints => threatPoints;

        public int Revision => revision;

        /// <summary>
        /// null 表示「随机」。
        /// </summary>
        public PawnGroupMaker? SelectedGroupMaker => selectedGroupMaker;

        public bool IsInputValid =>
            !threatPointsBuffer.NullOrEmpty()
            && int.TryParse(threatPointsBuffer, out int parsed)
            && parsed == threatPoints
            && threatPoints >= MinThreatPoints;

        public int Cost => IsInputValid ? ComputeCost(threatPoints) : 0;

        public static int ComputeCost(int points)
        {
            if (points < MinThreatPoints)
            {
                return 0;
            }

            return (int)(((long)points + 19L) / 20L);
        }

        public string GetTemplateLabel(Map? map = null)
        {
            if (selectedGroupMaker == null)
            {
                return "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.ForceSupport.TemplateRandom"
                    .Translate();
            }

            List<(PawnGroupMaker? maker, string label, string fullLabel)> entries =
                MechForceSupportService.BuildTemplateMenuEntries(this, map);
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].maker == selectedGroupMaker)
                {
                    return entries[i].fullLabel;
                }
            }

            return MechForceSupportService.BuildTemplateDisplayName(selectedGroupMaker);
        }

        public void SetSelectedGroupMaker(PawnGroupMaker? maker)
        {
            if (selectedGroupMaker == maker)
            {
                return;
            }

            selectedGroupMaker = maker;
            revision++;
        }

        public void DrawThreatPointsField(Rect rect)
        {
            int previousPoints = threatPoints;
            string previousBuffer = threatPointsBuffer;
            Widgets.TextFieldNumeric(
                rect,
                ref threatPoints,
                ref threatPointsBuffer,
                MinThreatPoints,
                EffectiveMaxThreatPoints());
            if (previousPoints != threatPoints || previousBuffer != threatPointsBuffer)
            {
                revision++;
            }
        }

        public void Clear()
        {
            string defaultText = DefaultThreatPoints.ToString();
            if (threatPoints == DefaultThreatPoints
                && threatPointsBuffer == defaultText
                && selectedGroupMaker == null)
            {
                return;
            }

            threatPoints = DefaultThreatPoints;
            threatPointsBuffer = defaultText;
            selectedGroupMaker = null;
            revision++;
        }
    }
}
