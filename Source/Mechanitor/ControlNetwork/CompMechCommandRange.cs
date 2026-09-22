using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>机械体自身的指挥距离豁免；不授予监管下属或自律资格。</summary>
    public sealed class CompProperties_MechCommandRange : CompProperties
    {
        public bool allowCrossMapCommand = true;

        public CompProperties_MechCommandRange() => compClass = typeof(CompMechCommandRange);
    }

    public sealed class CompMechCommandRange : ThingComp
    {
        public CompProperties_MechCommandRange Props => (CompProperties_MechCommandRange)props;
    }
}
