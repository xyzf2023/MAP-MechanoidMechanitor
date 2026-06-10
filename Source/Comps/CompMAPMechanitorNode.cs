using MMT;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompProperties_MAPMechanitorNode : CompProperties
    {
        public MAPMechanitorControlBackend controlBackend = MAPMechanitorControlBackend.None;
        // Whether this node itself needs an external overseer; does not affect its ability to control other mechs.
        public bool requiresExternalOverseer = false;
        public int extraMechBandwidth = 0;
        public int extraMechControlGroups = 0;

        public CompProperties_MAPMechanitorNode()
        {
            compClass = typeof(CompMAPMechanitorNode);
        }
    }

    public class CompMAPMechanitorNode : ThingComp
    {
        public CompProperties_MAPMechanitorNode? NodeProps => props as CompProperties_MAPMechanitorNode;

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

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            if (parent is not Pawn pawn)
            {
                return;
            }

            CompProperties_MAPMechanitorNode? nodeProps = NodeProps;
            if (nodeProps == null)
            {
                return;
            }

            if (nodeProps.controlBackend == MAPMechanitorControlBackend.Shadow
                || nodeProps.controlBackend == MAPMechanitorControlBackend.Vanilla)
            {
                OverseerlessMechanitorUtility.EnsureBasicTrackers(pawn);

                if (!nodeProps.requiresExternalOverseer)
                {
                    OverseerlessMechanitorUtility.ClearExternalOverseerIfNode(pawn);
                }

                if (Prefs.DevMode)
                {
                    Log.Message(
                        $"[MAP-MechanoidMechanitor] MAP mechanitor node ({nodeProps.controlBackend}): {pawn.LabelShort}, " +
                        $"requiresExternalOverseer={nodeProps.requiresExternalOverseer}, " +
                        $"mechanitor={(pawn.mechanitor != null)}, relations={(pawn.relations != null)}, " +
                        $"noOverseer={(pawn.GetOverseer() == null)}, " +
                        $"totalBandwidth={pawn.mechanitor?.TotalBandwidth}, " +
                        $"controlGroups={pawn.mechanitor?.controlGroups?.Count}");
                }
            }
        }
    }
}
