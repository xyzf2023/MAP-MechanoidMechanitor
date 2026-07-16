using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 开发者模式一次性「当前 Lovin 运行状态」诊断（只读）。
    /// </summary>
    public static partial class ExplicitSocialLovinDebugActions
    {
        private static readonly List<Pawn> TmpRuntimeSpouses = new List<Pawn>();

        private const int ErrorKeyAutoCaptureSelf = 879346704;
        private const int ErrorKeyAutoCaptureToilStructure = 879346705;
        private const int MaxAutoCaptureCacheEntries = 256;

        private const string StageGotoBedEnd = "恋人抵达床位／GotoBed结束";
        private const string StageLayDownInitBefore = "最终LayDown初始化前";
        private const string StageLayDownInitAfter = "最终LayDown初始化后";
        private const string StageLayDownInitAfterException = "最终LayDown初始化后／初始化异常现场";
        private const string StageJobFinished = "恋人Lovin工作结束";

        private static readonly HashSet<string> AutoCapturedStageKeys = new HashSet<string>();
        private static readonly HashSet<string> AutoFinishedJobKeys = new HashSet<string>();
        private static readonly HashSet<string> AutoNotifiedJobKeys = new HashSet<string>();

        [DebugAction(
            "MAP-机械族机械师",
            "诊断当前爱爱运行状态",
            false,
            false,
            false,
            false,
            false,
            0,
            false,
            actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DiagnoseCurrentLovinRuntimeState(Pawn clicked)
        {
            if (clicked == null)
            {
                Messages.Message(
                    "当前爱爱运行状态诊断失败：未选中有效 Pawn。",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            if (!TryBuildFullRuntimeReport(
                    clicked,
                    reportTitle: "=== MAP-机械族机械师：当前爱爱运行状态诊断 ===",
                    stageName: null,
                    jobCondition: null,
                    autoCaptureMeta: null,
                    out string report,
                    out string? failureScreenMessage))
            {
                Log.Message(report);
                Messages.Message(
                    failureScreenMessage ?? "当前爱爱运行状态诊断失败：无法解析双方。",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            Log.Message(report);
            Messages.Message(
                "当前爱爱运行状态诊断完成，详细信息已写入日志。",
                MessageTypeDefOf.TaskCompletion,
                historical: false);
        }

        /// <summary>
        /// 在恋人配套 Lovin 的 MakeNewToils 包装中安装事件触发式自动诊断（仅 DevMode）。
        /// </summary>
        internal static void TryInstallRemoteLoverAutoCapture(
            JobDriver_Lovin driver,
            Pawn lover,
            Pawn humanSpouse,
            Building_Bed bed,
            List<Toil> toils)
        {
            if (!Prefs.DevMode || driver == null || lover == null || humanSpouse == null || bed == null)
            {
                return;
            }

            try
            {
                if (!TryIdentifyRemoteLoverLovinToils(
                        toils,
                        out Toil? gotoBedToil,
                        out Toil? finalLayDownToil))
                {
                    Log.ErrorOnce(
                        "[MAP-机械族机械师] 自动 Lovin 诊断：无法识别恋人配套 Job 的 GotoBed／最终 LayDown 结构，"
                        + "已跳过自动捕获（正式 Lovin 流程不受影响）。",
                        ErrorKeyAutoCaptureToilStructure);
                    return;
                }

                int thingId = lover.thingIDNumber;
                int loadId = driver.job?.loadID ?? -1;

                if (gotoBedToil != null)
                {
                    gotoBedToil.AddPreTickIntervalAction(
                        delegate(int _)
                        {
                            if (!Prefs.DevMode)
                            {
                                return;
                            }

                            if (HasLoverArrivedAtBedSlot(lover, bed))
                            {
                                TryEmitAutoRuntimeCapture(
                                    lover,
                                    humanSpouse,
                                    bed,
                                    thingId,
                                    loadId,
                                    StageGotoBedEnd,
                                    jobCondition: null);
                            }
                        });
                    gotoBedToil.AddFinishAction(
                        delegate
                        {
                            if (!Prefs.DevMode)
                            {
                                return;
                            }

                            TryEmitAutoRuntimeCapture(
                                lover,
                                humanSpouse,
                                bed,
                                thingId,
                                loadId,
                                StageGotoBedEnd,
                                jobCondition: null);
                        });
                }

                if (finalLayDownToil != null)
                {
                    Action? originalInit = finalLayDownToil.initAction;
                    finalLayDownToil.initAction = delegate
                    {
                        // 诊断异常不得阻止原版 initAction。
                        TryEmitAutoRuntimeCapture(
                            lover,
                            humanSpouse,
                            bed,
                            thingId,
                            loadId,
                            StageLayDownInitBefore,
                            jobCondition: null);

                        bool initCompleted = false;
                        try
                        {
                            originalInit?.Invoke();
                            initCompleted = true;
                        }
                        finally
                        {
                            // 成功或异常现场都尽量记录一次；原版异常在 finally 后继续向外抛出。
                            TryEmitAutoRuntimeCapture(
                                lover,
                                humanSpouse,
                                bed,
                                thingId,
                                loadId,
                                initCompleted
                                    ? StageLayDownInitAfter
                                    : StageLayDownInitAfterException,
                                jobCondition: null);
                        }
                    };
                }

                driver.AddFinishAction(
                    delegate(JobCondition condition)
                    {
                        if (!Prefs.DevMode)
                        {
                            MarkAutoJobFinished(thingId, loadId);
                            return;
                        }

                        TryEmitAutoRuntimeCapture(
                            lover,
                            humanSpouse,
                            bed,
                            thingId,
                            loadId,
                            StageJobFinished,
                            condition);
                        MarkAutoJobFinished(thingId, loadId);
                    });
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(
                    "[MAP-机械族机械师] 自动 Lovin 诊断：安装捕获钩子时自身发生异常（正式流程不受影响）。\n"
                    + ex,
                    ErrorKeyAutoCaptureSelf);
            }
        }

        private static bool TryIdentifyRemoteLoverLovinToils(
            List<Toil> toils,
            out Toil? gotoBedToil,
            out Toil? finalLayDownToil)
        {
            gotoBedToil = null;
            finalLayDownToil = null;
            if (toils == null || toils.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < toils.Count; i++)
            {
                Toil toil = toils[i];
                string name = toil.debugName ?? string.Empty;
                if (name.IndexOf("GotoBed", StringComparison.OrdinalIgnoreCase) >= 0
                    || toil.defaultCompleteMode == ToilCompleteMode.PatherArrival)
                {
                    gotoBedToil = toil;
                    break;
                }
            }

            // 原版：瞬时初始化(Instant) 后紧跟最终 LayDown(Never)。
            for (int i = 0; i < toils.Count - 1; i++)
            {
                if (toils[i].defaultCompleteMode == ToilCompleteMode.Instant
                    && toils[i + 1].defaultCompleteMode == ToilCompleteMode.Never)
                {
                    finalLayDownToil = toils[i + 1];
                    break;
                }
            }

            if (finalLayDownToil == null)
            {
                for (int i = 0; i < toils.Count; i++)
                {
                    Toil toil = toils[i];
                    string name = toil.debugName ?? string.Empty;
                    if (toil.defaultCompleteMode == ToilCompleteMode.Never
                        && name.IndexOf("LayDown", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        finalLayDownToil = toil;
                        break;
                    }
                }
            }

            return gotoBedToil != null && finalLayDownToil != null;
        }

        private static bool HasLoverArrivedAtBedSlot(Pawn lover, Building_Bed bed)
        {
            try
            {
                if (lover == null || bed == null || !lover.Spawned || bed.Destroyed || !bed.Spawned)
                {
                    return false;
                }

                IntVec3 slot = RestUtility.GetBedSleepingSlotPosFor(lover, bed);
                if (lover.Position == slot)
                {
                    return true;
                }

                if (bed.OccupiedRect().Contains(lover.Position)
                    && (lover.pather == null || !lover.pather.Moving))
                {
                    return true;
                }
            }
            catch
            {
                // 只读检查失败时不触发捕获。
            }

            return false;
        }

        private static void TryEmitAutoRuntimeCapture(
            Pawn lover,
            Pawn humanSpouse,
            Building_Bed bed,
            int thingId,
            int loadId,
            string stageName,
            JobCondition? jobCondition)
        {
            try
            {
                if (!Prefs.DevMode)
                {
                    return;
                }

                string jobKey = thingId + ":" + loadId;
                // Job 结束后不再接受迟到的阶段钩子（结束阶段在 MarkAutoJobFinished 之前写入）。
                if (AutoFinishedJobKeys.Contains(jobKey))
                {
                    return;
                }

                string stageKey = jobKey + ":" + stageName;
                if (!TryMarkAutoStageCaptured(stageKey))
                {
                    return;
                }

                string autoHeader =
                    "=== 自动 Lovin 运行诊断：" + stageName + " ===";
                AutoCaptureMeta meta = new AutoCaptureMeta(
                    stageName,
                    lover.ThingID,
                    loadId,
                    Find.TickManager.TicksGame);

                if (!TryBuildFullRuntimeReport(
                        lover,
                        reportTitle: autoHeader,
                        stageName: stageName,
                        jobCondition: jobCondition,
                        autoCaptureMeta: meta,
                        out string report,
                        out _))
                {
                    // 即便解析失败也输出已有内容，便于定位。
                    Log.Message(report);
                    MaybeNotifyAutoCapture(jobKey);
                    return;
                }

                Log.Message(report);
                MaybeNotifyAutoCapture(jobKey);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(
                    "[MAP-机械族机械师] 自动 Lovin 诊断自身发生异常（正式 Job 不受影响）。\n" + ex,
                    ErrorKeyAutoCaptureSelf);
            }
        }

        private static bool TryMarkAutoStageCaptured(string stageKey)
        {
            TrimAutoCaptureCacheIfNeeded();
            if (AutoCapturedStageKeys.Contains(stageKey))
            {
                return false;
            }

            AutoCapturedStageKeys.Add(stageKey);
            return true;
        }

        private static void MarkAutoJobFinished(int thingId, int loadId)
        {
            string jobKey = thingId + ":" + loadId;
            AutoFinishedJobKeys.Add(jobKey);

            List<string> toRemove = new List<string>();
            foreach (string key in AutoCapturedStageKeys)
            {
                if (key.StartsWith(jobKey + ":", StringComparison.Ordinal))
                {
                    // 保留结束阶段键，防止 FinishAction 被重复调用时再次输出。
                    if (key.EndsWith(":" + StageJobFinished, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    toRemove.Add(key);
                }
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                AutoCapturedStageKeys.Remove(toRemove[i]);
            }

            TrimAutoCaptureCacheIfNeeded();
        }

        private static void MaybeNotifyAutoCapture(string jobKey)
        {
            if (!AutoNotifiedJobKeys.Add(jobKey))
            {
                return;
            }

            Messages.Message(
                "已自动记录恋人 Lovin 运行诊断，请在日志中搜索“自动 Lovin 运行诊断”。",
                MessageTypeDefOf.NeutralEvent,
                historical: false);
        }

        private static void TrimAutoCaptureCacheIfNeeded()
        {
            if (AutoCapturedStageKeys.Count <= MaxAutoCaptureCacheEntries
                && AutoFinishedJobKeys.Count <= MaxAutoCaptureCacheEntries
                && AutoNotifiedJobKeys.Count <= MaxAutoCaptureCacheEntries)
            {
                return;
            }

            AutoCapturedStageKeys.Clear();
            AutoFinishedJobKeys.Clear();
            AutoNotifiedJobKeys.Clear();
        }

        private readonly struct AutoCaptureMeta
        {
            public readonly string StageName;
            public readonly string LoverThingId;
            public readonly int JobLoadId;
            public readonly int GameTick;

            public AutoCaptureMeta(
                string stageName,
                string loverThingId,
                int jobLoadId,
                int gameTick)
            {
                StageName = stageName;
                LoverThingId = loverThingId;
                JobLoadId = jobLoadId;
                GameTick = gameTick;
            }
        }

        /// <summary>
        /// 构建完整运行状态报告；供手动诊断与自动捕获共用。
        /// </summary>
        private static bool TryBuildFullRuntimeReport(
            Pawn focus,
            string reportTitle,
            string? stageName,
            JobCondition? jobCondition,
            AutoCaptureMeta? autoCaptureMeta,
            out string report,
            out string? failureScreenMessage)
        {
            StringBuilder sb = new StringBuilder();
            failureScreenMessage = null;
            sb.AppendLine(reportTitle);
            sb.AppendLine("说明：只读诊断，不修改 Job、姿态、寻路、床主、预约、关系、开关或冷却。");

            if (autoCaptureMeta.HasValue)
            {
                AutoCaptureMeta meta = autoCaptureMeta.Value;
                sb.AppendLine("游戏 Tick：" + meta.GameTick);
                sb.AppendLine("恋人 ThingID：" + meta.LoverThingId);
                sb.AppendLine("恋人 Job.loadID：" + meta.JobLoadId);
                sb.AppendLine("当前阶段：" + meta.StageName);
                if (jobCondition.HasValue)
                {
                    sb.AppendLine("JobCondition：" + DescribeJobCondition(jobCondition.Value));
                }
            }
            else
            {
                sb.AppendLine("点击的 Pawn：" + ExplicitSocialLovinUtility.DescribePawn(focus));
                sb.AppendLine("当前游戏 Tick：" + Find.TickManager.TicksGame);
            }

            if (!TryResolveRuntimePair(
                    focus,
                    sb,
                    out Pawn? humanSpouse,
                    out Pawn? lover,
                    out Building_Bed? sharedBed,
                    out string? resolveNote))
            {
                sb.AppendLine("--- 最终结论 ---");
                sb.AppendLine("可能故障阶段：无法解析人类配偶与授权恋人双方。");
                if (!string.IsNullOrEmpty(resolveNote))
                {
                    sb.AppendLine(resolveNote);
                }

                if (!string.IsNullOrEmpty(stageName))
                {
                    sb.AppendLine("自动捕获阶段：" + stageName);
                }

                report = sb.ToString();
                failureScreenMessage = resolveNote;
                return false;
            }

            sb.AppendLine("解析到的人类配偶：" + ExplicitSocialLovinUtility.DescribePawn(humanSpouse));
            sb.AppendLine("解析到的授权恋人：" + ExplicitSocialLovinUtility.DescribePawn(lover));
            if (sharedBed != null)
            {
                sb.AppendLine(
                    "共享床铺候选：" + sharedBed.LabelCap + " @ " + sharedBed.Position
                    + "（" + sharedBed.ThingID + "）");
            }
            else
            {
                sb.AppendLine("共享床铺候选：未能从 Lovin Target B / CurrentBed 解析。");
            }

            if (!string.IsNullOrEmpty(resolveNote))
            {
                sb.AppendLine("[状态] " + resolveNote);
            }

            AppendPawnRuntimeSection(sb, "焦点 Pawn", focus, humanSpouse, lover, sharedBed);
            if (humanSpouse != null && humanSpouse != focus)
            {
                AppendPawnRuntimeSection(sb, "人类配偶", humanSpouse, humanSpouse, lover, sharedBed);
            }

            if (lover != null && lover != focus)
            {
                AppendPawnRuntimeSection(sb, "授权恋人", lover, humanSpouse, lover, sharedBed);
            }

            AppendBindingSection(sb, humanSpouse, lover, sharedBed);
            AppendPathSection(sb, lover, sharedBed);
            AppendBedSection(sb, humanSpouse, lover, sharedBed);
            bool harmonyOk = AppendHarmonyPatchSection(sb);
            string conclusion = BuildRuntimeConclusion(
                focus,
                humanSpouse,
                lover,
                sharedBed,
                harmonyOk);
            sb.AppendLine("--- 最终结论 ---");
            sb.AppendLine("可能故障阶段：" + conclusion);
            if (jobCondition.HasValue)
            {
                sb.AppendLine("本次结束 JobCondition：" + DescribeJobCondition(jobCondition.Value));
            }

            report = sb.ToString();
            return true;
        }

        private static string DescribeJobCondition(JobCondition condition)
        {
            switch (condition)
            {
                case JobCondition.Succeeded:
                    return "Succeeded（成功完成）";
                case JobCondition.Incompletable:
                    return "Incompletable（无法完成／失败条件）";
                case JobCondition.InterruptForced:
                    return "InterruptForced（强制中断）";
                case JobCondition.Errored:
                    return "Errored（错误）";
                case JobCondition.ErroredPather:
                    return "ErroredPather（寻路错误）";
                default:
                    return condition.ToString();
            }
        }

        private static bool TryResolveRuntimePair(
            Pawn clicked,
            StringBuilder sb,
            out Pawn? humanSpouse,
            out Pawn? lover,
            out Building_Bed? sharedBed,
            out string? resolveNote)
        {
            humanSpouse = null;
            lover = null;
            sharedBed = null;
            resolveNote = null;

            bool clickedIsLover = ExplicitSocialRelationUtility.IsOptedIn(clicked);
            Job? clickedJob = clicked.CurJob;

            if (clickedJob != null
                && clickedJob.def == JobDefOf.Lovin
                && ExplicitSocialLovinUtility.TryGetLovinPartnerAndBed(
                    clickedJob,
                    out Pawn? jobPartner,
                    out Building_Bed? jobBed))
            {
                sharedBed = jobBed;
                if (clickedIsLover)
                {
                    lover = clicked;
                    humanSpouse = jobPartner;
                    resolveNote = "由恋人当前 Lovin 的 TargetIndex.A 解析人类配偶。";
                }
                else
                {
                    humanSpouse = clicked;
                    lover = jobPartner;
                    resolveNote = "由人类配偶当前 Lovin 的 TargetIndex.A 解析伴侣（预期为授权恋人）。";
                    if (lover != null && !ExplicitSocialRelationUtility.IsOptedIn(lover))
                    {
                        resolveNote += " 注意：Target A 不是授权恋人，可能是原版床上伴侣。";
                    }
                }

                return humanSpouse != null && lover != null;
            }

            resolveNote = "点击 Pawn 当前不是 Lovin；改为通过直接 Spouse 关系尝试解析双方。";
            ExplicitSocialLovinUtility.CollectDirectSpousePawns(clicked, TmpRuntimeSpouses);
            if (TmpRuntimeSpouses.Count == 0)
            {
                resolveNote = "点击 Pawn 当前不是 Lovin，且没有直接 Spouse 关系，无法解析双方。";
                // 仍输出点击者状态由调用方在失败路径前... 调用方失败会直接返回。
                // 为让玩家看到点击者状态，先写一段再返回 false。
                AppendPawnRuntimeSection(sb, "点击的 Pawn", clicked, null, null, null);
                return false;
            }

            if (clickedIsLover)
            {
                lover = clicked;
                for (int i = 0; i < TmpRuntimeSpouses.Count; i++)
                {
                    Pawn spouse = TmpRuntimeSpouses[i];
                    if (spouse != null
                        && !ExplicitSocialRelationUtility.IsOptedIn(spouse)
                        && spouse.RaceProps != null
                        && spouse.RaceProps.Humanlike)
                    {
                        humanSpouse = spouse;
                        break;
                    }
                }

                if (humanSpouse == null)
                {
                    resolveNote = "恋人当前没有可识别的人类直接配偶。";
                    AppendPawnRuntimeSection(sb, "点击的 Pawn", clicked, null, lover, null);
                    return false;
                }
            }
            else
            {
                humanSpouse = clicked;
                for (int i = 0; i < TmpRuntimeSpouses.Count; i++)
                {
                    Pawn spouse = TmpRuntimeSpouses[i];
                    if (ExplicitSocialRelationUtility.IsOptedIn(spouse))
                    {
                        lover = spouse;
                        break;
                    }
                }

                if (lover == null)
                {
                    resolveNote = "人类配偶的直接 Spouse 中找不到授权恋人。";
                    AppendPawnRuntimeSection(sb, "点击的 Pawn", clicked, humanSpouse, null, null);
                    return false;
                }
            }

            sharedBed = humanSpouse.CurrentBed()
                ?? lover.CurrentBed()
                ?? (humanSpouse.CurJob != null
                    && ExplicitSocialLovinUtility.TryGetLovinPartnerAndBed(
                        humanSpouse.CurJob,
                        out _,
                        out Building_Bed? humanBed)
                    ? humanBed
                    : null)
                ?? (lover.CurJob != null
                    && ExplicitSocialLovinUtility.TryGetLovinPartnerAndBed(
                        lover.CurJob,
                        out _,
                        out Building_Bed? loverBed)
                    ? loverBed
                    : null);

            return true;
        }

        private static void AppendPawnRuntimeSection(
            StringBuilder sb,
            string title,
            Pawn pawn,
            Pawn? humanSpouse,
            Pawn? lover,
            Building_Bed? sharedBed)
        {
            sb.AppendLine("--- " + title + " ---");
            try
            {
                sb.AppendLine("LabelShort / ThingID / thingIDNumber："
                    + pawn.LabelShort + " / " + pawn.ThingID + " / " + pawn.thingIDNumber);
                sb.AppendLine("defName：" + (pawn.def?.defName ?? "null"));
                sb.AppendLine(
                    "RaceProps.Humanlike / IsMechanoid："
                    + (pawn.RaceProps?.Humanlike.ToString() ?? "null")
                    + " / "
                    + (pawn.RaceProps?.IsMechanoid.ToString() ?? "null"));
                sb.AppendLine(
                    "Spawned / Destroyed / Dead / Downed / Drafted / InMentalState："
                    + pawn.Spawned + " / " + pawn.Destroyed + " / " + pawn.Dead + " / "
                    + pawn.Downed + " / " + pawn.Drafted + " / " + pawn.InMentalState);
                sb.AppendLine("Map：" + (pawn.Map != null ? pawn.Map.ToString() : "null"));
                sb.AppendLine("Position：" + pawn.Position);

                Job? job = pawn.CurJob;
                sb.AppendLine("CurJob 存在：" + (job != null));
                sb.AppendLine("CurJobDef.defName：" + (pawn.CurJobDef?.defName ?? "null"));
                if (job != null)
                {
                    sb.AppendLine("CurJob.loadID：" + job.loadID);
                    sb.AppendLine("CurJob.playerForced：" + job.playerForced);
                    sb.AppendLine("CurJob.startTick：" + job.startTick);
                }

                JobDriver? driver = pawn.jobs?.curDriver;
                sb.AppendLine(
                    "JobDriver 实际类型："
                    + (driver != null ? driver.GetType().FullName : "null"));
                if (driver != null)
                {
                    sb.AppendLine("JobDriver.CurToilIndex：" + driver.CurToilIndex);
                    sb.AppendLine("JobDriver.CurToilString：" + driver.CurToilString);
                    sb.AppendLine("JobDriver.OnLastToil：" + driver.OnLastToil);
                    sb.AppendLine("JobDriver.ended：" + driver.ended);
                    sb.AppendLine("JobDriver.asleep：" + driver.asleep);
                    sb.AppendLine("JobDriver.ticksLeftThisToil：" + driver.ticksLeftThisToil);
                    sb.AppendLine(
                        "JobDriver.globalFailConditions 数量："
                        + (driver.globalFailConditions?.Count ?? 0)
                        + "（不执行其中委托）");
                }

                sb.AppendLine(
                    "jobs.posture 原始值："
                    + (pawn.jobs != null ? pawn.jobs.posture.ToString() : "jobs 缺失"));
                PawnPosture posture = pawn.GetPosture();
                sb.AppendLine("GetPosture()：" + posture);
                sb.AppendLine("GetPosture().InBed()：" + posture.InBed());

                Building_Bed? currentBed = null;
                try
                {
                    currentBed = pawn.CurrentBed();
                    sb.AppendLine(
                        "CurrentBed()："
                        + (currentBed != null
                            ? currentBed.LabelCap + " @ " + currentBed.Position
                            : "null"));
                }
                catch (Exception ex)
                {
                    sb.AppendLine("CurrentBed() 异常：" + ex);
                }

                try
                {
                    Building_Bed? bedWithSlot = pawn.CurrentBed(out int? sleepingSlot);
                    sb.AppendLine(
                        "CurrentBed(out sleepingSlot)："
                        + (bedWithSlot != null
                            ? bedWithSlot.LabelCap + " @ " + bedWithSlot.Position
                            : "null")
                        + "，sleepingSlot="
                        + (sleepingSlot.HasValue ? sleepingSlot.Value.ToString() : "null"));
                }
                catch (Exception ex)
                {
                    sb.AppendLine("CurrentBed(out sleepingSlot) 异常：" + ex);
                }

                sb.AppendLine(
                    "ShouldUseHumanlikeBedLovinRender："
                    + ExplicitSocialLovinUtility.ShouldUseHumanlikeBedLovinRender(pawn));
                sb.AppendLine("story 存在：" + (pawn.story != null));
                sb.AppendLine(
                    "story.bodyType："
                    + (pawn.story?.bodyType != null
                        ? pawn.story.bodyType.defName
                        : "null"));
                sb.AppendLine(
                    "mindState / health / jobs / pather 存在："
                    + (pawn.mindState != null) + " / "
                    + (pawn.health != null) + " / "
                    + (pawn.jobs != null) + " / "
                    + (pawn.pather != null));

                if (job != null && job.def == JobDefOf.Lovin)
                {
                    AppendLovinTargetLines(sb, pawn, job, humanSpouse, lover, sharedBed);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("该区段诊断异常：" + ex);
            }
        }

        private static void AppendLovinTargetLines(
            StringBuilder sb,
            Pawn pawn,
            Job job,
            Pawn? humanSpouse,
            Pawn? lover,
            Building_Bed? sharedBed)
        {
            LocalTargetInfo targetA = job.GetTarget(TargetIndex.A);
            LocalTargetInfo targetB = job.GetTarget(TargetIndex.B);
            sb.AppendLine("TargetIndex.A 有效：" + targetA.IsValid);
            sb.AppendLine(
                "TargetIndex.A Thing："
                + (targetA.Thing != null
                    ? targetA.Thing.LabelCap + " / " + targetA.Thing.ThingID
                    : "null"));
            if (targetA.Thing is Pawn targetAPawn)
            {
                sb.AppendLine("TargetIndex.A Pawn 名称：" + targetAPawn.LabelShort);
            }

            sb.AppendLine("TargetIndex.B 有效：" + targetB.IsValid);
            sb.AppendLine(
                "TargetIndex.B Thing："
                + (targetB.Thing != null
                    ? targetB.Thing.def?.defName + " @ " + targetB.Thing.Position
                        + " / " + targetB.Thing.ThingID
                    : "null"));
            sb.AppendLine("TargetIndex.B 是 Building_Bed：" + (targetB.Thing is Building_Bed));

            Pawn? expectedPartner = ExplicitSocialRelationUtility.IsOptedIn(pawn)
                ? humanSpouse
                : lover;
            sb.AppendLine(
                "Target A 与预期对方一致："
                + (expectedPartner != null && targetA.Thing == expectedPartner));
            sb.AppendLine(
                "Target B 与共享床一致："
                + (sharedBed != null && targetB.Thing == sharedBed));
        }

        private static void AppendBindingSection(
            StringBuilder sb,
            Pawn? humanSpouse,
            Pawn? lover,
            Building_Bed? sharedBed)
        {
            sb.AppendLine("--- 模组远程 Lovin 绑定判断 ---");
            if (humanSpouse == null || lover == null)
            {
                sb.AppendLine("缺少人类配偶或恋人，跳过绑定判断。");
                return;
            }

            try
            {
                Job? humanJob = humanSpouse.CurJob;
                Job? loverJob = lover.CurJob;
                bool tryGetHuman = ExplicitSocialLovinUtility.TryGetLovinPartnerAndBed(
                    humanJob,
                    out Pawn? humanPartner,
                    out Building_Bed? humanBed);
                bool tryGetLover = ExplicitSocialLovinUtility.TryGetLovinPartnerAndBed(
                    loverJob,
                    out Pawn? loverPartner,
                    out Building_Bed? loverBed);

                sb.AppendLine(
                    "TryGetLovinPartnerAndBed(人类)："
                    + tryGetHuman
                    + "，partner="
                    + ExplicitSocialLovinUtility.DescribePawn(humanPartner)
                    + "，bed="
                    + (humanBed?.LabelCap ?? "null"));
                sb.AppendLine(
                    "TryGetLovinPartnerAndBed(恋人)："
                    + tryGetLover
                    + "，partner="
                    + ExplicitSocialLovinUtility.DescribePawn(loverPartner)
                    + "，bed="
                    + (loverBed?.LabelCap ?? "null"));

                Building_Bed? bedForChecks = sharedBed ?? humanBed ?? loverBed;
                sb.AppendLine(
                    "IsRemoteHumanSpouseLovinJob："
                    + ExplicitSocialLovinUtility.IsRemoteHumanSpouseLovinJob(
                        humanSpouse,
                        lover,
                        bedForChecks));
                sb.AppendLine(
                    "IsRemoteLoverCompanionLovinJob："
                    + ExplicitSocialLovinUtility.IsRemoteLoverCompanionLovinJob(
                        lover,
                        humanSpouse,
                        bedForChecks));

                if (bedForChecks == null)
                {
                    sb.AppendLine("无可用床铺引用，后续绑定拆分跳过。");
                    return;
                }

                bool stillBoundWait =
                    ExplicitSocialLovinUtility.IsRemoteLoverStillBoundForHumanWait(
                        humanSpouse,
                        lover,
                        bedForChecks);
                bool physicallyReady =
                    ExplicitSocialLovinUtility.IsRemoteLoverPhysicallyReadyInBed(
                        humanSpouse,
                        lover,
                        bedForChecks);
                bool humanStillBound =
                    ExplicitSocialLovinUtility.IsRemoteHumanStillBoundForLoverCompanion(
                        lover,
                        humanSpouse,
                        bedForChecks);
                bool shouldFail =
                    ExplicitSocialLovinUtility.ShouldFailRemoteLoverCompanionLovin(
                        lover,
                        humanSpouse,
                        bedForChecks);

                sb.AppendLine("IsRemoteLoverStillBoundForHumanWait：" + stillBoundWait);
                sb.AppendLine("IsRemoteLoverPhysicallyReadyInBed：" + physicallyReady);
                sb.AppendLine("IsRemoteHumanStillBoundForLoverCompanion：" + humanStillBound);
                sb.AppendLine("ShouldFailRemoteLoverCompanionLovin：" + shouldFail);

                sb.AppendLine("拆分状态：");
                sb.AppendLine("  恋人仍在 Lovin：" + (lover.CurJobDef == JobDefOf.Lovin));
                sb.AppendLine("  人类仍在 Lovin：" + (humanSpouse.CurJobDef == JobDefOf.Lovin));
                sb.AppendLine(
                    "  恋人 Target A 是人类配偶："
                    + (loverJob != null
                        && loverJob.GetTarget(TargetIndex.A).Thing == humanSpouse));
                sb.AppendLine(
                    "  人类 Target A 是恋人："
                    + (humanJob != null
                        && humanJob.GetTarget(TargetIndex.A).Thing == lover));
                sb.AppendLine(
                    "  恋人 Target B 是同一张床："
                    + (loverJob != null
                        && loverJob.GetTarget(TargetIndex.B).Thing == bedForChecks));
                sb.AppendLine(
                    "  人类 Target B 是同一张床："
                    + (humanJob != null
                        && humanJob.GetTarget(TargetIndex.B).Thing == bedForChecks));
                sb.AppendLine("  双方同一地图：" + (lover.Map != null && lover.Map == humanSpouse.Map));
                sb.AppendLine(
                    "  床 Spawned / Destroyed："
                    + bedForChecks.Spawned + " / " + bedForChecks.Destroyed);
                sb.AppendLine(
                    "  恋人存活/Spawned："
                    + (!lover.Dead) + " / " + lover.Spawned);
                sb.AppendLine(
                    "  人类存活/Spawned："
                    + (!humanSpouse.Dead) + " / " + humanSpouse.Spawned);
                sb.AppendLine("  恋人 InBed：" + lover.GetPosture().InBed());
                sb.AppendLine(
                    "  恋人 CurrentBed == Target B："
                    + (lover.CurrentBed() == bedForChecks));
            }
            catch (Exception ex)
            {
                sb.AppendLine("绑定判断区段异常：" + ex);
            }
        }

        private static void AppendPathSection(
            StringBuilder sb,
            Pawn? lover,
            Building_Bed? bed)
        {
            sb.AppendLine("--- 恋人寻路状态 ---");
            if (lover == null)
            {
                sb.AppendLine("无恋人，跳过。");
                return;
            }

            try
            {
                Pawn_PathFollower? pather = lover.pather;
                if (pather == null)
                {
                    sb.AppendLine("pather 缺失。");
                    return;
                }

                sb.AppendLine("pather.Moving：" + pather.Moving);
                sb.AppendLine("pather.MovingNow：" + pather.MovingNow);
                LocalTargetInfo dest = pather.Destination;
                sb.AppendLine(
                    "当前寻路目的地："
                    + (dest.IsValid
                        ? (dest.HasThing
                            ? dest.Thing.LabelCap + " @ " + dest.Cell
                            : dest.Cell.ToString())
                        : "无效"));
                sb.AppendLine(
                    "目的地 PathEndMode：peMode 为 Pawn_PathFollower 私有字段，本次诊断不读取。");
                sb.AppendLine(
                    "当前位置等于目的地 Cell："
                    + (dest.IsValid && lover.Position == dest.Cell));

                if (bed != null)
                {
                    CellRect occupied = bed.OccupiedRect();
                    sb.AppendLine("床 OccupiedRect：" + occupied);
                    sb.AppendLine(
                        "当前位置位于床 OccupiedRect：" + occupied.Contains(lover.Position));

                    IntVec3 slotPos = IntVec3.Invalid;
                    try
                    {
                        slotPos = RestUtility.GetBedSleepingSlotPosFor(lover, bed);
                        sb.AppendLine("GetBedSleepingSlotPosFor：" + slotPos);
                        sb.AppendLine(
                            "当前位置等于该睡眠位：" + (lover.Position == slotPos));
                        sb.AppendLine(
                            "目标睡眠位位于 OccupiedRect：" + occupied.Contains(slotPos));
                        Pawn? slotOcc = bed.GetCurOccupantAt(slotPos);
                        sb.AppendLine(
                            "目标睡眠位当前占用者："
                            + (slotOcc != null
                                ? ExplicitSocialLovinUtility.DescribePawn(slotOcc)
                                : "无"));
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine("GetBedSleepingSlotPosFor 异常：" + ex);
                    }
                }
                else
                {
                    sb.AppendLine("无床铺引用，跳过床格寻路比对。");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("寻路区段异常：" + ex);
            }
        }

        private static void AppendBedSection(
            StringBuilder sb,
            Pawn? humanSpouse,
            Pawn? lover,
            Building_Bed? bed)
        {
            sb.AppendLine("--- 床铺与睡眠位 ---");
            if (bed == null)
            {
                try
                {
                    bed = humanSpouse?.CurrentBed() ?? lover?.CurrentBed();
                }
                catch (Exception ex)
                {
                    sb.AppendLine("解析 CurrentBed 异常：" + ex);
                }
            }

            if (bed == null)
            {
                sb.AppendLine("无法解析床铺。");
                return;
            }

            try
            {
                sb.AppendLine(
                    "床 LabelCap / defName / ThingID："
                    + bed.LabelCap + " / " + (bed.def?.defName ?? "null") + " / " + bed.ThingID);
                sb.AppendLine(
                    "Spawned / Destroyed / IsBurning / Medical："
                    + bed.Spawned + " / " + bed.Destroyed + " / "
                    + bed.IsBurning() + " / " + bed.Medical);
                sb.AppendLine(
                    "Map / Position / Rotation："
                    + (bed.Map != null ? bed.Map.ToString() : "null")
                    + " / " + bed.Position + " / " + bed.Rotation);
                sb.AppendLine("OccupiedRect：" + bed.OccupiedRect());
                sb.AppendLine("SleepingSlotsCount：" + bed.SleepingSlotsCount);
                sb.AppendLine("AnyUnoccupiedSleepingSlot：" + bed.AnyUnoccupiedSleepingSlot);
                sb.AppendLine("AnyUnownedSleepingSlot：" + bed.AnyUnownedSleepingSlot);
                sb.AppendLine(
                    "ForPrisoners / ForSlaves：" + bed.ForPrisoners + " / " + bed.ForSlaves);
                sb.AppendLine(
                    "bed_humanlike / bed_maxBodySize："
                    + (bed.def?.building != null
                        ? bed.def.building.bed_humanlike + " / " + bed.def.building.bed_maxBodySize
                        : "building 缺失"));
                if (lover != null)
                {
                    sb.AppendLine("恋人 BodySize：" + lover.BodySize);
                }

                sb.Append("OwnersForReading：");
                AppendPawnList(sb, bed.CompAssignableToPawn != null ? bed.OwnersForReading : null);
                sb.Append("CurOccupants：");
                bool anyOcc = false;
                foreach (Pawn occ in bed.CurOccupants)
                {
                    if (anyOcc)
                    {
                        sb.Append("，");
                    }

                    sb.Append(ExplicitSocialLovinUtility.DescribePawn(occ));
                    anyOcc = true;
                }

                sb.AppendLine(anyOcc ? string.Empty : "无");

                if (lover != null)
                {
                    sb.AppendLine("恋人是否为床主：" + bed.IsOwner(lover, out _));
                }

                if (humanSpouse != null)
                {
                    sb.AppendLine("人类配偶是否为床主：" + bed.IsOwner(humanSpouse, out _));
                }

                if (lover != null)
                {
                    try
                    {
                        sb.AppendLine(
                            "BedOwnerWillShare(恋人)："
                            + RestUtility.BedOwnerWillShare(bed, lover, null));
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine("BedOwnerWillShare 异常：" + ex);
                    }

                    try
                    {
                        sb.AppendLine(
                            "CanUseBedEver(恋人)："
                            + (bed.def != null && RestUtility.CanUseBedEver(lover, bed.def)));
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine("CanUseBedEver 异常：" + ex);
                    }

                    try
                    {
                        sb.AppendLine(
                            "CanUseBedNow(恋人, checkSocialProperness=false)："
                            + RestUtility.CanUseBedNow(
                                bed,
                                lover,
                                checkSocialProperness: false));
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine("CanUseBedNow 异常：" + ex);
                    }

                    try
                    {
                        sb.AppendLine(
                            "Toils_Bed.BedNoLongerUsable(恋人)："
                            + Toils_Bed.BedNoLongerUsable(lover, bed, forcePrisoner: false));
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine("BedNoLongerUsable 异常：" + ex);
                    }

                    try
                    {
                        bool reserved = lover.Map != null
                            && lover.Map.reservationManager.ReservedBy(bed, lover);
                        sb.AppendLine("恋人是否已预约床：" + reserved);
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine("ReservedBy 异常：" + ex);
                    }

                    try
                    {
                        sb.AppendLine(
                            "恋人 CanReserve 床（不改变预约）："
                            + lover.CanReserve(bed, bed.SleepingSlotsCount, 0));
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine("CanReserve 异常：" + ex);
                    }
                }

                sb.AppendLine("逐个睡眠位：");
                for (int i = 0; i < bed.SleepingSlotsCount; i++)
                {
                    try
                    {
                        IntVec3 slotPos = bed.GetSleepingSlotPos(i);
                        string ownerLabel = "无";
                        if (bed.CompAssignableToPawn != null
                            && bed.OwnersForReading != null
                            && i < bed.OwnersForReading.Count
                            && bed.OwnersForReading[i] != null)
                        {
                            ownerLabel = ExplicitSocialLovinUtility.DescribePawn(
                                bed.OwnersForReading[i]);
                        }

                        Pawn? occupant = bed.GetCurOccupant(i);
                        sb.AppendLine(
                            "  [" + i + "] pos=" + slotPos
                            + "，所有者=" + ownerLabel
                            + "，占用者="
                            + (occupant != null
                                ? ExplicitSocialLovinUtility.DescribePawn(occupant)
                                : "无")
                            + "，恋人在此格="
                            + (lover != null && lover.Position == slotPos)
                            + "，人类在此格="
                            + (humanSpouse != null && humanSpouse.Position == slotPos));
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine("  睡眠位 " + i + " 查询异常：" + ex);
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("床铺区段异常：" + ex);
            }
        }

        private static void AppendPawnList(StringBuilder sb, List<Pawn>? pawns)
        {
            if (pawns == null || pawns.Count == 0)
            {
                sb.AppendLine("无");
                return;
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append("，");
                }

                sb.Append(ExplicitSocialLovinUtility.DescribePawn(pawns[i]));
            }

            sb.AppendLine();
        }

        private static bool AppendHarmonyPatchSection(StringBuilder sb)
        {
            sb.AppendLine("--- Harmony 补丁加载检查 ---");
            sb.AppendLine("当前 MOD Harmony ID：" + ModInit.HarmonyId);

            bool allResolved = true;
            bool ourPatchesPresent = true;

            allResolved &= ReportMethodPatches(
                sb,
                AccessTools.Method(typeof(JobDriver_Lovin), "MakeNewToils"),
                "JobDriver_Lovin.MakeNewToils",
                ref ourPatchesPresent);
            allResolved &= ReportMethodPatches(
                sb,
                AccessTools.Method(
                    typeof(LovePartnerRelationUtility),
                    nameof(LovePartnerRelationUtility.GetPartnerInMyBed)),
                "LovePartnerRelationUtility.GetPartnerInMyBed",
                ref ourPatchesPresent);
            allResolved &= ReportMethodPatches(
                sb,
                AccessTools.Method(
                    typeof(RestUtility),
                    nameof(RestUtility.CanUseBedEver),
                    new[] { typeof(Pawn), typeof(ThingDef) }),
                "RestUtility.CanUseBedEver",
                ref ourPatchesPresent);
            allResolved &= ReportMethodPatches(
                sb,
                AccessTools.Method(typeof(ThinkNode_ChancePerHour_Lovin), "MtbHours"),
                "ThinkNode_ChancePerHour_Lovin.MtbHours",
                ref ourPatchesPresent);
            allResolved &= ReportMethodPatches(
                sb,
                AccessTools.Method(
                    typeof(JobDriver_Lovin),
                    "GenerateRandomMinTicksToNextLovin",
                    new[] { typeof(Pawn) }),
                "JobDriver_Lovin.GenerateRandomMinTicksToNextLovin",
                ref ourPatchesPresent);
            allResolved &= ReportMethodPatches(
                sb,
                AccessTools.Method(
                    typeof(PawnRenderer),
                    "GetBodyPos",
                    new[] { typeof(Vector3), typeof(PawnPosture), typeof(bool).MakeByRefType() }),
                "PawnRenderer.GetBodyPos(Vector3, PawnPosture, out bool)",
                ref ourPatchesPresent);
            allResolved &= ReportMethodPatches(
                sb,
                AccessTools.Method(typeof(PawnRenderer), nameof(PawnRenderer.BodyAngle)),
                "PawnRenderer.BodyAngle",
                ref ourPatchesPresent);
            allResolved &= ReportMethodPatches(
                sb,
                AccessTools.Method(typeof(PawnRenderer), nameof(PawnRenderer.LayingFacing)),
                "PawnRenderer.LayingFacing",
                ref ourPatchesPresent);
            allResolved &= ReportMethodPatches(
                sb,
                AccessTools.Method(
                    typeof(PawnRenderNodeWorker_Body),
                    nameof(PawnRenderNodeWorker_Body.CanDrawNow)),
                "PawnRenderNodeWorker_Body.CanDrawNow",
                ref ourPatchesPresent);

            sb.AppendLine(
                "关键方法全部解析成功：" + allResolved
                + "；本 MOD Harmony ID 均出现在补丁 owner 中：" + ourPatchesPresent);
            return allResolved && ourPatchesPresent;
        }

        private static bool ReportMethodPatches(
            StringBuilder sb,
            MethodBase? method,
            string label,
            ref bool ourPatchesPresent)
        {
            sb.AppendLine("目标：" + label);
            if (method == null)
            {
                sb.AppendLine("  MethodBase：解析失败");
                ourPatchesPresent = false;
                return false;
            }

            sb.AppendLine("  MethodBase：成功 → " + method.FullDescription());
            Patches? info = Harmony.GetPatchInfo(method);
            if (info == null)
            {
                sb.AppendLine("  GetPatchInfo：null（无任何补丁）");
                ourPatchesPresent = false;
                return true;
            }

            bool sawOursOnMethod = false;
            AppendPatchOwnerList(sb, "Prefix", info.Prefixes, ref sawOursOnMethod);
            AppendPatchOwnerList(sb, "Postfix", info.Postfixes, ref sawOursOnMethod);
            AppendPatchOwnerList(sb, "Transpiler", info.Transpilers, ref sawOursOnMethod);
            AppendPatchOwnerList(sb, "Finalizer", info.Finalizers, ref sawOursOnMethod);
            sb.AppendLine("  含本 MOD Harmony ID：" + sawOursOnMethod);
            if (!sawOursOnMethod)
            {
                ourPatchesPresent = false;
            }

            return true;
        }

        private static void AppendPatchOwnerList(
            StringBuilder sb,
            string kind,
            IEnumerable<Patch> patches,
            ref bool sawOursOnMethod)
        {
            List<Patch> list = new List<Patch>();
            foreach (Patch patch in patches)
            {
                list.Add(patch);
            }

            sb.AppendLine("  " + kind + " 数量：" + list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                string owner = list[i].owner ?? "null";
                sb.AppendLine("    owner=" + owner);
                if (owner == ModInit.HarmonyId)
                {
                    sawOursOnMethod = true;
                }
            }
        }

        private static string BuildRuntimeConclusion(
            Pawn clicked,
            Pawn? humanSpouse,
            Pawn? lover,
            Building_Bed? bed,
            bool harmonyOk)
        {
            if (!harmonyOk)
            {
                return "关键 Harmony 补丁未加载（或 MethodBase 解析失败）。请确认游戏实际加载的是包含最新补丁的 DLL。";
            }

            if (lover == null)
            {
                return "当前没有可诊断的授权恋人。";
            }

            bool loverLovin = lover.CurJobDef == JobDefOf.Lovin;
            bool humanLovin = humanSpouse != null && humanSpouse.CurJobDef == JobDefOf.Lovin;
            if (!loverLovin && !humanLovin)
            {
                return "当前没有 Lovin Job。";
            }

            Building_Bed? bedForChecks = bed
                ?? lover.CurrentBed()
                ?? humanSpouse?.CurrentBed();

            if (loverLovin
                && humanSpouse != null
                && bedForChecks != null
                && ExplicitSocialLovinUtility.ShouldFailRemoteLoverCompanionLovin(
                    lover,
                    humanSpouse,
                    bedForChecks))
            {
                return "恋人侧全局失败条件当前成立（ShouldFailRemoteLoverCompanionLovin=true）。";
            }

            if (loverLovin && bedForChecks != null)
            {
                try
                {
                    if (Toils_Bed.BedNoLongerUsable(lover, bedForChecks, forcePrisoner: false))
                    {
                        return "床铺被 BedNoLongerUsable 拒绝。";
                    }
                }
                catch
                {
                    // 结论阶段忽略单次查询异常。
                }
            }

            string toil = lover.jobs?.curDriver?.CurToilString ?? string.Empty;
            bool moving = lover.pather != null && lover.pather.Moving;
            bool inBedPosture = lover.GetPosture().InBed();
            Building_Bed? loverBed = lover.CurrentBed();
            bool renderOk = ExplicitSocialLovinUtility.ShouldUseHumanlikeBedLovinRender(lover);

            if (loverLovin && toil.IndexOf("GotoBed", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "恋人仍停留在 GotoBed。";
            }

            if (loverLovin
                && !moving
                && !inBedPosture
                && toil.IndexOf("LayDown", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return "恋人寻路已经停止，但尚未进入最终 LayDown。";
            }

            if (loverLovin
                && toil.IndexOf("LayDown", StringComparison.OrdinalIgnoreCase) >= 0
                && lover.jobs != null
                && (lover.jobs.posture & PawnPosture.InBedMask) == 0
                && !inBedPosture)
            {
                return "恋人已经进入最终 LayDown，但 jobs.posture 仍不是 LayingInBed。";
            }

            if (inBedPosture && loverBed == null)
            {
                return "恋人已经是 LayingInBed，但 CurrentBed 为空。";
            }

            if (inBedPosture && loverBed != null && !renderOk)
            {
                return "恋人已经是 LayingInBed 且 CurrentBed 正确，但 ShouldUseHumanlikeBedLovinRender 为 false。";
            }

            if (inBedPosture && loverBed != null && renderOk)
            {
                return "工作和姿态均正确，问题应位于渲染补丁。";
            }

            return "当前数据没有发现明显异常。";
        }
    }
}
