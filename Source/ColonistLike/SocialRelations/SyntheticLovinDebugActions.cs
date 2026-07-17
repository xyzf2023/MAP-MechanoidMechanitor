using System.Collections.Generic;
using System.Text;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 开发者模式一次性 Lovin 条件诊断。仅在玩家主动点击 DebugAction 时输出日志。
    /// </summary>
    public static class SyntheticLovinDebugActions
    {
        private static readonly List<Pawn> TmpSpouses = new List<Pawn>();
        private static readonly List<Pawn> TmpSyntheticCompanions = new List<Pawn>();
        private static readonly List<string> TmpFailures = new List<string>();

        [DebugAction(
            "MAP-机械族机械师",
            "诊断机械体与配偶爱爱条件",
            false,
            false,
            false,
            false,
            false,
            0,
            false,
            actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DiagnoseSyntheticSpouseLovinConditions(Pawn clicked)
        {
            if (clicked == null)
            {
                Messages.Message(
                    "爱爱条件诊断失败：未选中有效 Pawn。",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            StringBuilder sb = new StringBuilder();
            List<string> reportBlocks = new List<string>();
            HashSet<string> seenReportBlocks = new HashSet<string>();

            void Block(string reason)
            {
                if (!seenReportBlocks.Add(reason))
                {
                    return;
                }

                reportBlocks.Add(reason);
                sb.AppendLine("[阻断] " + reason);
            }

            void Status(string reason)
            {
                sb.AppendLine("[状态] " + reason);
            }

            void AbsorbFailures(List<string> failures)
            {
                for (int i = 0; i < failures.Count; i++)
                {
                    Block(failures[i]);
                }
            }

            sb.AppendLine("=== MAP-机械族机械师：机械体与配偶爱爱条件诊断 ===");
            sb.AppendLine("点击的 Pawn：" + SyntheticLovinUtility.DescribePawn(clicked));
            sb.AppendLine("说明：只读诊断，不会分配 Job、修改冷却、开关、关系、床主或预约。");

            int ticksGame = Find.TickManager.TicksGame;
            sb.AppendLine("当前游戏 Tick：" + ticksGame);

            Pawn? initiator = null;
            Pawn? primaryCompanion = null;
            bool clickedIsSyntheticCompanion = MechanoidMechanitorCapabilityUtility.HasCapability(
                clicked, MechanoidMechanitorCapability.SyntheticSpouseInteraction);

            if (clickedIsSyntheticCompanion)
            {
                Status("点击的是拥有 SyntheticSpouseInteraction 能力的仿生伴侣。");
                SyntheticLovinUtility.CollectDirectSpousePawns(clicked, TmpSpouses);
                if (TmpSpouses.Count == 0)
                {
                    Block("仿生伴侣当前没有直接配偶关系。");
                    Finish(sb, reportBlocks);
                    return;
                }

                sb.AppendLine("仿生伴侣对端直接 Spouse 列表：");
                for (int i = 0; i < TmpSpouses.Count; i++)
                {
                    sb.AppendLine("  - " + SyntheticLovinUtility.DescribePawn(TmpSpouses[i]));
                }

                for (int i = 0; i < TmpSpouses.Count; i++)
                {
                    Pawn candidate = TmpSpouses[i];
                    if (SyntheticLovinUtility.IsValidHumanSpouseInitiator(candidate))
                    {
                        if (initiator == null
                            || candidate.thingIDNumber < initiator.thingIDNumber)
                        {
                            initiator = candidate;
                        }
                    }
                }

                if (initiator == null)
                {
                    initiator = SyntheticLovinUtility.SelectPreferredSyntheticCompanionByThingId(TmpSpouses);
                    Block("未能解析到合法的人类发起者（Spouse 中无有效 Humanlike 发起者）。");
                }

                primaryCompanion = clicked;
            }
            else
            {
                Status("点击的是普通 Pawn，视为 Lovin 发起者。");
                initiator = clicked;
                SyntheticLovinUtility.CollectDirectSpousePawns(clicked, TmpSpouses);
                TmpSyntheticCompanions.Clear();
                for (int i = 0; i < TmpSpouses.Count; i++)
                {
                    Pawn spouse = TmpSpouses[i];
                    if (MechanoidMechanitorCapabilityUtility.HasCapability(
                        spouse, MechanoidMechanitorCapability.SyntheticSpouseInteraction))
                    {
                        TmpSyntheticCompanions.Add(spouse);
                    }
                }

                if (TmpSyntheticCompanions.Count == 0)
                {
                    Block("发起者的直接 Spouse 中找不到拥有 SyntheticSpouseInteraction 的授权机械体。");
                    DiagnosePairDetails(
                        sb,
                        Block,
                        Status,
                        AbsorbFailures,
                        initiator,
                        null,
                        ticksGame);
                    AbsorbFailures(CollectJobGiverDoLovinBlocks(initiator));
                    Finish(sb, reportBlocks);
                    return;
                }

                sb.AppendLine("授权机械体配偶候选（按 DirectRelations 收集）：");
                for (int i = 0; i < TmpSyntheticCompanions.Count; i++)
                {
                    sb.AppendLine(
                        "  - " + SyntheticLovinUtility.DescribePawn(TmpSyntheticCompanions[i]));
                }

                Pawn? formalPick =
                    SyntheticLovinUtility.TryFindEnabledSyntheticCompanionForRemoteLovin(initiator);
                if (formalPick != null)
                {
                    primaryCompanion = formalPick;
                    Status(
                        "正式逻辑 TryFindEnabledSyntheticCompanionForRemoteLovin 最终选择："
                        + SyntheticLovinUtility.DescribePawn(formalPick));
                }
                else
                {
                    primaryCompanion =
                        SyntheticLovinUtility.SelectPreferredSyntheticCompanionByThingId(
                            TmpSyntheticCompanions);
                    Status(
                        "正式逻辑当前返回 null；诊断仍对 thingIDNumber 最小的授权机械体逐项展开："
                        + SyntheticLovinUtility.DescribePawn(primaryCompanion));
                }
            }

            sb.AppendLine("发起者：" + SyntheticLovinUtility.DescribePawn(initiator));
            sb.AppendLine("仿生伴侣：" + SyntheticLovinUtility.DescribePawn(primaryCompanion));

            if (initiator == null)
            {
                Block("发起者解析失败。");
                Finish(sb, reportBlocks);
                return;
            }

            DiagnosePairDetails(
                sb,
                Block,
                Status,
                AbsorbFailures,
                initiator,
                primaryCompanion,
                ticksGame);

            AbsorbFailures(CollectJobGiverDoLovinBlocks(initiator));
            Finish(sb, reportBlocks);
        }

        private static List<string> CollectJobGiverDoLovinBlocks(Pawn initiator)
        {
            List<string> blocks = new List<string>();
            int ticksGame = Find.TickManager.TicksGame;

            if (initiator.Destroyed || initiator.Dead || !initiator.Spawned)
            {
                blocks.Add("发起者未处于可用的地图生成状态。");
            }

            if (initiator.mindState == null)
            {
                blocks.Add("发起者缺少 mindState，无法判定 canLovinTick。");
                return blocks;
            }

            if (ticksGame < initiator.mindState.canLovinTick)
            {
                int remaining = initiator.mindState.canLovinTick - ticksGame;
                blocks.Add(
                    "发起者 Lovin 冷却未结束：剩余 "
                    + remaining
                    + " Tick（"
                    + remaining.ToStringTicksToPeriod()
                    + "）。");
            }

            Building_Bed? bed = initiator.CurrentBed();
            if (bed == null)
            {
                blocks.Add("发起者 CurrentBed() 为空。");
            }
            else if (bed.Medical)
            {
                blocks.Add("床铺是医疗床。");
            }

            if (initiator.health == null || !initiator.health.capacities.CanBeAwake)
            {
                blocks.Add("发起者不具备 CanBeAwake。");
            }

            Pawn? partner = LovePartnerRelationUtility.GetPartnerInMyBed(initiator);
            if (partner == null)
            {
                blocks.Add("GetPartnerInMyBed 返回 null（无床上伴侣且模组未注入远程仿生伴侣）。");
                return blocks;
            }

            if (partner.health == null || !partner.health.capacities.CanBeAwake)
            {
                blocks.Add("伴侣不具备 CanBeAwake。");
            }

            if (partner.mindState == null)
            {
                blocks.Add("伴侣缺少 mindState，无法判定 canLovinTick。");
            }
            else if (ticksGame < partner.mindState.canLovinTick)
            {
                int remaining = partner.mindState.canLovinTick - ticksGame;
                blocks.Add(
                    "伴侣 Lovin 冷却未结束：剩余 "
                    + remaining
                    + " Tick（"
                    + remaining.ToStringTicksToPeriod()
                    + "）。");
            }

            if (!initiator.CanReserve(partner) || !partner.CanReserve(initiator))
            {
                blocks.Add("双方无法互相预约。");
            }

            return blocks;
        }

        private static void DiagnosePairDetails(
            StringBuilder sb,
            System.Action<string> Block,
            System.Action<string> Status,
            System.Action<List<string>> AbsorbFailures,
            Pawn initiator,
            Pawn? primaryCompanion,
            int ticksGame)
        {
            sb.AppendLine("--- 一、原版 JobGiver_DoLovin 条件 ---");

            TmpFailures.Clear();
            SyntheticLovinUtility.EvaluateHumanSpouseInitiator(initiator, TmpFailures);
            AbsorbFailures(TmpFailures);
            if (TmpFailures.Count == 0)
            {
                Status("发起者条件通过。");
            }

            bool initiatorCooldownBlocking = SyntheticLovinUtility.FormatCanLovinCooldown(
                initiator,
                ticksGame,
                out int initiatorCanLovinTick,
                out int initiatorRemaining,
                out string initiatorReadable);
            sb.AppendLine(
                "发起者 mindState.canLovinTick："
                + initiatorCanLovinTick
                + "（当前 Tick="
                + ticksGame
                + "）");
            if (initiatorCooldownBlocking)
            {
                Block(
                    "发起者 Lovin 冷却未结束：剩余 "
                    + initiatorRemaining
                    + " Tick（"
                    + initiatorReadable
                    + "）。");
            }
            else
            {
                Status("发起者 Lovin 冷却已就绪。");
            }

            Building_Bed? bed = initiator.CurrentBed();
            if (bed == null)
            {
                Block("发起者 CurrentBed() 为空，无法取得有效床铺。");
            }

            if (primaryCompanion == null)
            {
                Block("没有可用于逐项诊断的授权机械体。");
                return;
            }

            sb.AppendLine("--- 二、仿生伴侣冷却与当前工作 ---");

            bool companionCooldownBlocking = SyntheticLovinUtility.FormatCanLovinCooldown(
                primaryCompanion,
                ticksGame,
                out int companionCanLovinTick,
                out int companionRemaining,
                out string companionReadable);
            if (companionCooldownBlocking)
            {
                Block(
                    "仿生伴侣 Lovin 冷却未结束：剩余 "
                    + companionRemaining
                    + " Tick（"
                    + companionReadable
                    + "）。");
            }
            else
            {
                Status("仿生伴侣 Lovin 冷却已就绪。");
            }

            Status(
                "「与配偶爱爱」开关："
                + (SyntheticCompanionStateUtility.IsLovinWithSpouseEnabled(primaryCompanion) ? "开启" : "关闭"));

            sb.AppendLine("--- 三、模组候选仿生伴侣条件 ---");
            TmpFailures.Clear();
            SyntheticLovinUtility.EvaluateEnabledSyntheticCompanionForRemoteLovin(
                initiator,
                primaryCompanion,
                bed,
                TmpFailures);
            if (TmpFailures.Count == 0)
            {
                Status("模组候选仿生伴侣全部条件通过。");
            }
            else
            {
                AbsorbFailures(TmpFailures);
            }

            sb.AppendLine("--- 四、床铺条件 ---");
            if (bed == null)
            {
                Block("无可诊断床铺。");
            }
            else
            {
                SyntheticLovinUtility.AppendBedOccupancySummary(sb, bed);
                TmpFailures.Clear();
                SyntheticLovinUtility.EvaluateBedStructurallyEligible(bed, TmpFailures);
                AbsorbFailures(TmpFailures);
            }
        }

        private static void Finish(StringBuilder sb, List<string> reportBlocks)
        {
            sb.AppendLine("--- 最终结论 ---");
            int n = reportBlocks.Count;
            if (n == 0)
            {
                sb.AppendLine("诊断结果：当前满足条件。");
            }
            else
            {
                sb.AppendLine(
                    "诊断结果：当前存在 " + n + " 项阻断条件。");
            }

            Log.Message(sb.ToString());

            if (n == 0)
            {
                Messages.Message(
                    "爱爱条件诊断完成：当前可以执行，详细信息已写入日志。",
                    MessageTypeDefOf.TaskCompletion,
                    historical: false);
            }
            else
            {
                Messages.Message(
                    "爱爱条件诊断完成：发现 " + n + " 项阻断条件，详细信息已写入日志。",
                    MessageTypeDefOf.NeutralEvent,
                    historical: false);
            }
        }
    }
}
