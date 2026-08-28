using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompProperties_AutonomousDirectiveCoreInstallEffect
        : CompProperties_MechanoidModuleInstallEffect
    {
        public CompProperties_AutonomousDirectiveCoreInstallEffect()
            : base(typeof(CompAutonomousDirectiveCoreInstallEffect))
        {
        }
    }

    /// <summary>
    /// 自律指令核心的具体安装效果：升格为后天机械族机械师。
    /// 复用 MechanoidMechanitorRoleUtility，不复制升格逻辑。
    /// </summary>
    public class CompAutonomousDirectiveCoreInstallEffect
        : CompMechanoidModuleInstallEffect
    {
        private const string AlreadyMechanitorSuffix = "：已经是机械族机械师";

        public override bool ShouldShowOption(Pawn pawn, out string? disabledReason)
        {
            disabledReason = null;

            if (MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                disabledReason = AlreadyMechanitorSuffix;
                return true;
            }

            // 不满足升格条件且又不是机械族机械师时，保持现有行为：不显示安装选项。
            if (!MechanoidMechanitorRoleUtility.CanBecomeAcquiredMechanoidMechanitor(pawn))
            {
                return false;
            }

            return true;
        }

        public override AcceptanceReport CanInstallNow(Pawn pawn)
        {
            if (MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                return AcceptanceReport.WasRejected;
            }

            if (!MechanoidMechanitorRoleUtility.CanBecomeAcquiredMechanoidMechanitor(pawn))
            {
                return AcceptanceReport.WasRejected;
            }

            return AcceptanceReport.WasAccepted;
        }

        public override bool TryApply(Pawn pawn)
        {
            if (!MechanoidMechanitorRoleUtility.PromoteToAcquiredMechanoidMechanitor(pawn)
                || !MechanoidMechanitorRoleUtility.IsAcquiredMechanoidMechanitor(pawn))
            {
                return false;
            }

            // 升格成功但标识 Hediff 没有同步时，保留现有错误日志（不影响成功提示）。
            if (!MechanoidMechanitorRoleUtility.HasAcquiredMechanitorHediff(pawn))
            {
                Log.Error(
                    "[MAP-机械族机械师] 自律指令核心升格成功但标识健康状态同步失败：" +
                    $"pawn={pawn.LabelShort}（{pawn.ThingID}）。");
            }

            return true;
        }

        public override string GetRejectMessage(Pawn pawn)
            => $"{pawn.LabelShort}无法安装{parent.LabelNoCount}。";

        public override string GetSuccessMessage(Pawn pawn)
            => $"{pawn.LabelShort}已成为机械族机械师。";
    }
}
