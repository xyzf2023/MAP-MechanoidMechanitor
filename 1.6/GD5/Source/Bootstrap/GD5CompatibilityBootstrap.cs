using System;
using System.Linq;
using GD3;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor.GD5
{
    [StaticConstructorOnStartup]
    internal static class GD5CompatibilityBootstrap
    {
        private const string HarmonyId = "xyzf.mechanoidmechanitor.gd5.story";
        internal static GD5DialogueDef FirstContact = null!;

        static GD5CompatibilityBootstrap()
        {
            try
            {
                // 全部内容与入口校验成功后，只安装这一处显式补丁。
                FirstContact = DefDatabase<GD5DialogueDef>.GetNamed("MAP_GD5_FirstContact");
                string[] errors = FirstContact.Validate().ToArray();
                if (errors.Length != 0) throw new InvalidOperationException(string.Join("; ", errors));
                ValidateStage("Scripts_Begin", 0, null);
                ValidateStage("Scripts_PollutionDump", 100, null);
                ValidateStage("Scripts_200", 200, "GD_PollutionDump");
                ValidateStage("Scripts_300", 300, "GD_SubcoreAnalyse");
                var target = AccessTools.DeclaredMethod(typeof(TradeWindow_BlackMech), "Option2", Type.EmptyTypes);
                if (target == null || target.ReturnType != typeof(void)
                    || AccessTools.Field(typeof(TradeWindow_BlackMech), "map")?.FieldType != typeof(Map)
                    || AccessTools.Field(typeof(TradeWindow_BlackMech), "pawn")?.FieldType != typeof(Pawn))
                    throw new MissingMemberException("闪毁5剧情入口结构已变化。");
                new Harmony(HarmonyId).Patch(target,
                    prefix: new HarmonyMethod(typeof(GD5StoryCompatibilityPatch), nameof(GD5StoryCompatibilityPatch.Prefix)));
                Log.Message("[MAP-GD5] 黑衣首次通讯联动已加载，仅在机械族机械师剧本启用。");
            }
            catch (Exception exception)
            {
                Log.Error("[MAP-GD5] 黑衣剧情联动安装失败。\n" + exception);
            }
        }

        private static void ValidateStage(string name, int id, string? requiredQuest)
        {
            MechanoidScriptDef tree = DefDatabase<MechanoidScriptDef>.GetNamed(name);
            if (tree.ID != id || tree.priceKind != "None" || tree.branch != null
                || tree.questNeedToFinish?.defName != requiredQuest
                || tree.scriptTree == null || tree.scriptTree.Count == 0
                || tree.scriptTree.Any(n => n == null || n.buttons == null || n.buttons.Count == 0))
                throw new InvalidOperationException("闪毁5剧情结构不符合预期：" + name);
        }
    }
}
