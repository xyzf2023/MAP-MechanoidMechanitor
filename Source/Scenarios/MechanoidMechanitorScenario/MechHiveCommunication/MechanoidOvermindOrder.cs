using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidOvermindOrder
    {
        public const int MaxCount = 500;

        private readonly List<MechanoidOvermindOrderLine_Mech> mechLines =
            new List<MechanoidOvermindOrderLine_Mech>();

        private readonly List<MechanoidOvermindOrderLine_Thing> thingLines =
            new List<MechanoidOvermindOrderLine_Thing>();

        public IReadOnlyList<MechanoidOvermindOrderLine_Mech> MechLines => mechLines;

        public IReadOnlyList<MechanoidOvermindOrderLine_Thing> ThingLines => thingLines;

        public bool IsEmpty => mechLines.Count == 0 && thingLines.Count == 0;

        public bool TryAddMech(PawnKindDef kind, int count)
        {
            if (kind == null || count <= 0)
            {
                return false;
            }

            if (count > MaxCount)
            {
                count = MaxCount;
            }

            for (int i = 0; i < mechLines.Count; i++)
            {
                MechanoidOvermindOrderLine_Mech line = mechLines[i];
                if (line.Kind != kind)
                {
                    continue;
                }

                long merged = (long)line.Count + count;
                if (merged > MaxCount)
                {
                    merged = MaxCount;
                }

                line.SetCount((int)merged);
                return true;
            }

            mechLines.Add(new MechanoidOvermindOrderLine_Mech(kind, count));
            return true;
        }

        public bool TryAddThing(MechanoidOvermindThingSpec spec, int count)
        {
            if (spec?.Def == null || count <= 0)
            {
                return false;
            }

            if (count > MaxCount)
            {
                count = MaxCount;
            }

            for (int i = 0; i < thingLines.Count; i++)
            {
                MechanoidOvermindOrderLine_Thing line = thingLines[i];
                if (!line.Spec.Equals(spec))
                {
                    continue;
                }

                long merged = (long)line.Count + count;
                if (merged > MaxCount)
                {
                    merged = MaxCount;
                }

                line.SetCount((int)merged);
                return true;
            }

            thingLines.Add(new MechanoidOvermindOrderLine_Thing(spec, count));
            return true;
        }

        public bool RemoveMechAt(int index)
        {
            if (index < 0 || index >= mechLines.Count)
            {
                return false;
            }

            mechLines.RemoveAt(index);
            return true;
        }

        public bool RemoveThingAt(int index)
        {
            if (index < 0 || index >= thingLines.Count)
            {
                return false;
            }

            thingLines.RemoveAt(index);
            return true;
        }

        public void Clear()
        {
            mechLines.Clear();
            thingLines.Clear();
        }

        public bool TryGetCosts(out int mechCost, out int thingCost, out int totalCost)
        {
            return MechanoidOvermindPricingService.TryCalculateOrderCosts(
                this,
                out mechCost,
                out thingCost,
                out totalCost);
        }
    }
}
