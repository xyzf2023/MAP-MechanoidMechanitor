using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidOvermindOrder
    {
        public const int MaxCount = 500;

        // 原限制保留给机械族；动态物资按价值限购，并另限每次投送的实体堆数。
        public static int ThingMaxCount => GameComponent_OvermindEconomy.Enabled ? 50000 : MaxCount;
        public const int MaxThingStacks = 2048;

        private readonly List<MechanoidOvermindOrderLine_Mech> mechLines =
            new List<MechanoidOvermindOrderLine_Mech>();

        private readonly List<MechanoidOvermindOrderLine_Thing> thingLines =
            new List<MechanoidOvermindOrderLine_Thing>();

        private int revision;

        public IReadOnlyList<MechanoidOvermindOrderLine_Mech> MechLines => mechLines;

        public IReadOnlyList<MechanoidOvermindOrderLine_Thing> ThingLines => thingLines;

        public bool IsEmpty => mechLines.Count == 0 && thingLines.Count == 0;

        public int Revision => revision;

        public int GetMechCount(PawnKindDef? kind)
        {
            if (kind == null)
            {
                return 0;
            }

            for (int i = 0; i < mechLines.Count; i++)
            {
                if (mechLines[i].Kind == kind)
                {
                    return mechLines[i].Count;
                }
            }

            return 0;
        }

        public bool SetMechCount(PawnKindDef? kind, int count)
        {
            if (kind == null)
            {
                return false;
            }

            if (count < 0)
            {
                count = 0;
            }
            else if (count > MaxCount)
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

                if (count <= 0)
                {
                    mechLines.RemoveAt(i);
                    revision++;
                    return true;
                }

                if (line.Count == count)
                {
                    return false;
                }

                line.SetCount(count);
                revision++;
                return true;
            }

            if (count <= 0)
            {
                return false;
            }

            mechLines.Add(new MechanoidOvermindOrderLine_Mech(kind, count));
            revision++;
            return true;
        }

        public int GetThingCount(MechanoidOvermindThingSpec? spec)
        {
            if (spec?.Def == null)
            {
                return 0;
            }

            for (int i = 0; i < thingLines.Count; i++)
            {
                if (thingLines[i].Spec.Equals(spec))
                {
                    return thingLines[i].Count;
                }
            }

            return 0;
        }

        public bool SetThingCount(MechanoidOvermindThingSpec? spec, int count)
        {
            if (spec?.Def == null)
            {
                return false;
            }

            if (count < 0)
            {
                count = 0;
            }
            else if (count > ThingMaxCount)
            {
                count = ThingMaxCount;
            }

            for (int i = 0; i < thingLines.Count; i++)
            {
                MechanoidOvermindOrderLine_Thing line = thingLines[i];
                if (!line.Spec.Equals(spec))
                {
                    continue;
                }

                if (count <= 0)
                {
                    thingLines.RemoveAt(i);
                    revision++;
                    return true;
                }

                if (line.Count == count)
                {
                    return false;
                }

                line.SetCount(count);
                revision++;
                return true;
            }

            if (count <= 0)
            {
                return false;
            }

            thingLines.Add(new MechanoidOvermindOrderLine_Thing(spec, count));
            revision++;
            return true;
        }

        public bool RemoveMechAt(int index)
        {
            if (index < 0 || index >= mechLines.Count)
            {
                return false;
            }

            mechLines.RemoveAt(index);
            revision++;
            return true;
        }

        public bool RemoveThingAt(int index)
        {
            if (index < 0 || index >= thingLines.Count)
            {
                return false;
            }

            thingLines.RemoveAt(index);
            revision++;
            return true;
        }

        public void Clear()
        {
            if (mechLines.Count == 0 && thingLines.Count == 0)
            {
                return;
            }

            mechLines.Clear();
            thingLines.Clear();
            revision++;
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
