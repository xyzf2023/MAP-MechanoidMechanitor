using System.Collections.Generic;
using System.Linq;
using GD3;
using LudeonTK;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.GD5
{
    public static class GD5StoryDebugActions
    {
        [DebugAction("MAP-机械族机械师", "闪毁5剧情测试",
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static List<DebugActionNode> Actions()
        {
            return new List<DebugActionNode>
            {
                new DebugActionNode("直接和平完成黑衣线（毒蜂结局测试）", DebugActionType.Action, CompletePeacefulStory),
                new DebugActionNode("完成当前任务并进入下一对话", DebugActionType.Action, Advance),
                new DebugActionNode("打开枯海对话（true）", DebugActionType.Action,
                    () => OpenDrysea("Scripts_Apocriton_true")),
                new DebugActionNode("打开枯海对话（false）", DebugActionType.Action,
                    () => OpenDrysea("Scripts_Apocriton_false"))
            };
        }

        private static bool TryContext(out MissionComponent mission, out Map map, out Pawn speaker)
        {
            mission = null!;
            map = null!;
            speaker = null!;
            if (!Prefs.DevMode || !GD5StoryFlowService.IsEnabled)
                return Reject("请在机械族机械师剧本中启用开发者模式后使用。");
            if (Find.CurrentMap == null)
                return Reject("请先进入地图，并选择要进行通讯的玩家单位。");
            if (Find.WindowStack.Windows.Any(w => w is GD5StoryDialog
                || w is CommunicationWindow_BlackMech || w is GraphicWindow
                || w is MissionWindow || w is TradeWindow_BlackMech))
                return Reject("请先结束或关闭当前通讯，再推进下一阶段。");
            if (!GD5CompatibilityBootstrap.IsReady)
                return Reject("联动剧情内容尚未初始化，请检查加载错误。");

            map = Find.CurrentMap;
            Pawn? selected = Find.Selector.SingleSelectedThing as Pawn;
            if (selected != null)
            {
                if (!selected.Spawned || selected.Map != map || selected.Dead || selected.Faction != Faction.OfPlayer)
                    return Reject("请选择当前地图上的存活玩家单位作为通讯者。");
                speaker = selected;
            }
            else
            {
                // 未选中 Pawn 时优先使用机械族机械师，便于连续测试。
                var candidates = map.mapPawns.AllPawnsSpawned
                    .Where(p => !p.Dead && p.Faction == Faction.OfPlayer).ToList();
                speaker = candidates.FirstOrDefault(p => MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(p))
                    ?? candidates.FirstOrDefault()!;
                if (speaker == null) return Reject("当前地图没有可用的玩家通讯者。");
            }
            mission = Find.World.GetComponent<MissionComponent>();
            return mission != null || Reject("未找到闪毁5剧情状态。");
        }

        private static void CompletePeacefulStory()
        {
            if (!TryContext(out MissionComponent mission, out _, out _)) return;
            var state = GameComponent_GD5StoryState.Current;
            MainComponent? main = Find.World.GetComponent<MainComponent>();
            Faction? hive = GD5BlackHiveEndingService.Hive;
            MechanoidScriptDef? finale = DefDatabase<MechanoidScriptDef>.GetNamedSilentFail("Scripts_1200");
            if (!GD5BlackHiveEndingBootstrap.IsReady || state == null || main == null
                || hive == null || finale == null || finale.ID != 1200)
            {
                Reject("毒蜂结局、黑衣派系或收尾剧情尚未正确初始化，请检查加载错误。");
                return;
            }
            if (state.HasVisit)
            {
                Reject("已有毒蜂预约、来访或结局过渡，请完成本次流程后再设置测试进度。");
                return;
            }
            // 不伪造已击杀毒蜂的历史，也不改动正在进行的终战任务。
            if (mission.apocritonDead || Find.QuestManager.QuestsListForReading.Any(q =>
                q.root?.defName == "GD_Quest_BlackApocriton"
                && (!q.Historical || q.State == QuestState.EndedSuccess)))
            {
                Reject("当前存档有毒蜂死亡记录，或存在未结束／已成功的终战任务；请使用尚未进入终战的测试存档。");
                return;
            }

            // 只处理黑衣主线截至 1200 的正常阶段，排除 ID=-1 的事件对话。
            var scripts = DefDatabase<MechanoidScriptDef>.AllDefsListForReading
                .Where(s => s.ID >= 0 && s.ID <= finale.ID).OrderBy(s => s.ID).ToList();
            var questDefs = new HashSet<QuestScriptDef>(scripts.Where(s => s.questNeedToFinish != null)
                .Select(s => s.questNeedToFinish));
            var pendingQuests = Find.QuestManager.QuestsListForReading
                .Where(q => q.root != null && questDefs.Contains(q.root) && !q.Historical).ToList();
            // 使用与逐阶段 DEV 推进相同的原版任务结算入口，不生成尚不存在的任务。
            foreach (Quest quest in pendingQuests)
                quest.End(QuestEndOutcome.Success, sendLetter: false, playSound: false);

            mission.script_Finished ??= new List<int>();
            mission.script_Allowed ??= new List<int>();
            foreach (MechanoidScriptDef script in scripts)
            {
                if (!mission.script_Finished.Contains(script.ID)) mission.script_Finished.Add(script.ID);
                if (!mission.script_Allowed.Contains(script.ID)) mission.script_Allowed.Add(script.ID);
            }
            GD5StoryFlowService.CompleteFirstContact();
            mission.blackMechDiscoverd = true;
            mission.scriptEnded = true;
            main.list_str ??= new List<string>();
            if (!main.list_str.Contains("ScriptFinished")) main.list_str.Add("ScriptFinished");

            // 这是和平结局测试准备：一并解除原 MOD 的敌对锁，避免入口仍被派系关系禁用。
            mission.relation = 0;
            mission.factionRelationLock = false;
            if (!GD5BlackHiveEndingService.IsFriendly)
                hive.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Neutral, canSendHostilityLetter: false);

            if (!GD5BlackHiveEndingService.IsUnlocked || !GD5BlackHiveEndingService.IsFriendly)
            {
                Reject("主线进度已设置，但毒蜂结局仍未解锁或派系仍然敌对，请检查其他 MOD 的状态改写。");
                return;
            }
            Messages.Message("[DEV] 已和平完成黑衣线，主线进度为“已完成”。可在机械通讯台选择“联络黑衣毒蜂”测试结局。",
                MessageTypeDefOf.TaskCompletion, false);
        }

        private static void Advance()
        {
            if (!TryContext(out MissionComponent mission, out Map map, out Pawn speaker)) return;
            // 原 Script 返回最早尚未打开的阶段，通常就是当前任务结束后要进入的通讯。
            MechanoidScriptDef? script = mission.Script;
            if (mission.scriptEnded || script == null)
            {
                Reject("黑衣主线已结束，没有下一对话阶段。");
                return;
            }
            int index = EntryIndex(mission, script);
            if (!ValidNode(script, index)) return;

            if (script.questNeedToFinish != null)
            {
                // 仅结束本阶段前置的未归档任务，不重写已经结束的任务历史。
                // 快照避免 Quest.End 清理任务时改变枚举中的集合。
                var quests = Find.QuestManager.QuestsListForReading
                    .Where(q => q.root == script.questNeedToFinish && !q.Historical).ToList();
                foreach (Quest quest in quests)
                    quest.End(QuestEndOutcome.Success, sendLetter: false, playSound: false);
            }
            mission.blackMechDiscoverd = true;
            if (!mission.script_Allowed.Contains(script.ID)) mission.script_Allowed.Add(script.ID);

            // 开头和 300 必须走实际联动入口，否则会漏掉首次通讯标记或角色分支。
            if (script.defName == "Scripts_Begin"
                || (script.defName == "Scripts_300" && GameComponent_GD5StoryState.Current?.firstContactCompleted == true))
            {
                var entry = new TradeWindow_BlackMech("", "", map, speaker);
                entry.Option2();
            }
            else
            {
                OpenOriginal(script, index, map, speaker);
                if (!mission.script_Finished.Contains(script.ID)) mission.script_Finished.Add(script.ID);
            }
            Messages.Message("[DEV] 已满足前置并打开 " + script.defName + "（" + script.ID
                + "）；通讯者：" + speaker.LabelShort + "。", MessageTypeDefOf.TaskCompletion, false);
        }

        private static void OpenDrysea(string defName)
        {
            if (!TryContext(out MissionComponent mission, out Map map, out Pawn speaker)) return;
            if (GameComponent_GD5StoryState.Current?.firstContactCompleted != true)
            {
                Reject("请先通过推进按钮完成首次联动通讯，再测试枯海替换。");
                return;
            }
            MechanoidScriptDef? script = DefDatabase<MechanoidScriptDef>.GetNamedSilentFail(defName);
            if (script == null || !ValidNode(script, 0)) return;
            // ID=-1 的事件对话不写入主线阶段列表，也不修改小蠊结局分支。
            OpenOriginal(script, 0, map, speaker);
        }

        private static int EntryIndex(MissionComponent mission, MechanoidScriptDef script)
        {
            // 闪毁原入口使用 TryGetValue(key, true)，缺少记录时默认走 true 分支。
            if (script.branch != null && (mission.BranchDict == null
                || !mission.BranchDict.TryGetValue(script.branch, out bool branch) || branch))
                return script.to;
            return 0;
        }

        private static bool ValidNode(MechanoidScriptDef script, int index)
        {
            if (script.scriptTree == null || index < 0 || index >= script.scriptTree.Count
                || script.scriptTree[index]?.buttons == null || script.scriptTree[index].buttons.Count == 0)
                return Reject("剧情节点结构无效，未推进：" + script.defName);
            return true;
        }

        private static void OpenOriginal(MechanoidScriptDef script, int index, Map map, Pawn speaker)
        {
            ScriptTree node = script.scriptTree[index];
            Find.WindowStack.Add(new CommunicationWindow_BlackMech(node.title, node.dialogue,
                node.graphic, node.drawSize, node.drawOffset, map, speaker,
                node.buttons, script.scriptTree, index));
        }

        private static bool Reject(string message)
        {
            Messages.Message("[DEV] " + message, MessageTypeDefOf.RejectInput, false);
            return false;
        }
    }
}
