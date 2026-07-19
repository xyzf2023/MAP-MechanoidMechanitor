namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class MechanoidOvermindOrderLine_Thing
    {
        public MechanoidOvermindThingSpec Spec { get; }

        public int Count { get; private set; }

        public MechanoidOvermindOrderLine_Thing(MechanoidOvermindThingSpec spec, int count)
        {
            Spec = spec;
            Count = count;
        }

        public void SetCount(int count)
        {
            Count = count;
        }
    }
}
