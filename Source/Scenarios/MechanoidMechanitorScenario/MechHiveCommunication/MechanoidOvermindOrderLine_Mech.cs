using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidOvermindOrderLine_Mech
    {
        public PawnKindDef Kind { get; }

        public int Count { get; private set; }

        public MechanoidOvermindOrderLine_Mech(PawnKindDef kind, int count)
        {
            Kind = kind;
            Count = count;
        }

        public void SetCount(int count)
        {
            Count = count;
        }
    }
}
