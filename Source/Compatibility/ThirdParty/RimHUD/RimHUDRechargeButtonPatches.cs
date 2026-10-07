using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.RimHUD
{
    /// <summary>仅由兼容注册表显式安装；不使用 HarmonyPatch/PatchAll 绑定第三方类型。</summary>
    internal static class RimHUDRechargeButtonPatches
    {
        internal delegate Rect RowRectGetter(Rect bounds, ref float offset, float width, float height);

        internal static bool Enabled;
        private static RowRectGetter? getRowRect;
        private const int ErrorKeyDrawFailed = 879347230;

        internal static void Configure(RowRectGetter getter) => getRowRect = getter;

        internal static void Disable()
        {
            Enabled = false;
            getRowRect = null;
        }

        // 使用参数索引绑定，避免依赖第三方参数名。只在上游原方法实际执行后追加。
        public static void Postfix(Rect __0, IInspectPane __1, ref float __2, bool __runOriginal)
        {
            RowRectGetter? rowLayout = getRowRect;
            if (!Enabled || !__runOriginal || rowLayout == null)
                return;

            float originalOffset = __2;
            try
            {
                if (__1 == null || !__1.AnythingSelected || Find.Selector == null
                    || Find.Selector.NumSelected != 1
                    || !(Find.Selector.SingleSelectedThing is Pawn pawn)
                    || !MechanoidMechanitorRechargeInspectPaneUtility.ShouldShowRechargeButton(pawn))
                    return;

                // 上游负责矩形坐标、垂直居中和间距；offset 会继续参与角色名称宽度计算。
                float size = MechanoidMechanitorRechargeInspectPaneUtility.ButtonSize;
                Rect buttonRect = rowLayout(__0, ref __2, size, size);
                if (!MechanoidMechanitorRechargeInspectPaneUtility.TryDrawRechargeButton(pawn, buttonRect))
                    __2 = originalOffset;
            }
            catch (Exception ex)
            {
                // 仅处理本 Postfix 的异常，不吞上游异常；布局失败时恢复本次新增的宽度。
                __2 = originalOffset;
                Log.ErrorOnce(
                    $"[MAP-机械族机械师] RimHUD 个人充电阈值按钮绘制失败：{ex}", ErrorKeyDrawFailed);
            }
        }
    }
}
