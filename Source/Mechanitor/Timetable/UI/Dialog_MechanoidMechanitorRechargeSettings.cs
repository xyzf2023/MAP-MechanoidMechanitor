using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械族机械师“个人充电阈值”设置窗口。
    /// 这是原版 <see cref="Dialog_RechargeSettings"/> 的极薄适配层：
    /// 标题 / 说明 / 双端 FloatRange 滑条 / Cancel / Reset / OK 全部由原版负责，
    /// 本类不复制任何原版 DoWindowContents 内容。
    ///
    /// 数据适配方式：
    /// 构造一个仅用于本窗口的临时 <see cref="MechanitorControlGroup"/> proxy，
    /// 其 mechRechargeThresholds 初始值来自当前 Pawn 的个人阈值。
    /// 原版窗口只会在点击 OK 时写入 proxy.mechRechargeThresholds，
    /// 我们据此把结果同步到独立自律记录，普通机械体无需拥有机械师 Tracker。
    ///
    /// proxy 绝不加入 tracker.controlGroups、不 Assign 任何机械体、不参与带宽 / WorkMode / 存档，
    /// 窗口关闭后可自然被 GC。
    /// </summary>
    public class Dialog_MechanoidMechanitorRechargeSettings : Dialog_RechargeSettings
    {
        private readonly Pawn pawn;

        // 仅作为原版 Dialog 读写 mechRechargeThresholds 的临时数据载体，非真实控制组。
        private readonly MechanitorControlGroup proxyControlGroup;

        // 已同步到 Record 的值。原版仅在 OK 时修改 proxy，因此值变化即等价于“玩家点了 OK”。
        private FloatRange lastSyncedThresholds;

        public Dialog_MechanoidMechanitorRechargeSettings(Pawn pawn)
            : this(pawn, CreateProxyControlGroup(pawn))
        {
        }

        private Dialog_MechanoidMechanitorRechargeSettings(
            Pawn pawn,
            MechanitorControlGroup proxyControlGroup)
            : base(proxyControlGroup)
        {
            this.pawn = pawn;
            this.proxyControlGroup = proxyControlGroup;
            lastSyncedThresholds = proxyControlGroup.mechRechargeThresholds;
        }

        /// <summary>
        /// 建立临时 proxy 控制组：
        /// 原版构造器只做 tracker 赋值与 workMode = Work，不会注册自身、不会 Assign 机械体，
        /// 因此这里传入 Pawn 现有 tracker（可为 null）都是安全的，且不会修改真实控制组集合。
        /// </summary>
        private static MechanitorControlGroup CreateProxyControlGroup(Pawn pawn)
        {
            MechanitorControlGroup proxy = new MechanitorControlGroup(pawn?.mechanitor);
            proxy.mechRechargeThresholds =
                MechanoidMechanitorRechargeUtility.GetRechargeThresholds(pawn);
            return proxy;
        }

        public override void DoWindowContents(Rect inRect)
        {
            // 原版负责全部界面与按钮逻辑；OK 时原版会写 proxy.mechRechargeThresholds 并 Close。
            base.DoWindowContents(inRect);
            TryCommitProxyThresholds();
        }

        public override void PostClose()
        {
            base.PostClose();

            // 兜底：即使 OK 帧的提交检测被异常中断，也只在 proxy 值确实变化时写回。
            TryCommitProxyThresholds();
        }

        /// <summary>
        /// 仅在 proxy 值相对上次同步值发生变化时写回个人阈值。
        /// 拖动滑条 / Reset 都只改原版窗口内部 range，不动 proxy，
        /// 因此 Cancel、点击窗口外关闭、Reset 后 Cancel 都不会写入 Pawn。
        /// </summary>
        private void TryCommitProxyThresholds()
        {
            FloatRange current = proxyControlGroup.mechRechargeThresholds;
            if (current.min == lastSyncedThresholds.min
                && current.max == lastSyncedThresholds.max)
            {
                return;
            }

            lastSyncedThresholds = current;
            MechanoidMechanitorRechargeUtility.TrySetRechargeThresholds(pawn, current);
        }
    }
}
