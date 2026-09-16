using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public sealed class FloatMenuOptionProvider_MechServiceStation : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool MechanoidCanDo => true;

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing clickedThing, FloatMenuContext context)
        {
            CompMechServiceStation? station = clickedThing.TryGetComp<CompMechServiceStation>();
            Pawn pawn = context.FirstSelectedPawn;
            if (station == null || pawn == null || !MechServicePolicyUtility.IsLegalPawn(pawn)) yield break;
            if (MechServicePolicyUtility.IsServiceJob(pawn)
                && pawn.CurJob.targetA.Thing == clickedThing && pawn.CurJob.playerForced)
            {
                yield return new FloatMenuOption("取消整备", () =>
                {
                    if (MechServicePolicyUtility.IsServiceJob(pawn)
                        && pawn.CurJob.targetA.Thing == clickedThing)
                        pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                });
                yield break;
            }
            string? reason = !station.Enabled ? "整备台未启用"
                : !MechServicePolicyUtility.IsAllowed(station, pawn)
                ? station.Mode == MechServiceStationMode.AssignedOnly ? "仅限指定机械族" : "仅限机械族机械师"
                : !station.Powered ? "未通电"
                : !MechServicePolicyUtility.HasEntryNeed(station, pawn, true) ? "无需整备"
                : !station.CanReach(pawn, true) ? "无法到达" : null;
            if (reason != null)
            {
                yield return new FloatMenuOption("优先使用整备台：" + reason, null);
                yield break;
            }
            yield return new FloatMenuOption("优先使用整备台", () =>
            {
                // 点击时再次校验，菜单打开期间可能修改模式或指定对象。
                if (!MechServicePolicyUtility.IsAllowed(station, pawn)
                    || !station.Powered || !station.CanReach(pawn, true)
                    || !MechServicePolicyUtility.HasEntryNeed(station, pawn, true)) return;
                if (MechServicePolicyUtility.IsServiceJob(pawn)
                    && pawn.CurJob.targetA.Thing == clickedThing)
                {
                    station.PromoteToManual(pawn, pawn.CurJob);
                    return;
                }
                Job requested = JobMaker.MakeJob(MechServiceStationDefOf.MAP_Job_UseMechServiceStation,
                    station.parent, station.ServiceCell);
                requested.playerForced = true;
                pawn.jobs.TryTakeOrderedJob(requested, JobTag.SatisfyingNeeds);
            });
        }
    }
}
