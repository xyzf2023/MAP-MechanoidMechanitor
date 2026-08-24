using System.Reflection;
using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    // 职责：在原版主脑终局任务“任务可用”信发送完成后，追加本 MOD 的中性情报信。
    // 仅响应奥德赛 Gravcore_Mechhive 终局任务的真实生成与原始任务信发送，绝不修改原版行为。
    [HarmonyPatch]
    public static class CerebrexBossQuestDiscoveryLetterPatch
    {
        private static readonly string QuestDefNameGravcoreMechhive = "Gravcore_Mechhive";

        private static readonly string LetterLabelKey = "MAP_CerebrexBoss.Discovery.Letter.Label";
        private static readonly string LetterCommonKey = "MAP_CerebrexBoss.Discovery.Letter.Common";
        private static readonly string LetterSymbiosisKey = "MAP_CerebrexBoss.Discovery.Letter.Symbiosis";
        private static readonly string LetterPurgeKey = "MAP_CerebrexBoss.Discovery.Letter.Purge";

        private static MethodInfo TargetMethod()
        {
            return AccessTools.Method(
                typeof(QuestUtility),
                nameof(QuestUtility.SendLetterQuestAvailable),
                new[] { typeof(Quest), typeof(string) });
        }

        [HarmonyPostfix]
        public static void SendLetterQuestAvailable_Postfix(Quest quest)
        {
            // 1. 仅奥德赛 DLC 激活时处理。
            if (!ModsConfig.OdysseyActive)
            {
                return;
            }

            // 2. 任务不可为空。
            if (quest == null)
            {
                return;
            }

            // 3. 任务根定义不可为空。
            if (quest.root == null)
            {
                return;
            }

            // 4. 仅奥德赛机械主巢终局任务触发，使用严格字符串相等。
            if (quest.root.defName != QuestDefNameGravcoreMechhive)
            {
                return;
            }

            string textKey = ResolveLetterTextKey();

            // 在原版任务信之后，发送灰色中性情报信。
            Find.LetterStack.ReceiveLetter(
                LetterMaker.MakeLetter(
                    LetterLabelKey.Translate(),
                    textKey.Translate(),
                    LetterDefOf.NeutralEvent,
                    LookTargets.Invalid,
                    null,
                    quest));
        }

        // 只返回翻译 Key，不直接拼接中文文本。
        private static string ResolveLetterTextKey()
        {
            // A. 肃清指令优先。
            if (GameComponent_MechanoidMechanitorStoryState.IsPurgeDirectiveActive)
            {
                return LetterPurgeKey;
            }

            // B. 共生盟约版本：盟约激活、状态对象存在、盟约等级达到 L2、至少一名成员。
            if (GameComponent_SymbiosisCovenantState.IsActive
                && GameComponent_SymbiosisCovenantState.CurrentComponent != null
                && GameComponent_SymbiosisCovenantState.CurrentComponent.CovenantLevel >= 2
                && GameComponent_SymbiosisCovenantState.CurrentComponent.CovenantMemberCount >= 1)
            {
                return LetterSymbiosisKey;
            }

            // C. 普通版本：所有其他情况。
            return LetterCommonKey;
        }
    }
}
