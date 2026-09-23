using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompProperties_BionicCompanionModuleInstallEffect
        : CompProperties_MechanoidModuleInstallEffect
    {
        public CompProperties_BionicCompanionModuleInstallEffect()
            : base(typeof(CompBionicCompanionModuleInstallEffect))
        {
        }
    }

    /// <summary>
    /// 仿生伴侣模块的具体安装效果：动态授权仿生伴侣。
    /// 复用 SyntheticCompanionStateUtility 与 GameComponent_SyntheticCompanionRegistry，不复制授权逻辑。
    /// </summary>
    public class CompBionicCompanionModuleInstallEffect
        : CompMechanoidModuleInstallEffect
    {
        private const string AlreadyHasStateSuffixKey =
            "MAP_MechanoidMechanitor.InstallableModule.AlreadyCompanionSuffix";

        public override bool ShouldShowOption(Pawn pawn, out string? disabledReason)
        {
            disabledReason = null;

            if (SyntheticCompanionStateUtility.HasState(pawn))
            {
                disabledReason = AlreadyHasStateSuffixKey.Translate();
                return true;
            }

            return true;
        }

        public override AcceptanceReport CanInstallNow(Pawn pawn)
        {
            if (SyntheticCompanionStateUtility.HasState(pawn))
            {
                return AcceptanceReport.WasRejected;
            }

            return AcceptanceReport.WasAccepted;
        }

        public override bool TryApply(Pawn pawn)
        {
            if (!GameComponent_SyntheticCompanionRegistry.TryAuthorize(pawn)
                || !SyntheticCompanionStateUtility.HasState(pawn))
            {
                return false;
            }

            return true;
        }

        public override string GetRejectMessage(Pawn pawn)
            => "MAP_MechanoidMechanitor.InstallableModule.Rejected".Translate(
                pawn.LabelShort, parent.LabelNoCount);

        public override string GetSuccessMessage(Pawn pawn)
            => "MAP_MechanoidMechanitor.InstallableModule.BionicCompanion.Success"
                .Translate(pawn.LabelShort);
    }
}
