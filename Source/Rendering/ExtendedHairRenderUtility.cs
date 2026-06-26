using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class ExtendedHairRenderUtility
    {
        public static HairRenderExtension? ExtensionFor(Pawn pawn)
        {
            return pawn.story?.hairDef?.GetModExtension<HairRenderExtension>();
        }

        public static GraphicMeshSet MeshSetFor(Pawn pawn, HairRenderExtension extension)
        {
            if (extension.meshSize.HasValue)
            {
                Vector2 size = extension.meshSize.Value;

                if (ModsConfig.BiotechActive
                    && pawn.ageTracker.CurLifeStage.headSizeFactor.HasValue)
                {
                    size *= pawn.ageTracker.CurLifeStage.headSizeFactor.Value;
                }

                return MeshPool.GetMeshSetForSize(size.x, size.y);
            }

            return HumanlikeMeshPoolUtility.GetHumanlikeHairSetForPawn(pawn);
        }
    }
}
