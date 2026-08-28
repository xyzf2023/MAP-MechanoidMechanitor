using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 具体安装效果组件属性基类。子类通过构造函数传入自己的 compClass。
    /// </summary>
    public abstract class CompProperties_MechanoidModuleInstallEffect : CompProperties
    {
        protected CompProperties_MechanoidModuleInstallEffect(System.Type compClass)
        {
            this.compClass = compClass;
        }
    }

    /// <summary>
    /// 机械族模块具体安装效果的抽象基类。
    /// 通用流程不判断具体 ThingDef defName，也不使用字符串 switch 分发；
    /// 新增第三种模块时只需新增具体效果组件与 XML 配置。
    /// 具体效果组件不得自行销毁物品，销毁由通用安装流程在效果成功后统一执行。
    /// </summary>
    public abstract class CompMechanoidModuleInstallEffect : ThingComp
    {
        /// <summary>
        /// 右键菜单是否应显示该安装选项。
        /// 返回 false 时不显示任何选项；
        /// 返回 true 且 disabledReason 非空时显示禁用选项（通常为已安装状态）。
        /// </summary>
        public abstract bool ShouldShowOption(Pawn pawn, out string? disabledReason);

        /// <summary>
        /// 当前是否允许安装；拒绝时返回 AcceptanceReport.WasRejected。
        /// </summary>
        public abstract AcceptanceReport CanInstallNow(Pawn pawn);

        /// <summary>
        /// 执行具体安装效果；仅返回 true 表示成功。不得自行销毁物品。
        /// </summary>
        public abstract bool TryApply(Pawn pawn);

        /// <summary>
        /// 安装失败时发送的拒绝消息（含 Pawn 标识与模块标签）。
        /// </summary>
        public abstract string GetRejectMessage(Pawn pawn);

        /// <summary>
        /// 安装成功时发送的成功消息。
        /// </summary>
        public abstract string GetSuccessMessage(Pawn pawn);
    }
}
