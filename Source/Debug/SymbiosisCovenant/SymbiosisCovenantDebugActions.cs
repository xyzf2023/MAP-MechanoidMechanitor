using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 共生盟约测试工具的原生 LudeonTK Debug Actions 控制台入口。
    /// 只负责菜单与执行反馈，不实现任何业务逻辑；
    /// 所有叶子 Action 均委托给 SymbiosisCovenantDebugUtility。
    /// 所有固定菜单 label 保持稳定，便于人工操作与未来 Harness 按路径识别。
    /// </summary>
    public static class SymbiosisCovenantDebugActions
    {
        private delegate bool TryDebugAction(out string message);

        [DebugAction(
            "MAP-机械族机械师",
            "共生盟约测试",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static List<DebugActionNode> BuildRootMenu()
        {
            List<DebugActionNode> nodes = new List<DebugActionNode>
            {
                CreateSubmenuNode("窗口与基础状态", BuildWindowMenu),
                CreateSubmenuNode("团结度与等级", BuildUnityMenu),
                CreateSubmenuNode("派系 / 信任 / 成员", BuildFactionMenu),
                CreateSubmenuNode("联合贸易代表团", BuildTradeDelegationMenu),
                CreateSubmenuNode("共同防卫", BuildMilitaryAidMenu),
                CreateSubmenuNode("标准测试袭击", BuildTestRaidMenu),
                CreateSubmenuNode("联合军事行动", BuildJointOperationMenu)
            };

            // 主脑 BOSS 战：仅当启用奥德赛 DLC 时才加入菜单节点。
            // 未启用时完全不显示，不出现灰色/报错按钮或空子菜单。
            if (ModsConfig.OdysseyActive)
            {
                nodes.Add(CreateSubmenuNode("主脑 BOSS 战", BuildCerebrexBossTestMenu));
            }

            nodes.Add(CreateSubmenuNode("状态与日志", BuildStatusMenu));

            return nodes;
        }

        // ===== 主脑 BOSS 战（仅奥德赛 DLC） =====

        private static List<DebugActionNode> BuildCerebrexBossTestMenu()
        {
            return new List<DebugActionNode>
            {
                CreateSubmenuNode("一键准备并进入", BuildCerebrexQuickEnterMenu),
                CreateSubmenuNode("仅生成世界目标", BuildCerebrexWorldTargetMenu),
                CreateActionNode(
                    "定位现有主脑测试目标",
                    SymbiosisCovenantCerebrexDebugUtility.TryFocusExistingTestTarget),
                CreateActionNode(
                    "输出主脑测试状态",
                    SymbiosisCovenantCerebrexDebugUtility.TryLogTestStatus)
            };
        }

        private static List<DebugActionNode> BuildCerebrexQuickEnterMenu()
        {
            return new List<DebugActionNode>
            {
                CreateActionNode(
                    "低威胁：3000 点",
                    (out string message) =>
                        SymbiosisCovenantCerebrexDebugUtility
                            .TryPrepareAndEnterTest(3000f, out message)),
                CreateActionNode(
                    "中威胁：6000 点",
                    (out string message) =>
                        SymbiosisCovenantCerebrexDebugUtility
                            .TryPrepareAndEnterTest(6000f, out message)),
                CreateActionNode(
                    "高威胁：10000 点",
                    (out string message) =>
                        SymbiosisCovenantCerebrexDebugUtility
                            .TryPrepareAndEnterTest(10000f, out message))
            };
        }

        private static List<DebugActionNode> BuildCerebrexWorldTargetMenu()
        {
            return new List<DebugActionNode>
            {
                CreateActionNode(
                    "低威胁：3000 点",
                    (out string message) =>
                        SymbiosisCovenantCerebrexDebugUtility
                            .TryPrepareAndGenerateTarget(3000f, out message)),
                CreateActionNode(
                    "中威胁：6000 点",
                    (out string message) =>
                        SymbiosisCovenantCerebrexDebugUtility
                            .TryPrepareAndGenerateTarget(6000f, out message)),
                CreateActionNode(
                    "高威胁：10000 点",
                    (out string message) =>
                        SymbiosisCovenantCerebrexDebugUtility
                            .TryPrepareAndGenerateTarget(10000f, out message))
            };
        }

        // ===== Node Helper =====

        private static DebugActionNode CreateSubmenuNode(
            string label,
            Func<List<DebugActionNode>> childGetter)
        {
            return new DebugActionNode(label)
            {
                actionType = DebugActionType.Action,
                childGetter = childGetter
            };
        }

        private static DebugActionNode CreateActionNode(
            string label,
            TryDebugAction action)
        {
            return new DebugActionNode(
                label,
                DebugActionType.Action,
                () => ExecuteAction(label, action));
        }

        private static void ExecuteAction(
            string label,
            TryDebugAction action)
        {
            bool succeeded;
            string message;

            try
            {
                succeeded = action(out message);
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-SymbiosisDebug] "
                    + label
                    + " 发生未处理异常。\n"
                    + ex);

                succeeded = false;
                message = "操作发生异常，请查看日志。";
            }

            if (message.NullOrEmpty())
            {
                message = succeeded
                    ? label + "：完成。"
                    : label + "：失败。";
            }

            Messages.Message(
                message,
                succeeded
                    ? MessageTypeDefOf.TaskCompletion
                    : MessageTypeDefOf.RejectInput,
                historical: false);

            if (succeeded)
            {
                Log.Message(
                    "[MAP-SymbiosisDebug] OK "
                    + label
                    + " | "
                    + message);
            }
            else
            {
                Log.Warning(
                    "[MAP-SymbiosisDebug] FAIL "
                    + label
                    + " | "
                    + message);
            }
        }

        // ===== 窗口与基础状态 =====

        private static List<DebugActionNode> BuildWindowMenu()
        {
            return new List<DebugActionNode>
            {
                CreateActionNode(
                    "打开共生盟约窗口",
                    SymbiosisCovenantDebugUtility.TryOpenCovenantWindow),
                CreateActionNode(
                    "打开共生盟约 DEV 面板",
                    SymbiosisCovenantDebugUtility.TryOpenCovenantDevWindow),
                CreateActionNode(
                    "广播公开脱离声明",
                    SymbiosisCovenantDebugUtility.TryBroadcastDeclaration)
            };
        }

        // ===== 团结度与等级 =====

        private static List<DebugActionNode> BuildUnityMenu()
        {
            List<DebugActionNode> nodes = new List<DebugActionNode>();

            List<float> unityValues = new List<float> { 0f, 100f, 250f, 450f, 700f, 1000f };
            List<DebugActionNode> setUnityNodes = new List<DebugActionNode>();
            foreach (float raw in unityValues)
            {
                float captured = raw;
                setUnityNodes.Add(CreateActionNode(
                    "设置团结度：" + captured.ToString("F0"),
                    (out string message) =>
                        SymbiosisCovenantDebugUtility.TrySetUnity(captured, out message)));
            }

            nodes.Add(CreateSubmenuNode("设置团结度", () => setUnityNodes));

            nodes.Add(CreateActionNode(
                "团结度 -100",
                (out string message) =>
                    SymbiosisCovenantDebugUtility.TryChangeUnity(-100f, out message)));
            nodes.Add(CreateActionNode(
                "团结度 +100",
                (out string message) =>
                    SymbiosisCovenantDebugUtility.TryChangeUnity(100f, out message)));

            nodes.Add(CreateActionNode(
                "每日团结度更新",
                SymbiosisCovenantDebugUtility.TryUpdateUnityDaily));
            nodes.Add(CreateActionNode(
                "重新计算等级",
                SymbiosisCovenantDebugUtility.TryRecalculateLevel));

            return nodes;
        }

        // ===== 派系 / 信任 / 成员 =====

        private static List<DebugActionNode> BuildFactionMenu()
        {
            List<Faction> factions =
                SymbiosisCovenantDebugUtility.GetRecordedFactions();

            if (factions.Count == 0)
            {
                return new List<DebugActionNode>
                {
                    new DebugActionNode(
                        "当前没有可测试的派系记录",
                        DebugActionType.Action,
                        () => Messages.Message(
                            "当前没有共生盟约派系记录；"
                            + "请先通过“切换剧本状态 → 共生盟约”启用，并等待同步。",
                            MessageTypeDefOf.RejectInput,
                            historical: false))
                };
            }

            List<DebugActionNode> nodes = new List<DebugActionNode>(factions.Count);
            foreach (Faction faction in factions)
            {
                Faction capturedFaction = faction;
                nodes.Add(CreateSubmenuNode(
                    SymbiosisCovenantDebugUtility.FactionMenuLabel(capturedFaction),
                    () => BuildFactionSubmenu(capturedFaction)));
            }

            return nodes;
        }

        private static List<DebugActionNode> BuildFactionSubmenu(Faction faction)
        {
            List<DebugActionNode> nodes = new List<DebugActionNode>();

            // 设置信任度
            int[] trustSetValues = { -100, -50, -25, 0, 25, 50, 100, 150, 200 };
            List<DebugActionNode> setTrustNodes = new List<DebugActionNode>();
            foreach (int value in trustSetValues)
            {
                int captured = value;
                setTrustNodes.Add(CreateActionNode(
                    captured.ToString(),
                    (out string message) =>
                        SymbiosisCovenantDebugUtility.TrySetTrust(
                            faction,
                            captured,
                            out message)));
            }

            nodes.Add(CreateSubmenuNode("设置信任度", () => setTrustNodes));

            // 调整信任度
            int[] trustAdjustValues = { -25, -1, 1, 25 };
            List<DebugActionNode> adjustTrustNodes = new List<DebugActionNode>();
            foreach (int value in trustAdjustValues)
            {
                int captured = value;
                adjustTrustNodes.Add(CreateActionNode(
                    (captured >= 0 ? "+" : string.Empty) + captured.ToString(),
                    (out string message) =>
                        SymbiosisCovenantDebugUtility.TryAdjustTrust(
                            faction,
                            captured,
                            out message)));
            }

            nodes.Add(CreateSubmenuNode("调整信任度", () => adjustTrustNodes));

            // 成员状态
            nodes.Add(CreateSubmenuNode("成员状态", () => new List<DebugActionNode>
            {
                CreateActionNode(
                    "强制加入盟约",
                    (out string message) =>
                        SymbiosisCovenantDebugUtility.TryForceJoin(
                            faction, out message)),
                CreateActionNode(
                    "强制退出盟约",
                    (out string message) =>
                        SymbiosisCovenantDebugUtility.TryForceLeave(
                            faction, out message))
            }));

            // 提案
            nodes.Add(CreateSubmenuNode("提案", () => new List<DebugActionNode>
            {
                CreateActionNode(
                    "开始提案",
                    (out string message) =>
                        SymbiosisCovenantDebugUtility.TryBeginProposal(
                            faction, out message)),
                CreateActionNode(
                    "随机结算提案",
                    (out string message) =>
                        SymbiosisCovenantDebugUtility.TryResolveProposalRandom(
                            faction, out message)),
                CreateActionNode(
                    "强制成功",
                    (out string message) =>
                        SymbiosisCovenantDebugUtility.TryForceProposalSuccess(
                            faction, out message)),
                CreateActionNode(
                    "强制失败",
                    (out string message) =>
                        SymbiosisCovenantDebugUtility.TryForceProposalFailure(
                            faction, out message)),
                CreateActionNode(
                    "清除邀请冷却",
                    (out string message) =>
                        SymbiosisCovenantDebugUtility.TryClearInvitationCooldown(
                            faction, out message))
            }));

            // 计数器 / 维护
            nodes.Add(CreateSubmenuNode("计数器 / 维护", () =>
            {
                int[] invFailValues = { 0, 1, 2, 3 };
                List<DebugActionNode> invFailNodes = new List<DebugActionNode>();
                foreach (int value in invFailValues)
                {
                    int captured = value;
                    invFailNodes.Add(CreateActionNode(
                        "invFail=" + captured,
                        (out string message) =>
                            SymbiosisCovenantDebugUtility
                                .TrySetInvitationFailureCount(
                                    faction, captured, out message)));
                }

                int[] exitValues = { 0, 1, 2 };
                List<DebugActionNode> exitNodes = new List<DebugActionNode>();
                foreach (int value in exitValues)
                {
                    int captured = value;
                    exitNodes.Add(CreateActionNode(
                        "exitCount=" + captured,
                        (out string message) =>
                            SymbiosisCovenantDebugUtility
                                .TrySetCovenantExitCount(
                                    faction, captured, out message)));
                }

                return new List<DebugActionNode>
                {
                    CreateSubmenuNode("邀请失败次数", () => invFailNodes),
                    CreateSubmenuNode("退出盟约次数", () => exitNodes),
                    CreateActionNode(
                        "重置信任来源限额",
                        (out string message) =>
                            SymbiosisCovenantDebugUtility
                                .TryResetTrustSourceLimits(faction, out message)),
                    CreateActionNode(
                        "重建该派系记录",
                        (out string message) =>
                            SymbiosisCovenantDebugUtility.TryRecreateRecord(
                                faction, out message))
                };
            }));

            return nodes;
        }

        // ===== 联合贸易代表团 =====

        private static List<DebugActionNode> BuildTradeDelegationMenu()
        {
            return new List<DebugActionNode>
            {
                CreateActionNode(
                    "立即生成",
                    SymbiosisCovenantDebugUtility.TrySpawnTradeDelegationNow),
                CreateActionNode(
                    "重新安排",
                    SymbiosisCovenantDebugUtility.TryRescheduleTradeDelegation),
                CreateActionNode(
                    "立即到期",
                    SymbiosisCovenantDebugUtility.TryMakeTradeDelegationDueNow),
                CreateActionNode(
                    "输出当前调度状态到日志",
                    SymbiosisCovenantDebugUtility.TryLogTradeDelegationStatus)
            };
        }

        // ===== 共同防卫 =====

        private static List<DebugActionNode> BuildMilitaryAidMenu()
        {
            return new List<DebugActionNode>
            {
                CreateActionNode(
                    "强制当前威胁援助询问",
                    SymbiosisCovenantDebugUtility.TryForceCurrentThreatAidOffer),
                CreateActionNode(
                    "强制处理当前 Pending Raid（保留真实 Raid Points）",
                    SymbiosisCovenantDebugUtility.TryForcePendingRaidAidOffer),
                CreateActionNode(
                    "清除当前地图共同防卫状态",
                    SymbiosisCovenantDebugUtility.TryClearMilitaryAidState),
                CreateActionNode(
                    "仅清除共同防卫冷却",
                    SymbiosisCovenantDebugUtility.TryClearMilitaryAidCooldown),
                CreateActionNode(
                    "输出共同防卫状态到日志",
                    SymbiosisCovenantDebugUtility.TryLogMilitaryAidStatus)
            };
        }

        // ===== 联合军事行动 =====

        private static List<DebugActionNode> BuildJointOperationMenu()
        {
            return new List<DebugActionNode>
            {
                CreateActionNode(
                    "一键准备并生成测试邀请",
                    SymbiosisCovenantDebugUtility.TryPrepareAndSpawnJointOperationTest),
                CreateActionNode(
                    "立即生成邀请",
                    (out string msg) =>
                    {
                        bool ok = SymbiosisCovenantJointOperationScheduler.DevSpawnNow();
                        msg = ok ? "已生成邀请" : "生成失败（条件不足）";
                        return ok;
                    }),
                CreateActionNode(
                    "立即到期",
                    SymbiosisCovenantDebugUtility.TryMakeJointOperationDueNow),
                CreateActionNode(
                    "清除联合军事行动状态",
                    SymbiosisCovenantDebugUtility.TryClearJointOperation),
                CreateActionNode(
                    "输出联合军事行动状态",
                    SymbiosisCovenantDebugUtility.TryLogJointOperationStatus)
            };
        }

        // ===== 标准测试袭击 =====

        private static List<DebugActionNode> BuildTestRaidMenu()
        {
            List<DebugActionNode> randomNodes = new List<DebugActionNode>();
            foreach (float raw in SymbiosisCovenantDebugUtility.RaidPointOptions)
            {
                float captured = raw;
                randomNodes.Add(CreateActionNode(
                    captured.ToString("F0") + " 点",
                    (out string message) =>
                        SymbiosisCovenantDebugUtility.TrySpawnTestRaid(
                            captured, null, out message)));
            }

            List<Faction> factions =
                SymbiosisCovenantDebugUtility.GetHostileRaidTestFactions();
            List<DebugActionNode> specifiedNodes =
                new List<DebugActionNode>(factions.Count);
            foreach (Faction faction in factions)
            {
                Faction capturedFaction = faction;
                specifiedNodes.Add(CreateSubmenuNode(
                    SymbiosisCovenantDebugUtility.FactionMenuLabel(capturedFaction),
                    () => BuildSpecifiedFactionRaidSubmenu(capturedFaction)));
            }

            if (specifiedNodes.Count == 0)
            {
                specifiedNodes.Add(new DebugActionNode(
                    "当前没有可测试的敌对派系",
                    DebugActionType.Action,
                    () => Messages.Message(
                        "当前没有与玩家敌对且可作为袭击派系的记录。",
                        MessageTypeDefOf.RejectInput,
                        historical: false)));
            }

            return new List<DebugActionNode>
            {
                CreateSubmenuNode("随机敌对派系", () => randomNodes),
                CreateSubmenuNode("指定敌对派系", () => specifiedNodes)
            };
        }

        private static List<DebugActionNode> BuildSpecifiedFactionRaidSubmenu(
            Faction faction)
        {
            List<DebugActionNode> nodes = new List<DebugActionNode>();
            foreach (float raw in SymbiosisCovenantDebugUtility.RaidPointOptions)
            {
                float captured = raw;
                nodes.Add(CreateActionNode(
                    captured.ToString("F0") + " 点",
                    (out string message) =>
                        SymbiosisCovenantDebugUtility.TrySpawnTestRaid(
                            captured, faction, out message)));
            }

            return nodes;
        }

        // ===== 状态与日志 =====

        private static List<DebugActionNode> BuildStatusMenu()
        {
            return new List<DebugActionNode>
            {
                CreateActionNode(
                    "输出完整共生盟约状态",
                    SymbiosisCovenantDebugUtility.TryLogOverallStatus),
                CreateActionNode(
                    "输出全部派系记录",
                    SymbiosisCovenantDebugUtility.TryLogFactionRecords),
                CreateActionNode(
                    "输出联合贸易代表团状态",
                    SymbiosisCovenantDebugUtility.TryLogTradeDelegationStatus),
                CreateActionNode(
                    "输出共同防卫状态",
                    SymbiosisCovenantDebugUtility.TryLogMilitaryAidStatus)
            };
        }
    }
}
