using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械族机械师“个人充电阈值”按钮的 Inspect Pane（左下角检查面板）顶部按钮绘制工具。
    /// 职责严格限制为：判断是否显示、缓存原版充电图标、绘制 24×24 按钮、打开个人阈值窗口。
    /// 不新增 Gizmo、不新增 Assign/Schedule 表列、不修改任何充电 AI。
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class MechanoidMechanitorRechargeInspectPaneUtility
    {
        // 与原版 Inspect Pane 顶部小按钮（遇敌反应等）完全一致的尺寸。
        internal const float ButtonSize = 24f;

        // 直接复用原版机械控制组充电设置图标资源
        // （原版 MechanitorControlGroupGizmo.PowerIcon 使用同一路径），
        // 不复制贴图、不新增近似图标、不反射原版 private 字段。
        private const string RechargeIconPath = "UI/Icons/MechRechargeSettings";

        private const int ErrorKeyMissingIcon = 879347210;
        private const int ErrorKeyDrawFailed = 879347211;

        private const string LogPrefix =
            "[MAP-机械族机械师] MechanoidMechanitorRechargeInspectPaneUtility：";

        private static readonly Texture2D? cachedIcon =
            ContentFinder<Texture2D>.Get(RechargeIconPath, reportFailure: false);

        /// <summary>
        /// 原版图标缓存。启动阶段已解析一次；若缺失则为 null，绘制时最多记录一次。
        /// </summary>
        private static Texture2D? Icon => cachedIcon;

        /// <summary>
        /// 按钮显示条件来自独立自律资格与能源需求，不要求机械师身份或遇敌反应按钮。
        /// </summary>
        internal static bool ShouldShowRechargeButton(Pawn? pawn)
        {
            return pawn != null
                && !pawn.Destroyed
                && pawn.health != null
                && !pawn.health.Dead
                && pawn.Faction != null
                && pawn.Faction.IsPlayerSafe()
                && AutonomousMechUtility.UsesPersonalRechargeSettings(pawn);
        }

        /// <summary>
        /// 在 Inspect Pane 顶部按钮流水线中绘制个人充电阈值按钮，并返回推进后的 x。
        /// 由 MainTabWindow_Inspect.DoInspectPaneButtons 的 Transpiler 在原版
        /// 遇敌反应按钮之后调用，因此两个按钮必然左右相邻。
        /// 不合格 Pawn 直接返回原 x，且不修改 lineEndWidth，普通 Pawn 的原版 UI 完全不变。
        /// </summary>
        internal static float DrawRechargeButtonAndAdvance(
            Pawn? pawn,
            float x,
            ref float lineEndWidth)
        {
            if (!ShouldShowRechargeButton(pawn))
            {
                return x;
            }

            Texture2D? icon = Icon;
            if (icon == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}未找到原版图标 {RechargeIconPath}，" +
                    "个人充电阈值按钮不会显示。",
                    ErrorKeyMissingIcon);
                return x;
            }

            float buttonX = x - ButtonSize;
            Rect buttonRect = new Rect(buttonX, 0f, ButtonSize, ButtonSize);

            try
            {
                if (Mouse.IsOver(buttonRect))
                {
                    // 复用原版充电设置窗口标题翻译键，不新增重复文案。
                    TooltipHandler.TipRegion(
                        buttonRect,
                        "MechRechargeSettingsTitle".Translate());
                }

                // 原版 Inspect Pane 小按钮标准 API：自带 hover 变色与 mouseover 音效，
                // 图标不额外染色，与原版机械控制组充电按钮观感一致。
                if (Widgets.ButtonImage(buttonRect, icon))
                {
                    Find.WindowStack.Add(
                        new Dialog_MechanoidMechanitorRechargeSettings(pawn!));
                }
            }
            catch (Exception ex)
            {
                // Inspect Pane 由 InspectPaneUtility 统一 catch，异常会表现为整个面板报错，
                // 因此这里必须自行吞掉异常并保持布局推进一致。
                Log.ErrorOnce(
                    $"{LogPrefix}绘制个人充电阈值按钮时出现异常：{ex}",
                    ErrorKeyDrawFailed);
            }

            lineEndWidth += ButtonSize;
            return buttonX;
        }
    }
}
