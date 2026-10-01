using System;
using GD3;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.GD5
{
    // 与首次对话模块独立：文案和扣税不依赖是否已经播放首次对话。
    [StaticConstructorOnStartup]
    internal static class GD5CommunicationCompatibilityBootstrap
    {
        private const string HarmonyId = "xyzf.mechanoidmechanitor.gd5.communication";

        static GD5CommunicationCompatibilityBootstrap()
        {
            var harmony = new Harmony(HarmonyId);
            try
            {
                var tradeConstructor = AccessTools.Constructor(typeof(TradeWindow_BlackMech),
                    new[] { typeof(string), typeof(string), typeof(Map), typeof(Pawn) });
                var taxGetter = AccessTools.PropertyGetter(typeof(MissionComponent), nameof(MissionComponent.ShouldPayTax));
                var drawMethod = AccessTools.DeclaredMethod(typeof(MissionWindow), nameof(MissionWindow.DoWindowContents),
                    new[] { typeof(Rect) });
                if (tradeConstructor == null || taxGetter == null || taxGetter.ReturnType != typeof(bool)
                    || drawMethod == null || drawMethod.ReturnType != typeof(void))
                    throw new MissingMemberException("闪毁5通讯／税款接口结构已变化。");

                // 文案替换验证失败时不安装免税逻辑，防止显示与产出不一致。
                harmony.Patch(drawMethod, transpiler: new HarmonyMethod(typeof(GD5CommunicationCompatibilityPatch),
                    nameof(GD5CommunicationCompatibilityPatch.TaxTextTranspiler)));
                harmony.Patch(tradeConstructor, prefix: new HarmonyMethod(typeof(GD5CommunicationCompatibilityPatch),
                    nameof(GD5CommunicationCompatibilityPatch.TradeWindowPrefix)));
                harmony.Patch(taxGetter, postfix: new HarmonyMethod(typeof(GD5CommunicationCompatibilityPatch),
                    nameof(GD5CommunicationCompatibilityPatch.ShouldPayTaxPostfix)));
                if (MAPMechanitorMod.Settings?.enableStartupDetailedLogging == true)
                {
                    Log.Message("[MAP-GD5] 剧本交易文案与机械巢盟友情报免税兼容已加载。");
                }
            }
            catch (Exception exception)
            {
                harmony.UnpatchAll(HarmonyId);
                Log.Error("[MAP-GD5] 通讯／税款兼容安装失败，已撤销本模块补丁。\n" + exception);
            }
        }
    }
}
