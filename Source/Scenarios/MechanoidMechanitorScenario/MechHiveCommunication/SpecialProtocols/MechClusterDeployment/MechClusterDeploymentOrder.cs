using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechClusterDeploymentOrder
    {
        public const int BaseCost = 4500;

        public const int ConditionCauserSurcharge = 750;

        private bool selected;

        private ThingDef? conditionCauser;

        private int revision;

        public bool Selected => selected;

        public ThingDef? ConditionCauser => conditionCauser;

        public int Revision => revision;

        public int Cost => !selected
            ? 0
            : BaseCost + (conditionCauser != null ? ConditionCauserSurcharge : 0);

        public void Select()
        {
            if (selected)
            {
                return;
            }

            selected = true;
            revision++;
        }

        public void SetConditionCauser(ThingDef? def)
        {
            if (!selected)
            {
                selected = true;
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
            if (!selected && conditionCauser == null)
            {
                return;
            }

            selected = false;
            conditionCauser = null;
            revision++;
        }
    }
}
