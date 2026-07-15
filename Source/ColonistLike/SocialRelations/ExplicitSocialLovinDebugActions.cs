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
    public static class ExplicitSocialLovinDebugActions
    {
        private static readonly List<Pawn> TmpSpouses = new List<Pawn>();
        private static readonly List<Pawn> TmpOptedInLovers = new List<Pawn>();
        private static readonly List<string> TmpFailures = new List<string>();

        [DebugAction(
            "MAP-机械族机械师",
            "诊断恋人与配偶爱爱条件",
            false,
            false,
            false,
            false,
            false,
            0,
            false,
            actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DiagnoseLoverSpouseLovinConditions(Pawn clicked)
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

            sb.AppendLine("=== MAP-机械族机械师：恋人与配偶爱爱条件诊断 ===");
            sb.AppendLine("点击的 Pawn：" + ExplicitSocialLovinUtility.DescribePawn(clicked));
            sb.AppendLine("说明：只读诊断，不会分配 Job、修改冷却、开关、关系、床主或预约。");

            int ticksGame = Find.TickManager.TicksGame;
            sb.AppendLine("当前游戏 Tick：" + ticksGame);

            Pawn? initiator = null;
            Pawn? primaryLover = null;
            bool clickedIsLover = ExplicitSocialRelationUtility.IsOptedIn(clicked);

            if (clickedIsLover)
            {
                Status("点击的是挂载 CompExplicitSocialRelationUser 的恋人。");
                ExplicitSocialLovinUtility.CollectDirectSpousePawns(clicked, TmpSpouses);
                if (TmpSpouses.Count == 0)
                {
                    Block("恋人当前没有直接配偶关系。");
                    Finish(sb, reportBlocks);
                    return;
                }

                sb.AppendLine("恋人对端直接 Spouse 列表：");
                for (int i = 0; i < TmpSpouses.Count; i++)
                {
                    sb.AppendLine("  - " + ExplicitSocialLovinUtility.DescribePawn(TmpSpouses[i]));
                }

                for (int i = 0; i < TmpSpouses.Count; i++)
                {
                    Pawn candidate = TmpSpouses[i];
                    if (ExplicitSocialLovinUtility.IsValidHumanSpouseInitiator(candidate))
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
                    initiator = ExplicitSocialLovinUtility.SelectPreferredLoverByThingId(TmpSpouses);
                    Block("未能解析到合法的人类发起者（Spouse 中无有效 Humanlike 发起者）。");
                }

                primaryLover = clicked;
            }
            else
            {
                Status("点击的是普通 Pawn，视为 Lovin 发起者。");
                initiator = clicked;
                ExplicitSocialLovinUtility.CollectDirectSpousePawns(clicked, TmpSpouses);
                TmpOptedInLovers.Clear();
                for (int i = 0; i < TmpSpouses.Count; i++)
                {
                    Pawn spouse = TmpSpouses[i];
                    if (ExplicitSocialRelationUtility.IsOptedIn(spouse))
                    {
                        TmpOptedInLovers.Add(spouse);
                    }
                }

                if (TmpOptedInLovers.Count == 0)
                {
                    Block("发起者的直接 Spouse 中找不到挂载 CompExplicitSocialRelationUser 的授权恋人。");
                    DiagnosePairDetails(
                        sb,
                        Block,
                        Status,
                        AbsorbFailures,
                        initiator,
                        null,
                        ticksGame);
                    Finish(sb, CollectJobGiverDoLovinBlocks(initiator));
                    return;
                }

                sb.AppendLine("授权恋人配偶候选（按 DirectRelations 收集）：");
                for (int i = 0; i < TmpOptedInLovers.Count; i++)
                {
                    sb.AppendLine(
                        "  - " + ExplicitSocialLovinUtility.DescribePawn(TmpOptedInLovers[i]));
                }

                Pawn? formalPick =
                    ExplicitSocialLovinUtility.TryFindEnabledLoverPartnerForRemoteLovin(initiator);
                if (formalPick != null)
                {
                    primaryLover = formalPick;
                    Status(
                        "正式逻辑 TryFindEnabledLoverPartnerForRemoteLovin 最终选择："
                        + ExplicitSocialLovinUtility.DescribePawn(formalPick)
                        + "（thingIDNumber 最小且条件全过）。");
                }
                else
                {
                    primaryLover =
                        ExplicitSocialLovinUtility.SelectPreferredLoverByThingId(TmpOptedInLovers);
                    Status(
                        "正式逻辑当前返回 null；诊断仍对 thingIDNumber 最小的授权恋人逐项展开："
                        + ExplicitSocialLovinUtility.DescribePawn(primaryLover));
                }

                if (TmpOptedInLovers.Count > 1)
                {
                    Status(
                        "存在多个授权恋人配偶；正式选型顺序与 TryFindEnabledLoverPartnerForRemoteLovin 一致（thingIDNumber 升序取通过者）。");
                }
            }

            sb.AppendLine("发起者：" + ExplicitSocialLovinUtility.DescribePawn(initiator));
            sb.AppendLine("恋人：" + ExplicitSocialLovinUtility.DescribePawn(primaryLover));

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
                primaryLover,
                ticksGame);

            Finish(sb, CollectJobGiverDoLovinBlocks(initiator));
        }

        /// <summary>
        /// 按原版 JobGiver_DoLovin.TryGiveJob 的实际判断顺序收集阻断（全部列出，不做短路遗漏）。
        /// </summary>
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
                blocks.Add("GetPartnerInMyBed 返回 null（无床上伴侣且模组未注入远程恋人）。");
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
            Pawn? primaryLover,
            int ticksGame)
        {
            sb.AppendLine("--- 一、原版 JobGiver_DoLovin 条件 ---");

            TmpFailures.Clear();
            ExplicitSocialLovinUtility.EvaluateHumanSpouseInitiator(initiator, TmpFailures);
            AbsorbFailures(TmpFailures);
            if (TmpFailures.Count == 0)
            {
                Status("发起者存在、存活、已生成、Humanlike、非授权恋人，且必要 Tracker 齐全。");
            }

            bool initiatorCooldownBlocking = ExplicitSocialLovinUtility.FormatCanLovinCooldown(
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

            bool inBedPosture = initiator.GetPosture().InBed();
            if (!inBedPosture)
            {
                Block("发起者实际未处于床上（GetPosture().InBed() == false）。");
            }
            else
            {
                Status("发起者实际处于床上（GetPosture().InBed() == true）。");
            }

            Building_Bed? bed = initiator.CurrentBed();
            if (bed == null)
            {
                Block("发起者 CurrentBed() 为空，无法取得有效床铺。");
            }
            else
            {
                Status("发起者 CurrentBed() 有效：" + bed.LabelCap + " @ " + bed.Position);
                if (bed.Medical)
                {
                    Block("床铺是医疗床（JobGiver_DoLovin 直接拒绝）。");
                }
                else
                {
                    Status("床铺不是医疗床。");
                }
            }

            if (initiator.health == null || !initiator.health.capacities.CanBeAwake)
            {
                Block("发起者不具备 CanBeAwake。");
            }
            else
            {
                Status("发起者具备 CanBeAwake。");
            }

            Pawn? vanillaPartner =
                ExplicitSocialLovinUtility.TryFindVanillaLovePartnerOccupyingBed(initiator);
            Pawn? finalPartner = LovePartnerRelationUtility.GetPartnerInMyBed(initiator);

            if (vanillaPartner != null)
            {
                Status(
                    "原版 GetPartnerInMyBed 床上已有伴侣："
                    + ExplicitSocialLovinUtility.DescribePawn(vanillaPartner)
                    + "。模组 Postfix 不会替换该结果。");
            }
            else
            {
                Status("原版床上占用伴侣查询结果：null（Postfix 可以尝试注入远程恋人）。");
            }

            if (finalPartner != null)
            {
                if (ExplicitSocialRelationUtility.IsOptedIn(finalPartner))
                {
                    Status(
                        "GetPartnerInMyBed 最终结果为授权恋人："
                        + ExplicitSocialLovinUtility.DescribePawn(finalPartner)
                        + "（候选查询成功）。");
                }
                else
                {
                    Status(
                        "GetPartnerInMyBed 最终结果："
                        + ExplicitSocialLovinUtility.DescribePawn(finalPartner)
                        + "。");
                }
            }
            else
            {
                Status("GetPartnerInMyBed 最终结果：null。");
            }

            if (primaryLover == null)
            {
                Block("没有可用于逐项诊断的授权恋人。");
                return;
            }

            sb.AppendLine("--- 二、恋人冷却与当前工作 ---");

            bool loverCooldownBlocking = ExplicitSocialLovinUtility.FormatCanLovinCooldown(
                primaryLover,
                ticksGame,
                out int loverCanLovinTick,
                out int loverRemaining,
                out string loverReadable);
            sb.AppendLine(
                "恋人 mindState.canLovinTick："
                + loverCanLovinTick
                + "（当前 Tick="
                + ticksGame
                + "）");
            if (loverCooldownBlocking)
            {
                Block(
                    "恋人 Lovin 冷却未结束：剩余 "
                    + loverRemaining
                    + " Tick（"
                    + loverReadable
                    + "）。");
            }
            else
            {
                Status("恋人 Lovin 冷却已就绪。");
            }

            Job? curJob = primaryLover.CurJob;
            if (curJob == null)
            {
                Status("恋人当前无 Job。");
            }
            else
            {
                Status(
                    "恋人当前 JobDef="
                    + (curJob.def?.defName ?? "null")
                    + "，playerForced="
                    + curJob.playerForced
                    + "，forceCompleteBeforeNextJob="
                    + (curJob.def != null && curJob.def.forceCompleteBeforeNextJob)
                    + "，IsCurrentJobPlayerInterruptible="
                    + (primaryLover.jobs != null
                        && primaryLover.jobs.IsCurrentJobPlayerInterruptible()));
            }

            CompExplicitSocialRelationUser? comp =
                primaryLover.GetComp<CompExplicitSocialRelationUser>();
            if (comp == null)
            {
                Block("恋人未挂载 CompExplicitSocialRelationUser。");
            }
            else
            {
                Status(
                    "「与配偶爱爱」开关："
                    + (comp.LovinWithSpouseEnabled ? "开启" : "关闭"));
            }

            sb.AppendLine("--- 三、模组候选恋人条件 ---");
            TmpFailures.Clear();
            ExplicitSocialLovinUtility.EvaluateEnabledLoverForRemoteLovin(
                initiator,
                primaryLover,
                bed,
                TmpFailures);
            if (TmpFailures.Count == 0)
            {
                Status("模组候选恋人全部条件通过。");
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
                ExplicitSocialLovinUtility.AppendBedOccupancySummary(sb, bed);

                // 床铺细项已包含在 EvaluateEnabledLover / EvaluateCanLoverUseBed；
                // 此处仅补结构门槛中可能尚未出现的说明，并复述共享预检结果。
                TmpFailures.Clear();
                ExplicitSocialLovinUtility.EvaluateBedStructurallyEligible(bed, TmpFailures);
                AbsorbFailures(TmpFailures);

                bool isOwner = bed.IsOwner(primaryLover, out _);
                Status("恋人是否已经是床铺所有者：" + isOwner);
                if (!isOwner)
                {
                    Status(
                        "RestUtility.BedOwnerWillShare："
                        + RestUtility.BedOwnerWillShare(bed, primaryLover, null));
                }

                Status(
                    "床铺预约是否允许恋人加入（CanReserve slots="
                    + bed.SleepingSlotsCount
                    + "）："
                    + primaryLover.CanReserve(bed, bed.SleepingSlotsCount, 0));
            }

            Pawn? reservePartner = finalPartner ?? primaryLover;
            if (reservePartner != null)
            {
                bool canReservePartner = initiator.CanReserve(reservePartner);
                bool partnerCanReserveInitiator = reservePartner.CanReserve(initiator);
                if (!canReservePartner || !partnerCanReserveInitiator)
                {
                    Block(
                        "双方无法互相预约（发起者→伴侣="
                        + canReservePartner
                        + "，伴侣→发起者="
                        + partnerCanReserveInitiator
                        + "）。");
                }
                else
                {
                    Status("双方可以互相预约。");
                }
            }
        }

        private static void Finish(StringBuilder sb, List<string> jobGiverBlocks)
        {
            sb.AppendLine("--- 最终结论 ---");
            int n = jobGiverBlocks.Count;
            if (n == 0)
            {
                sb.AppendLine("诊断结果：当前满足 Try job giver → JobGiver_DoLovin 的条件。");
            }
            else
            {
                sb.AppendLine(
                    "诊断结果：当前存在 " + n + " 项阻断条件，无法获得 Lovin Job。");
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
