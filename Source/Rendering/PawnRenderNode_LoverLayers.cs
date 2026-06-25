using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class PawnRenderNode_LoverBody : PawnRenderNode
    {
        public PawnRenderNode_LoverBody(
            Pawn pawn,
            PawnRenderNodeProperties props,
            PawnRenderTree tree)
            : base(pawn, props, tree)
        {
        }

        public override GraphicMeshSet MeshSetFor(Pawn pawn)
        {
            return HumanlikeMeshPoolUtility.GetHumanlikeBodySetForPawn(pawn);
        }

        public override Graphic GraphicFor(Pawn pawn)
        {
            if (props.texPath.NullOrEmpty())
            {
                return null!;
            }

            return GraphicDatabase.Get<Graphic_Multi>(
                props.texPath,
                ShaderDatabase.Cutout,
                Vector2.one,
                Color.white);
        }
    }

    public class PawnRenderNode_LoverHead : PawnRenderNode
    {
        public PawnRenderNode_LoverHead(
            Pawn pawn,
            PawnRenderNodeProperties props,
            PawnRenderTree tree)
            : base(pawn, props, tree)
        {
        }

        public override GraphicMeshSet MeshSetFor(Pawn pawn)
        {
            return HumanlikeMeshPoolUtility.GetHumanlikeHeadSetForPawn(pawn);
        }

        public override Graphic GraphicFor(Pawn pawn)
        {
            if (pawn.health?.hediffSet == null
                || !pawn.health.hediffSet.HasHead
                || props.texPath.NullOrEmpty())
            {
                return null!;
            }

            return GraphicDatabase.Get<Graphic_Multi>(
                props.texPath,
                ShaderDatabase.Cutout,
                Vector2.one,
                Color.white);
        }
    }

    public class PawnRenderNode_LoverHairLayer : PawnRenderNode
    {
        private const string LoverHairDefName = "MAP_LoverHair";

        public PawnRenderNode_LoverHairLayer(
            Pawn pawn,
            PawnRenderNodeProperties props,
            PawnRenderTree tree)
            : base(pawn, props, tree)
        {
        }

        public override GraphicMeshSet MeshSetFor(Pawn pawn)
        {
            return HumanlikeMeshPoolUtility.GetHumanlikeHairSetForPawn(pawn);
        }

        public override Graphic GraphicFor(Pawn pawn)
        {
            if (pawn.health?.hediffSet == null
                || !pawn.health.hediffSet.HasHead
                || pawn.story?.hairDef?.defName != LoverHairDefName
                || props.texPath.NullOrEmpty())
            {
                return null!;
            }

            return GraphicDatabase.Get<Graphic_Multi>(
                props.texPath,
                ShaderDatabase.CutoutHair,
                Vector2.one,
                ColorFor(pawn));
        }
    }
}
