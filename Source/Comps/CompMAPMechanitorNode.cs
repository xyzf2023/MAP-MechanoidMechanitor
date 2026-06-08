using MMT;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompProperties_MAPMechanitorNode : CompProperties
    {
        public MAPMechanitorNodeRole role = MAPMechanitorNodeRole.None;
        public MAPMechanitorControlBackend controlBackend = MAPMechanitorControlBackend.None;
        public int extraMechBandwidth = 0;
        public int extraMechControlGroups = 0;
        public bool allowExternalOverseer = false;
        public bool canControlMechs = false;
        public bool temporaryTestNode = false;

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

            if (nodeProps.controlBackend == MAPMechanitorControlBackend.Shadow)
            {
                OverseerlessMechanitorUtility.RefreshMechanitorStateIfNode(pawn);
                OverseerlessMechanitorUtility.ClearExternalOverseerIfNode(pawn);

                if (Prefs.DevMode)
                {
                    Log.Message(
                        $"[MMT] MAP mechanitor node (Shadow): {pawn.LabelShort}, " +
                        $"role={nodeProps.role}, " +
                        $"mechanitor={(pawn.mechanitor != null)}, relations={(pawn.relations != null)}, " +
                        $"noOverseer={(pawn.GetOverseer() == null)}, " +
                        $"isMechanitor={MechanitorUtility.IsMechanitor(pawn)}, " +
                        $"requiresMechanitor={pawn.IsColonyMechRequiringMechanitor()}, " +
                        $"canDraft={MechanitorUtility.CanDraftMech(pawn).Accepted}, " +
                        $"totalBandwidth={pawn.mechanitor?.TotalBandwidth}, " +
                        $"controlGroups={pawn.mechanitor?.controlGroups?.Count}");
                }
            }
        }
    }
}
