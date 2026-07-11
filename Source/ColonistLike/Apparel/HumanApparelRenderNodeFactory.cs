using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    internal static class HumanApparelRenderNodeFactory
    {
        public static List<PawnRenderNode>? CreateNodes(Pawn pawn, PawnRenderTree tree)
        {
            if (pawn == null
                || tree == null
                || pawn.apparel == null
                || pawn.apparel.WornApparelCount == 0
                || !HumanApparelUtility.TryGetApparelComp(pawn, out CompHumanApparelUser? comp)
                || !comp!.EnableHumanApparelRendering)
            {
                return null;
            }

            tree.TryGetNodeByTag(PawnRenderNodeTagDefOf.ApparelHead, out PawnRenderNode headApparelNode);
            tree.TryGetNodeByTag(PawnRenderNodeTagDefOf.ApparelBody, out PawnRenderNode bodyApparelNode);

            Dictionary<PawnRenderNode, int> layerOffsets = new Dictionary<PawnRenderNode, int>();
            List<PawnRenderNode> nodes = new List<PawnRenderNode>();

            foreach (Apparel ap in pawn.apparel.WornApparel)
            {
                if (!ShouldProcessApparel(ap))
                {
                    continue;
                }

                if (!ApparelGraphicRecordGetter.TryGetGraphicApparel(
                        ap,
                        pawn.story.bodyType,
                        pawn.Drawer.renderer.StatueColor.HasValue,
                        out _))
                {
                    continue;
                }

                if (!TryResolvePlacement(
                        ap,
                        tree,
                        headApparelNode,
                        bodyApparelNode,
                        out bool isHeadApparel,
                        out PawnRenderNode? parentNode,
                        out PawnRenderNodeTagDef parentTagDef)
                    || parentNode == null)
                {
                    continue;
                }

                if (!layerOffsets.TryGetValue(parentNode, out int layerOffset))
                {
                    layerOffset = 0;
                }

                DrawData? drawData = ap.def.apparel.drawData;
                PawnRenderNodeProperties props = new PawnRenderNodeProperties
                {
                    debugLabel = ap.def.defName,
                    nodeClass = typeof(PawnRenderNode_Apparel),
                    workerClass = isHeadApparel
                        ? typeof(PawnRenderNodeWorker_Apparel_Head)
                        : typeof(PawnRenderNodeWorker_Apparel_Body),
                    parentTagDef = parentTagDef,
                    baseLayer = parentNode.Props.baseLayer + layerOffset,
                    drawData = drawData,
                    pawnType = PawnRenderNodeProperties.RenderNodePawnType.Any
                };

                if (!isHeadApparel
                    && drawData == null
                    && !ap.def.apparel.shellRenderedBehindHead)
                {
                    ApparelLayerDef lastLayer = ap.def.apparel.LastLayer;
                    if (lastLayer == ApparelLayerDefOf.Shell)
                    {
                        props.drawData = DrawData.NewWithData(
                            new DrawData.RotationalData(Rot4.North, 88f));
                    }
                    else if (ap.RenderAsPack())
                    {
                        props.drawData = DrawData.NewWithData(
                            new DrawData.RotationalData(Rot4.North, 93f),
                            new DrawData.RotationalData(Rot4.South, -3f));
                    }
                }

                if (!tree.ShouldAddNodeToTree(props))
                {
                    continue;
                }

                nodes.Add(new PawnRenderNode_Apparel(pawn, props, tree, ap));
                layerOffsets[parentNode] = layerOffset + 1;
            }

            return nodes.Count > 0 ? nodes : null;
        }

        private static bool ShouldProcessApparel(Apparel? ap)
        {
            if (ap?.def?.apparel == null)
            {
                return false;
            }

            if (ap.def.IsWeapon)
            {
                return false;
            }

            return !ap.def.apparel.HasDefinedGraphicProperties;
        }

        private static bool TryResolvePlacement(
            Apparel ap,
            PawnRenderTree tree,
            PawnRenderNode? headApparelNode,
            PawnRenderNode? bodyApparelNode,
            out bool isHeadApparel,
            out PawnRenderNode? parentNode,
            out PawnRenderNodeTagDef parentTagDef)
        {
            isHeadApparel = false;
            parentNode = null;
            parentTagDef = PawnRenderNodeTagDefOf.ApparelBody;

            PawnRenderNodeTagDef? explicitTag = ap.def.apparel.parentTagDef;
            if (explicitTag != null
                && explicitTag != PawnRenderNodeTagDefOf.ApparelHead
                && explicitTag != PawnRenderNodeTagDefOf.ApparelBody)
            {
                return false;
            }

            ApparelLayerDef lastLayer = ap.def.apparel.LastLayer;
            isHeadApparel = lastLayer == ApparelLayerDefOf.Overhead
                || lastLayer == ApparelLayerDefOf.EyeCover
                || explicitTag == PawnRenderNodeTagDefOf.ApparelHead;

            if (explicitTag != null && tree.TryGetNodeByTag(explicitTag, out PawnRenderNode taggedParent))
            {
                parentNode = taggedParent;
                parentTagDef = explicitTag;
                if (headApparelNode != null && parentNode == headApparelNode)
                {
                    isHeadApparel = true;
                }
                else if (bodyApparelNode != null && parentNode == bodyApparelNode)
                {
                    isHeadApparel = false;
                }
            }
            else if (isHeadApparel)
            {
                if (headApparelNode == null)
                {
                    return false;
                }

                parentNode = headApparelNode;
                parentTagDef = PawnRenderNodeTagDefOf.ApparelHead;
            }
            else
            {
                if (bodyApparelNode == null)
                {
                    return false;
                }

                parentNode = bodyApparelNode;
                parentTagDef = PawnRenderNodeTagDefOf.ApparelBody;
            }

            return parentNode != null;
        }
    }
}
