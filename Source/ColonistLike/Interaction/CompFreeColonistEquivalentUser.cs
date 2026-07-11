using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_FreeColonistEquivalentUser : CompProperties
    {
        public CompProperties_FreeColonistEquivalentUser()
        {
            compClass = typeof(CompFreeColonistEquivalentUser);
        }
    }

    public sealed class CompFreeColonistEquivalentUser : ThingComp
    {
        // 仅表示 Def 中真实挂载了该组件；最终功能资格请查询能力层。
        public static bool IsOptedIn(Pawn? pawn) =>
            pawn?.GetComp<CompFreeColonistEquivalentUser>() != null;
    }
}
