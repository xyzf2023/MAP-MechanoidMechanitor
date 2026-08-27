using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械族机械师个人充电阈值的唯一读取入口。
    /// 所有个人阈值读取必须集中走这里，禁止在 Sleep JobGiver / Harmony Patch / UI 等位置
    /// 各自直接访问 Record 字段并重复计算。
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

            if (!GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(
                    pawn,
                    out MechanoidMechanitorRecord? record)
                || record == null)
            {
                return false;
            }

            thresholds = record.RechargeThresholds;
            return true;
        }

        /// <summary>
        /// 读取个人充电阈值（0~1 比例）。查不到 Record 时回退原版默认值。
        /// </summary>
        public static FloatRange GetRechargeThresholds(Pawn pawn)
        {
            return TryGetRechargeThresholds(pawn, out FloatRange thresholds)
                ? thresholds
                : MechanitorControlGroup.DefaultMechRechargeThresholds;
        }

        /// <summary>普通状态下开始自动充电的阈值（0~1 比例）。</summary>
        public static float GetStartRechargeThreshold(Pawn pawn)
        {
            return GetRechargeThresholds(pawn).min;
        }

        /// <summary>自动充电结束阈值（0~1 比例）。</summary>
        public static float GetStopRechargeThreshold(Pawn pawn)
        {
            return GetRechargeThresholds(pawn).max;
        }

        /// <summary>普通状态下开始自动充电的阈值（换算为绝对能量值）。</summary>
        public static int GetStartRechargeEnergy(Pawn pawn)
        {
            return Mathf.RoundToInt(
                pawn.RaceProps.maxMechEnergy * GetStartRechargeThreshold(pawn));
        }

        /// <summary>自动充电结束阈值（换算为绝对能量值）。</summary>
        public static float GetStopRechargeEnergy(Pawn pawn)
        {
            return pawn.RaceProps.maxMechEnergy * GetStopRechargeThreshold(pawn);
        }
    }
}
