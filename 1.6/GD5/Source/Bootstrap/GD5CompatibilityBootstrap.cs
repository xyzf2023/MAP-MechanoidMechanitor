using System;
using System.Linq;
using System.Collections.Generic;
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
        internal static GD5DialogueDef Cooperation = null!;
        internal static bool IsReady { get; private set; }

        static GD5CompatibilityBootstrap()
        {
            var harmony = new Harmony(HarmonyId);
            try
            {
                // 全部内容与入口校验成功后，再安装显式补丁。
                FirstContact = DefDatabase<GD5DialogueDef>.GetNamed("MAP_GD5_FirstContact");
                Cooperation = DefDatabase<GD5DialogueDef>.GetNamed("MAP_GD5_Cooperation");
                string[] errors = FirstContact.Validate().Concat(Cooperation.Validate()).ToArray();
                if (errors.Length != 0) throw new InvalidOperationException(string.Join("; ", errors));
                ValidateStage("Scripts_Begin", 0, null);
                ValidateStage("Scripts_PollutionDump", 100, null);
                ValidateStage("Scripts_200", 200, "GD_PollutionDump");
                ValidateStage("Scripts_300", 300, "GD_SubcoreAnalyse");
                GD5StoryTextOverrides.Initialize();
                MechanoidScriptDef cooperationTree = DefDatabase<MechanoidScriptDef>.GetNamed("Scripts_300");
                if (cooperationTree.scriptTree.Count < 9
                    || cooperationTree.scriptTree[3].buttons.Count != 1
                    || cooperationTree.scriptTree[3].buttons[0].action != "Continue"
                    || cooperationTree.scriptTree[3].buttons[0].jumpTo != -1)
                    throw new InvalidOperationException("Scripts_300 的后续通讯衔接结构已变化。");
                GD5StoryFlowService.CooperationOriginalTail(cooperationTree);
                if (cooperationTree.scriptTree[4].buttons.Count != 5
                    || cooperationTree.scriptTree[8].buttons.Count != 1
                    || cooperationTree.scriptTree[8].buttons[0].action != "Continue"
                    || cooperationTree.scriptTree[8].buttons[0].jumpTo != 4)
                    throw new InvalidOperationException("Scripts_300 身份问答选项或电话问答回跳已变化。");
                var communicationConstructor = AccessTools.Constructor(typeof(CommunicationWindow_BlackMech),
                    new[] { typeof(string), typeof(string), typeof(string), typeof(float), typeof(float),
                        typeof(Map), typeof(Pawn), typeof(List<ScriptButton>), typeof(List<ScriptTree>), typeof(int) });
                var target = AccessTools.DeclaredMethod(typeof(TradeWindow_BlackMech), "Option2", Type.EmptyTypes);
                var drawCommunication = AccessTools.DeclaredMethod(typeof(CommunicationWindow_BlackMech),
                    "DoWindowContents", new[] { typeof(UnityEngine.Rect) });
                if (communicationConstructor == null || target == null || target.ReturnType != typeof(void)
                    || drawCommunication == null || drawCommunication.ReturnType != typeof(void)
                    || AccessTools.Field(typeof(TradeWindow_BlackMech), "map")?.FieldType != typeof(Map)
                    || AccessTools.Field(typeof(TradeWindow_BlackMech), "pawn")?.FieldType != typeof(Pawn))
                    throw new MissingMemberException("闪毁5剧情入口结构已变化。");
                harmony.Patch(drawCommunication,
                    transpiler: new HarmonyMethod(typeof(GD5CommunicationLayoutPatch),
                        nameof(GD5CommunicationLayoutPatch.Transpiler)));
                harmony.Patch(communicationConstructor,
                    prefix: new HarmonyMethod(typeof(GD5StoryCompatibilityPatch),
                        nameof(GD5StoryCompatibilityPatch.CommunicationConstructorPrefix)));
                harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(GD5StoryCompatibilityPatch), nameof(GD5StoryCompatibilityPatch.Prefix)));
                IsReady = true;
                if (MAPMechanitorMod.Settings?.enableStartupDetailedLogging == true)
                {
                    Log.Message("[MAP-GD5] 黑衣首次通讯联动已加载，仅在机械族机械师剧本启用。");
                }
            }
            catch (Exception exception)
            {
                harmony.UnpatchAll(HarmonyId);
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
