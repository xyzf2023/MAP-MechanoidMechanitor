using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class PawnRenderNode_HairFront : PawnRenderNode_Hair
    {
        public PawnRenderNode_HairFront(
            Pawn pawn,
            PawnRenderNodeProperties props,
            PawnRenderTree tree)
            : base(pawn, props, tree)
        {
        }

        public override GraphicMeshSet MeshSetFor(Pawn pawn)
        {
            HairDef? hairDef = pawn.story?.hairDef;

            if (hairDef == null || hairDef.noGraphic)
            {
                return null!;
            }

            if (pawn.DevelopmentalStage.Baby() || pawn.DevelopmentalStage.Newborn())
            {
                return null!;
            }

            HairRenderExtension? extension = hairDef.GetModExtension<HairRenderExtension>();

            if (extension == null)
            {
                return base.MeshSetFor(pawn);
            }

            return ExtendedHairRenderUtility.MeshSetFor(pawn, extension);
        }
    }
}
