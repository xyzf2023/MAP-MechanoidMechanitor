using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class HediffDefExtension_LoverPregnancy : DefModExtension
    {
        public const float DefaultGestationDays = 5f;

        public float gestationDays = DefaultGestationDays;

        public float ResolveGestationDays()
        {
            return gestationDays > 0f ? gestationDays : DefaultGestationDays;
        }
    }
}
