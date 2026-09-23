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
                ? "MAP_MechanoidMechanitor.InstallableModule.AlreadyFlightSuffix"
                    .Translate().ToString()
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
            => "MAP_MechanoidMechanitor.InstallableModule.Rejected".Translate(
                pawn.LabelShort, parent.LabelNoCount);

        public override string GetSuccessMessage(Pawn pawn)
            => "MAP_MechanoidMechanitor.InstallableModule.ReactionlessThruster.Success"
                .Translate(pawn.LabelShort);
    }
}
