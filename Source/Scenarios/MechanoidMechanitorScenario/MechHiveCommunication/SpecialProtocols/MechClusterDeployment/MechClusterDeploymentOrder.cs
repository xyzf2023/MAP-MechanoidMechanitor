using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechClusterDeploymentOrder
    {
        public const int MinThreatPoints = 1000;

        public const int MaxThreatPoints = 10000;

        public const int ThreatPointsStep = 1000;

        public const int DefaultThreatPoints = 10000;

        public const int CreditsPerThousandPoints = 250;

        public const int ConditionCauserSurcharge = 375;

        private bool selected;

        private ThingDef? conditionCauser;

        private int threatPoints = DefaultThreatPoints;

        private int revision;

        public bool Selected => selected;

        public ThingDef? ConditionCauser => conditionCauser;

        public int ThreatPoints => threatPoints;

        public int Revision => revision;

        public int Cost => !selected
            ? 0
            : ComputeCost(threatPoints, conditionCauser != null);

        public static int ComputeCost(int points, bool withConditionCauser)
        {
            int clamped = ClampThreatPoints(points);
            int baseCost = clamped / ThreatPointsStep * CreditsPerThousandPoints;
            return baseCost + (withConditionCauser ? ConditionCauserSurcharge : 0);
        }

        public static int ClampThreatPoints(int points)
        {
            if (points < MinThreatPoints)
            {
                return MinThreatPoints;
            }

            if (points > MaxThreatPoints)
            {
                return MaxThreatPoints;
            }

            int steps = (points + ThreatPointsStep / 2) / ThreatPointsStep;
            if (steps < 1)
            {
                steps = 1;
            }

            if (steps > MaxThreatPoints / ThreatPointsStep)
            {
                steps = MaxThreatPoints / ThreatPointsStep;
            }

            return steps * ThreatPointsStep;
        }

        public void Select()
        {
            if (selected)
            {
                return;
            }

            selected = true;
            revision++;
        }

        public void SetThreatPoints(int points)
        {
            int clamped = ClampThreatPoints(points);
            if (!selected)
            {
                selected = true;
            }

            ThingDef? previousCauser = conditionCauser;
            if (threatPoints == clamped)
            {
                EnsureConditionCauserMatchesPoints();
                if (previousCauser != conditionCauser)
                {
                    revision++;
                }

                return;
            }

            threatPoints = clamped;
            EnsureConditionCauserMatchesPoints();
            revision++;
        }

        public void SetConditionCauser(ThingDef? def)
        {
            if (!selected)
            {
                selected = true;
            }

            if (def != null
                && !MechClusterDeploymentService.IsConditionCauser(def, threatPoints))
            {
                def = null;
            }

            if (conditionCauser == def)
            {
                return;
            }

            conditionCauser = def;
            revision++;
        }

        public void Clear()
        {
            if (!selected
                && conditionCauser == null
                && threatPoints == DefaultThreatPoints)
            {
                return;
            }

            selected = false;
            conditionCauser = null;
            threatPoints = DefaultThreatPoints;
            revision++;
        }

        private void EnsureConditionCauserMatchesPoints()
        {
            if (conditionCauser == null)
            {
                return;
            }

            if (!MechClusterDeploymentService.IsConditionCauser(conditionCauser, threatPoints))
            {
                conditionCauser = null;
            }
        }
    }
}
