using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class HediffCompProperties_FusionDormantGuard
        : HediffCompProperties
    {
        public HediffCompProperties_FusionDormantGuard()
        {
            compClass = typeof(HediffComp_FusionDormantGuard);
        }
    }

    /// <summary>
    /// 合体休眠维持的载体组件。它本身不执行逻辑，只让该 HediffDef 的
    /// AlwaysAllowMothball 为 false，从而在原版 WorldPawns 中避免源机械族
    /// 被 mothball 而冻结 Hediff 与限时 Comp 的正常计时。
    /// </summary>
    public sealed class HediffComp_FusionDormantGuard : HediffComp
    {
    }
}
