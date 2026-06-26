using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class PawnRenderNode_HairBack : PawnRenderNode_Hair
    {
        public PawnRenderNode_HairBack(
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

            HairRenderExtension? extension = hairDef.GetModExtension<HairRenderExtension>();

            if (extension == null || extension.backTexPath.NullOrEmpty())
            {
                return null!;
            }

            return base.MeshSetFor(pawn);
        }

        public override Graphic GraphicFor(Pawn pawn)
        {
            HairDef? hairDef = pawn.story?.hairDef;

            if (hairDef == null
                || hairDef.noGraphic
                || pawn.DevelopmentalStage.Baby()
                || pawn.DevelopmentalStage.Newborn())
            {
                return null!;
            }

            HairRenderExtension? extension = hairDef.GetModExtension<HairRenderExtension>();

            if (extension == null || extension.backTexPath.NullOrEmpty())
            {
                return null!;
            }

            Shader shader =
                hairDef.overrideShaderTypeDef?.Shader
                ?? ShaderDatabase.CutoutHair;

            return GraphicDatabase.Get<Graphic_Multi>(
                extension.backTexPath,
                shader,
                Vector2.one,
                ColorFor(pawn));
        }
    }
}
