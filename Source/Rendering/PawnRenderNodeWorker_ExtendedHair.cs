using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class PawnRenderNodeWorker_ExtendedHair : PawnRenderNodeWorker_FlipWhenCrawling
    {
        public override Vector3 OffsetFor(
            PawnRenderNode node,
            PawnDrawParms parms,
            out Vector3 pivot)
        {
            Vector3 offset = base.OffsetFor(node, parms, out pivot);

            HairRenderExtension? extension =
                ExtendedHairRenderUtility.ExtensionFor(parms.pawn);

            if (extension != null)
            {
                offset += extension.OffsetFor(parms.facing);
            }

            return offset;
        }

        public override Vector3 ScaleFor(PawnRenderNode node, PawnDrawParms parms)
        {
            Vector3 scale = base.ScaleFor(node, parms);

            HairRenderExtension? extension =
                ExtendedHairRenderUtility.ExtensionFor(parms.pawn);

            if (extension != null)
            {
                scale.x *= extension.drawSize.x;
                scale.z *= extension.drawSize.y;
            }

            return scale;
        }
    }
}
