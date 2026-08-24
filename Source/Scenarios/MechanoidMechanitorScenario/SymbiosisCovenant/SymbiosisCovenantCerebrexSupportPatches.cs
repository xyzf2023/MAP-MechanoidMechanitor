using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 主脑盟约支援的窄范围 Harmony 注入：
    /// 1) 在原版 Gravcore_Mechhive 任务生成完成后，注入附加的支援 QuestPart（仅 Odyssey 且根任务严格为 Gravcore_Mechhive）。
    /// 2) 主脑防御解除事件转发给支援系统（实际调用写在 CerebrexBossPatches 的 LowerDefences Postfix 中）。
    /// 不修改原版 RunInt 的参数、返回值、Slate、信件与 QuestPart。
    /// </summary>
    [HarmonyPatch]
    public static class QuestNode_Root_Gravcore_Mechhive_RunInt_Patch
    {
        public static bool Prepare()
        {
            return ModsConfig.OdysseyActive;
        }

        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(QuestNode_Root_Gravcore_Mechhive), "RunInt")
                ?? throw new InvalidOperationException("QuestNode_Root_Gravcore_Mechhive.RunInt not found");
        }

        public static void Postfix()
        {
            if (!ModsConfig.OdysseyActive)
            {
                return;
            }

            Quest quest = QuestGen.quest;
            Site? site = QuestGen.slate.Get<Site>("site");
            if (quest == null || site == null)
            {
                return;
            }

            if (quest.root == null || quest.root.defName != "Gravcore_Mechhive")
            {
                return;
            }

            if (site.MainSitePartDef != SitePartDefOf.OrbitalMechhive)
            {
                return;
            }

            // 禁止重复添加。
            if (quest.PartsListForReading.Any(p => p is QuestPart_SymbiosisCovenantCerebrexSupport))
            {
                return;
            }

            // 复用原版生成的 site.MapGenerated 精确信号。
            string signal = QuestGenUtility.HardcodedSignalWithQuestID("site.MapGenerated");

            QuestPart_SymbiosisCovenantCerebrexSupport part = new QuestPart_SymbiosisCovenantCerebrexSupport
            {
                site = site,
                inSignalEnable = signal,
                mapGeneratedSignal = signal,
                offerId = quest.id + "_CerebrexSupport"
            };

            quest.AddPart(part);

            Log.Message($"[MAP][SymbiosisCovenantCerebrexSupport] 已注入主脑援军支援 QuestPart（quest={quest.id}）。");
        }
    }
}
