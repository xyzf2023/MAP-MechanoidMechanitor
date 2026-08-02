using RimWorld;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 集中定义量产型机械培育仓明确支持的原版机械培育账单类型。
    /// 仅接管普通机械族生产与机械族复活，避免误处理其他 MOD 的未知 Bill_Mech 子类。
    /// </summary>
    internal static class MassProductionMechGestatorBillUtility
    {
        internal static bool IsSupported(Bill_Mech? bill)
        {
            return bill is Bill_ProductionMech || bill is Bill_ResurrectMech;
        }
    }
}
