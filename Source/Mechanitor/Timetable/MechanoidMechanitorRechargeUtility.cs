using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 自律机械体个人充电阈值的唯一读取 / 写入 / 合法化入口。
    /// 保留原类名兼容机械师作息和 UI 调用；权威数据位于独立自律注册表。
    /// 所有个人阈值读取必须集中走这里，禁止在 Sleep JobGiver / Harmony Patch / UI 等位置
    /// 各自直接访问 Record 字段并重复计算；所有写入必须走 TrySetRechargeThresholds。
    /// 查不到 Record 时回退原版 DefaultMechRechargeThresholds，不抛异常。
    /// </summary>
    public static class MechanoidMechanitorRechargeUtility
    {
        /// <summary>
        /// 读取个人充电阈值（0~1 比例）。失败（查不到 Record）时回退原版默认值。
        /// </summary>
        public static bool TryGetRechargeThresholds(Pawn? pawn, out FloatRange thresholds)
        {
            thresholds = MechanitorControlGroup.DefaultMechRechargeThresholds;

            if (!GameComponent_AutonomousMechRegistry.TryGetRecord(
                    pawn,
                    out AutonomousMechAuthorizationRecord? record)
                || record == null)
            {
                return false;
            }

            thresholds = record.RechargeThresholds;
            return true;
        }

        /// <summary>
        /// 个人充电阈值的唯一写入入口。UI / 其他调用方禁止直接写 Record 字段。
        /// 写入前统一走 <see cref="SanitizeRechargeThresholds"/>，
        /// 与存档 PostLoad 使用完全相同的一套合法化规则。
        /// 仅修改阈值数据，不会中断 / 触发任何充电 Job。
        /// </summary>
        public static bool TrySetRechargeThresholds(Pawn? pawn, FloatRange thresholds)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return false;
            }

            return GameComponent_AutonomousMechRegistry.TrySetRechargeThresholds(pawn, thresholds);
        }

        /// <summary>
        /// 个人充电阈值的唯一合法化实现：
        /// 拦截 NaN / Infinity、min/max 钳制到 0~1、min&gt;max 时交换。
        /// 数据完全非法时回退原版 DefaultMechRechargeThresholds，不抛异常。
        /// MechanoidMechanitorRecord.SanitizeAfterLoad 与 TrySetRechargeThresholds 共用本方法，
        /// 禁止在其他位置复制第二份合法化规则。
        /// </summary>
        internal static FloatRange SanitizeRechargeThresholds(FloatRange value)
        {
            FloatRange fallback = MechanitorControlGroup.DefaultMechRechargeThresholds;

            if (float.IsNaN(value.min)
                || float.IsNaN(value.max)
                || float.IsInfinity(value.min)
                || float.IsInfinity(value.max))
            {
                return fallback;
            }

            float min = Mathf.Clamp(value.min, 0f, 1f);
            float max = Mathf.Clamp(value.max, 0f, 1f);

            // 若越界则交换，避免下游充电判定进入 min>max 的非法区间。
            if (min > max)
            {
                (min, max) = (max, min);
            }

            return new FloatRange(min, max);
        }

        /// <summary>
        /// 读取个人充电阈值（0~1 比例）。查不到 Record 时回退原版默认值。
        /// </summary>
        public static FloatRange GetRechargeThresholds(Pawn? pawn)
        {
            // pawn 为 null 时不查 Record，直接回退原版默认值，避免下游访问 RaceProps 抛 NRE。
            if (pawn == null)
            {
                return MechanitorControlGroup.DefaultMechRechargeThresholds;
            }

            return TryGetRechargeThresholds(pawn, out FloatRange thresholds)
                ? thresholds
                : MechanitorControlGroup.DefaultMechRechargeThresholds;
        }

        /// <summary>普通状态下开始自动充电的阈值（0~1 比例）。</summary>
        public static float GetStartRechargeThreshold(Pawn? pawn)
        {
            return GetRechargeThresholds(pawn).min;
        }

        /// <summary>自动充电结束阈值（0~1 比例）。</summary>
        public static float GetStopRechargeThreshold(Pawn? pawn)
        {
            return GetRechargeThresholds(pawn).max;
        }

        /// <summary>普通状态下开始自动充电的阈值（换算为绝对能量值）。</summary>
        public static int GetStartRechargeEnergy(Pawn? pawn)
        {
            if (pawn == null)
            {
                return 0;
            }

            return Mathf.RoundToInt(
                pawn.RaceProps.maxMechEnergy * GetStartRechargeThreshold(pawn));
        }

        /// <summary>自动充电结束阈值（换算为绝对能量值）。</summary>
        public static float GetStopRechargeEnergy(Pawn? pawn)
        {
            if (pawn == null)
            {
                return 0f;
            }

            return pawn.RaceProps.maxMechEnergy * GetStopRechargeThreshold(pawn);
        }
    }
}
