using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 显式授权标记：仅挂载本组件的 Pawn 参与显式社交关系与配偶关系基础逻辑。
    /// </summary>
    public sealed class CompProperties_ExplicitSocialRelationUser : CompProperties
    {
        public CompProperties_ExplicitSocialRelationUser()
        {
            compClass = typeof(CompExplicitSocialRelationUser);
        }
    }

    public sealed class CompExplicitSocialRelationUser : ThingComp
    {
        // 仅表示 Def 中真实挂载了该组件；最终功能资格请查询 ExplicitSocialRelationUtility。
    }
}
