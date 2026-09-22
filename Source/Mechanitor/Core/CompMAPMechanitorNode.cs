using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompProperties_MAPMechanitorNode : CompProperties
    {
        public MAPMechanitorControlBackend controlBackend = MAPMechanitorControlBackend.None;
        // Whether this node itself needs an external overseer; does not affect its ability to control other mechs.
        public bool requiresExternalOverseer = false;
        // Whether this externally overseen node ignores its overseer's command radius while it is actually controlled.
        public bool ignoreExternalOverseerCommandRange = false;
        public int extraMechBandwidth = 0;
        public int extraMechControlGroups = 0;
        public bool allowBossChipBandwidthUpgrade = false;
        public int maxIntrinsicBandwidth = 0;

        public CompProperties_MAPMechanitorNode()
        {
            compClass = typeof(CompMAPMechanitorNode);
        }
    }

    public class CompMAPMechanitorNode : ThingComp
    {
        public CompProperties_MAPMechanitorNode? NodeProps => props as CompProperties_MAPMechanitorNode;

        public int ChipBandwidthBonus => GetAuthoritativeChipBandwidthBonus();

        public int BaseExtraMechBandwidth => NodeProps?.extraMechBandwidth ?? 0;

        public int CurrentIntrinsicBandwidth => BaseExtraMechBandwidth + ChipBandwidthBonus;

        public int MaxIntrinsicBandwidth => NodeProps?.maxIntrinsicBandwidth ?? 0;

        public static bool PawnHasNode(Pawn? pawn)
        {
            return TryGetNodeComp(pawn, out _);
        }

        public static bool TryGetNodeComp(Pawn? pawn, out CompMAPMechanitorNode? comp)
        {
            comp = null;
            if (pawn == null)
            {
                return false;
            }

            comp = pawn.GetComp<CompMAPMechanitorNode>();
            return comp != null;
        }

        public override void PostPostMake()
        {
            base.PostPostMake();
            if (parent is Pawn pawn)
                MAPMechanitorNodeLifecycleUtility.NotifyLifecycle(pawn);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit && parent is Pawn pawn)
                MAPMechanitorNodeLifecycleUtility.NotifyLifecycle(pawn);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            if (parent is not Pawn pawn)
            {
                return;
            }

            MAPMechanitorNodeLifecycleUtility.NotifyLifecycle(pawn);
        }

        private int GetAuthoritativeChipBandwidthBonus()
        {
            if (parent is Pawn pawn
                && GameComponent_MechanoidMechanitorRegistry.TryGetNativeMechanitorRecord(
                    pawn,
                    out MechanoidMechanitorRecord? record)
                && record != null)
            {
                return record.ChipBandwidthBonus;
            }

            return 0;
        }

    }
}
