using RimWorld;
using Verse;

namespace MMT
{
    public class CompProperties_OverseerlessMechanitorNode : CompProperties
    {
        public int extraMechBandwidth = 20;
        public int extraMechControlGroups = 3;

        public CompProperties_OverseerlessMechanitorNode()
        {
            compClass = typeof(CompOverseerlessMechanitorNode);
        }
    }

    public class CompOverseerlessMechanitorNode : ThingComp
    {
        public static bool PawnHasNode(Pawn pawn)
        {
            return pawn?.GetComp<CompOverseerlessMechanitorNode>() != null;
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            if (parent is not Pawn pawn)
            {
                return;
            }

            OverseerlessMechanitorUtility.EnsureBasicTrackers(pawn);
            OverseerlessMechanitorUtility.ClearExternalOverseerIfNode(pawn);

            if (Prefs.DevMode)
            {
                Log.Message(
                    $"[MMT] Overseerless mechanitor node: {pawn.LabelShort}, " +
                    $"mechanitor={(pawn.mechanitor != null)}, relations={(pawn.relations != null)}, " +
                    $"noOverseer={(pawn.GetOverseer() == null)}, " +
                    $"isMechanitor={MechanitorUtility.IsMechanitor(pawn)}");
            }
        }
    }
}
