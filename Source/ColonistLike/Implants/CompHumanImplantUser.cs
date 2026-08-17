using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 轻量标记组件：表示这个 Pawn 被授权使用本 MOD 的类人植入体自安装系统。
    /// 真正的安装逻辑不写在这里，身份判断只依赖此组件是否存在。
    /// </summary>
    public sealed class CompProperties_HumanImplantUser : CompProperties
    {
        public CompProperties_HumanImplantUser()
        {
            compClass = typeof(CompHumanImplantUser);
        }
    }

    public sealed class CompHumanImplantUser : ThingComp
    {
    }
}
