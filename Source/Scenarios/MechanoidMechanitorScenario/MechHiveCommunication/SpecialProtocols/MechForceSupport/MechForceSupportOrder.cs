using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechForceSupportOrder
    {
        public const int MinThreatPoints = 1;

        public const int DefaultThreatPoints = 1000;

        public const int LargeRequestWarningThreshold = 20000;

        private int threatPoints = DefaultThreatPoints;

        private string threatPointsBuffer = DefaultThreatPoints.ToString();

        private int revision;

        public int ThreatPoints => threatPoints;

        public int Revision => revision;

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

        public void DrawThreatPointsField(Rect rect)
        {
            int previousPoints = threatPoints;
            string previousBuffer = threatPointsBuffer;
            Widgets.TextFieldNumeric(
                rect,
                ref threatPoints,
                ref threatPointsBuffer,
                MinThreatPoints,
                int.MaxValue);
            if (previousPoints != threatPoints || previousBuffer != threatPointsBuffer)
            {
                revision++;
            }
        }

        public void Clear()
        {
            string defaultText = DefaultThreatPoints.ToString();
            if (threatPoints == DefaultThreatPoints
                && threatPointsBuffer == defaultText)
            {
                return;
            }

            threatPoints = DefaultThreatPoints;
            threatPointsBuffer = defaultText;
            revision++;
        }
    }
}
