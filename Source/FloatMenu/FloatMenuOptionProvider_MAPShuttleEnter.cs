using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 为仅凭数据处理分配获得 ShuttlePilot、因而没有 ColonistLikeFloatMenu 的机械体
    /// 提供玩家穿梭机登机入口。已有殖民者式右键的机械族机械师走原版 FromThing 路径。
    /// </summary>
    public class FloatMenuOptionProvider_MAPShuttleEnter : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;

        protected override bool Undrafted => true;

        protected override bool Multiselect => false;

        protected override bool MechanoidCanDo => true;

        protected override bool AppliesInt(FloatMenuContext context)
        {
            return ModsConfig.OdysseyActive;
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(
            Thing clickedThing,
            FloatMenuContext context)
        {
            if (!ModsConfig.OdysseyActive || clickedThing == null)
            {
                yield break;
            }

            CompShuttle? shuttle = clickedThing.TryGetComp<CompShuttle>();
            if (shuttle == null || !shuttle.IsPlayerShuttle)
            {
                yield break;
            }

            Pawn? selPawn = context.FirstSelectedPawn;
            if (selPawn == null
                || !MAPShuttlePilotUtility.CanServeAsShuttlePilot(selPawn))
            {
                yield break;
            }

            // 已有殖民者式右键时，原版 FloatMenuOptionProvider_FromThing 会调用 CompFloatMenuOptions。
            if (CompColonistLikeFloatMenuUser.PawnCanUseColonistLikeFloatMenu(selPawn))
            {
                yield break;
            }

            string text = "EnterShuttle".Translate();

            if (selPawn.Dead || selPawn.Downed || !selPawn.Spawned)
            {
                yield break;
            }

            if (!shuttle.IsAllowedNow(selPawn))
            {
                yield return new FloatMenuOption(
                    text + " (" + "NotAllowed".Translate() + ")",
                    null);
                yield break;
            }

            if (!selPawn.CanReach(
                    clickedThing,
                    PathEndMode.Touch,
                    Danger.Deadly,
                    false,
                    false,
                    TraverseMode.ByPawn))
            {
                yield return new FloatMenuOption(
                    text + " (" + "NoPath".Translate() + ")",
                    null);
                yield break;
            }

            yield return new FloatMenuOption(
                text,
                delegate
                {
                    CompTransporter? transporter = shuttle.Transporter;
                    if (transporter == null)
                    {
                        return;
                    }

                    if (!transporter.LoadingInProgressOrReadyToLaunch)
                    {
                        TransporterUtility.InitiateLoading(
                            Gen.YieldSingle(transporter));
                    }

                    Job job = JobMaker.MakeJob(JobDefOf.EnterTransporter, clickedThing);
                    selPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                });
        }
    }
}
