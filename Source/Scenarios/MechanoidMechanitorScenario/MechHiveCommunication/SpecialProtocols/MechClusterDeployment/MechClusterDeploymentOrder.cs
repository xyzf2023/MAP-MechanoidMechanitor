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

        /// <summary>
        /// 评级感知的有效威胁点上限：接管主脑后回到原版常量（完全开放），
        /// 否则按当前评级等级限制。UI 与成本计算都必须引用此方法，避免上限漂移。
        /// </summary>
        public static int EffectiveMaxThreatPoints() =>
            PurgeDirectiveRatingUtility.EffectiveClusterMaxThreat();

        private ThingDef? conditionCauser;

        private int threatPoints = DefaultThreatPoints;

        private int revision;

        public ThingDef? ConditionCauser => conditionCauser;

        public int ThreatPoints => threatPoints;

        public int Revision => revision;

        public int Cost => ComputeCost(threatPoints, conditionCauser != null);

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

            int effectiveMax = EffectiveMaxThreatPoints();
            if (effectiveMax < MinThreatPoints)
            {
                effectiveMax = MinThreatPoints;
            }

            if (points > effectiveMax)
            {
                return effectiveMax;
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

        public void SetThreatPoints(int points)
        {
            int clamped = ClampThreatPoints(points);

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
            if (conditionCauser == null && threatPoints == DefaultThreatPoints)
            {
                return;
            }

            conditionCauser = null;
            threatPoints = DefaultThreatPoints;
            revision++;
        }

        public void SanitizeConditionCauser()
        {
            if (conditionCauser == null
                || MechClusterDeploymentService.IsConditionCauser(conditionCauser, threatPoints))
            {
                return;
            }

            conditionCauser = null;
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
