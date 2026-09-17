using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompProperties_ReactionlessThrusterInstallEffect
        : CompProperties_MechanoidModuleInstallEffect
    {
        public CompProperties_ReactionlessThrusterInstallEffect()
            : base(typeof(CompReactionlessThrusterInstallEffect))
        {
        }
    }

    /// <summary>
    /// 无工质推进器通过现有飞行注册表授予持久自主飞行资格。
    /// 安装流程与物品消耗由通用机械族模块框架负责。
    /// </summary>
    public class CompReactionlessThrusterInstallEffect : CompMechanoidModuleInstallEffect
    {
        public override bool ShouldShowOption(Pawn pawn, out string? disabledReason)
        {
            disabledReason = GameComponent_MechanicalFlightRegistry.IsAuthorized(pawn)
                ? "：已经拥有飞行能力"
                : null;
            return true;
        }

        public override AcceptanceReport CanInstallNow(Pawn pawn)
        {
            return GameComponent_MechanicalFlightRegistry.IsAuthorized(pawn)
                ? AcceptanceReport.WasRejected
                : AcceptanceReport.WasAccepted;
        }

        public override bool TryApply(Pawn pawn)
        {
            return !GameComponent_MechanicalFlightRegistry.IsAuthorized(pawn)
                && GameComponent_MechanicalFlightRegistry.TryAuthorize(
                    pawn, source: MechanicalFlightAuthorizationSource.Direct)
                && GameComponent_MechanicalFlightRegistry.IsAuthorized(pawn);
        }

        public override string GetRejectMessage(Pawn pawn)
            => $"{pawn.LabelShort}无法安装{parent.LabelNoCount}。";

        public override string GetSuccessMessage(Pawn pawn)
            => $"{pawn.LabelShort}已安装无工质推进器，获得自主飞行能力。";
    }
}
