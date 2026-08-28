using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 机械族机械师专用剧本的开局弹窗文本替换。
    /// <para>
    /// 原版 <see cref="ScenPart_GameStartDialog.PostGameStart"/> 只会在 Game.InitNewGame 中随
    /// Scenario.PostGameStart 触发一次，因此本补丁天然只作用于新存档首次进入地图时的开局弹窗，
    /// 读档不会再次触发，也不会新建第二个弹窗。
    /// </para>
    /// <para>
    /// 玩家在剧情风格页面/自定义页面完成选择后，
    /// GameComponent_MechanoidMechanitorStoryState.SetStoryStyleForNewGame 已经写入本局最终生效的
    /// symbiosisCovenantEnabled 与 purgeDirectiveEnabled，所以这里只依据 CurrentConfiguration 的
    /// 最终布尔值判断，不依赖 SelectedStoryStyle，
    /// 以避免“自定义”剧情风格最终只启用其中一个组件时被误判。
    /// </para>
    /// <para>
    /// 识别条件只使用剧情状态组件已提供的“机械族机械师专用剧本配置来源”，
    /// 不使用 Scenario 的名称、标签、当前文本内容或硬编码 defName。
    /// </para>
    /// <para>
    /// 实现方式：Prefix 临时替换私有 text 字段，让原版直接用该文本创建 DiaNode；
    /// Postfix 无条件把 text 还原为 Prefix 保存的原始值，
    /// 避免把本次动态选择的文字永久写回 Scenario 对象（该对象会被存档序列化）。
    /// 原版方法中创建 DiaNode、Dialog_NodeTree、关闭音效、音乐暂停/恢复、
    /// 历史记录、游戏速度恢复与教程事件的逻辑全部由原版自己执行，本补丁不介入。
    /// </para>
    /// </summary>
    [HarmonyPatch(
        typeof(ScenPart_GameStartDialog),
        nameof(ScenPart_GameStartDialog.PostGameStart))]
    public static class MechanoidMechanitorStory_ScenPart_GameStartDialog_PostGameStart_Patch
    {
        private const string SymbiosisCovenantTextKey =
            "MAP_MechanoidMechanitor.Scenario.GameStartDialog.SymbiosisCovenant";

        private const string PurgeDirectiveTextKey =
            "MAP_MechanoidMechanitor.Scenario.GameStartDialog.PurgeDirective";

        [HarmonyPrefix]
        public static void Prefix(ref string ___text, out string __state)
        {
            // 所有路径都先保存原始 text，保证 Postfix 总能还原。
            __state = ___text;

            if (!GameComponent_MechanoidMechanitorStoryState
                    .IsMechanoidMechanitorScenarioStoryConfiguration)
            {
                return;
            }

            MechanoidMechanitorStoryConfiguration? configuration =
                GameComponent_MechanoidMechanitorStoryState.CurrentConfiguration;
            if (configuration == null)
            {
                return;
            }

            // 只有恰好启用其中一个剧情组件时才替换文本。
            // 两者都为 false 与两者都为 true 的组合都不处理，
            // 直接沿用 Scenarios_Justice.xml 中现有的普通机械族机械师开局文本：
            // 不写回普通文本常量（未来 XML 文本被编辑时无需同步修改 C#），
            // 不发信、不弹窗、不记录日志、不修改任一开关。
            if (configuration.symbiosisCovenantEnabled
                && !configuration.purgeDirectiveEnabled)
            {
                ___text = SymbiosisCovenantTextKey.Translate();
            }
            else if (configuration.purgeDirectiveEnabled
                && !configuration.symbiosisCovenantEnabled)
            {
                ___text = PurgeDirectiveTextKey.Translate();
            }
        }

        [HarmonyPostfix]
        public static void Postfix(ref string ___text, string __state)
        {
            ___text = __state;
        }
    }
}
