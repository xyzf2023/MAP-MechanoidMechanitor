using UnityEngine;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 月亮（Moon）专用头部渲染节点，仅用于 PawnRenderTreeDef MAP_Moon 的 Head 节点。
    /// 通过 RenderTree 中明确设置的 props.texPath（Mech/Moon/Head/MoonHead）直接取得 Graphic_Multi，
    /// 绕开原版 PawnRenderNode_Head → HeadTypeDef.GetGraphic → ShaderUtility.GetSkinShader → CutoutSkin 路径，
    /// 从而消除机械体头部误用人类皮肤 Shader 导致的异常暗红 / 材质不一致问题。
    /// 继续使用原版 PawnRenderNodeWorker_Head，保留头部偏移、旋转、crawling、portrait、ApparelHead 等行为；
    /// 仍复用类人头部 Mesh（HumanlikeMeshPoolUtility）。
    /// </summary>
    public class PawnRenderNode_MoonHead : PawnRenderNode
    {
        public PawnRenderNode_MoonHead(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree)
            : base(pawn, props, tree)
        {
        }

        public override GraphicMeshSet MeshSetFor(Pawn pawn)
        {
            if (props.overrideMeshSize.HasValue)
            {
                return MeshPool.GetMeshSetForSize(props.overrideMeshSize.Value.x, props.overrideMeshSize.Value.y);
            }
            return HumanlikeMeshPoolUtility.GetHumanlikeHeadSetForPawn(pawn);
        }

        protected override string TexPathFor(Pawn pawn)
        {
            return props.texPath;
        }

        public override Graphic? GraphicFor(Pawn pawn)
        {
            if (!pawn.health.hediffSet.HasHead)
            {
                return null;
            }
            if (pawn.Drawer.renderer.CurRotDrawMode == RotDrawMode.Dessicated)
            {
                // 保留修改前 PawnRenderNode_Head 的 Dessicated 行为：尸化状态使用原版 Skull Graphic。
                // 该分支有意继续沿用 HeadTypeDefOf.Skull.GetGraphic，仍属于 HeadTypeDef 调用路径，
                // 不属于正常 Moon 头部的自定义材质路径（正常 Moon Head 不在此分支）。
                return HeadTypeDefOf.Skull.GetGraphic(pawn, Color.white);
            }
            string texPath = TexPathFor(pawn);
            if (texPath.NullOrEmpty())
            {
                return null;
            }
            Shader shader = ShaderFor(pawn);
            if (shader == null)
            {
                return null;
            }
            return GraphicDatabase.Get<Graphic_Multi>(texPath, shader, Vector2.one, Color.white);
        }
    }
}
