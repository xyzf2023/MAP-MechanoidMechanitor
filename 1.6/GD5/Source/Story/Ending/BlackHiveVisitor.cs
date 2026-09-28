using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor.GD5
{
    /// <summary>普通访客 Pawn；在原版伤害、派系受袭及护甲结算之前撤离。</summary>
    public sealed class Pawn_BlackHiveVisitor : Pawn
    {
        public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            if (TryWithdrawForDamage(dinfo))
            {
                absorbed = true;
                return;
            }
            base.PreApplyDamage(ref dinfo, out absorbed);
        }

        internal bool TryWithdrawForDamage(DamageInfo dinfo)
        {
            if (dinfo.Amount <= 0f && dinfo.Def != DamageDefOf.EMP && !dinfo.Def.causeStun) return false;
            if (Destroyed) return true;
            var state = GameComponent_GD5StoryState.Current;
            if (state?.IsVisitor(this) == true) state.DismissVisitor();
            else GD5BlackHiveEndingService.DismissPawn(this);
            return true;
        }
    }

    public sealed class CompProperties_BlackHiveVisitor : CompProperties
    {
        public CompProperties_BlackHiveVisitor() => compClass = typeof(CompBlackHiveVisitor);
    }

    public sealed class CompBlackHiveVisitor : ThingComp
    {
        private Effecter? departureProgress;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            parent.Rotation = Rot4.North;
        }

        public override void CompTick()
        {
            base.CompTick();
            var state = GameComponent_GD5StoryState.Current;
            // 不在 JobGiver 求职栈中销毁 Pawn，避免后续 JobTracker 继续使用已移除的组件。
            if (parent.Spawned && state?.IsVisitor((Pawn)parent) != true)
            {
                GD5BlackHiveEndingService.DismissPawn((Pawn)parent);
                return;
            }
            if (parent.Spawned)
            {
                var pawn = (Pawn)parent;
                // 清除旧存档仍在执行的闲逛 Job；新 JobGiver 只会派发固定朝向的等待。
                pawn.pather?.StopDead();
                if (pawn.CurJob?.def == JobDefOf.GotoWander)
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                pawn.Rotation = Rot4.North;
            }
            if (parent.Spawned && state?.IsDeparting == true)
            {
                departureProgress ??= EffecterDefOf.ProgressBar.Spawn();
                departureProgress.EffectTick(parent, TargetInfo.Invalid);
                MoteProgressBar? mote = ((SubEffecter_ProgressBar)departureProgress.children[0]).mote;
                if (mote != null)
                {
                    mote.progress = state.DepartureProgress;
                    mote.offsetZ = 1.6f;
                    mote.alwaysShow = true;
                }
            }
            else CleanupProgressBar();
        }

        public override void DrawGUIOverlay()
        {
            if (!parent.Spawned || parent.Position.Fogged(parent.Map)
                || GameComponent_GD5StoryState.Current?.CanTalkTo((Pawn)parent) != true) return;
            // 原版商队交易员的交互标记；复用贴图、位置和脉动，不赋予商人身份。
            parent.Map.overlayDrawer.DrawOverlay(parent, OverlayTypes.QuestionMark);
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            CleanupProgressBar();
            base.PostDeSpawn(map, mode);
        }

        private void CleanupProgressBar()
        {
            departureProgress?.Cleanup();
            departureProgress = null;
        }
    }

    /// <summary>复用手杖贴图与节点参数，但不读取原 Boss 专用字段。</summary>
    public sealed class PawnRenderNodeWorker_BlackHiveVisitorCane : PawnRenderNodeWorker
    {
        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms) =>
            parms.pawn != null && !parms.pawn.DeadOrDowned && base.CanDrawNow(node, parms);
    }

    // 保留已有 Def 引用的类名，行为改为原地朝北等待。
    public sealed class JobGiver_BlackHiveVisitorWander : ThinkNode_JobGiver
    {
        protected override Job? TryGiveJob(Pawn pawn)
        {
            var state = GameComponent_GD5StoryState.Current;
            if (state?.IsVisitor(pawn) != true)
            {
                return null;
            }
            var wait = JobMaker.MakeJob(JobDefOf.Wait_Wander);
            wait.overrideFacing = Rot4.North;
            wait.expiryInterval = 120;
            wait.checkOverrideOnExpire = true;
            return wait;
        }
    }

    public sealed class FloatMenuOptionProvider_BlackHiveVisitor : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool MechanoidCanDo => true;

        protected override FloatMenuOption? GetSingleOptionFor(Pawn clickedPawn, FloatMenuContext context)
        {
            Pawn actor = context.FirstSelectedPawn;
            if (!GD5BlackHiveEndingService.CanSpeak(actor)
                || GameComponent_GD5StoryState.Current?.CanTalkTo(clickedPawn) != true) return null;
            string label = "MAP_GD5.Ending.Talk".Translate();
            if (!actor.CanReach(clickedPawn, PathEndMode.Touch, Danger.Deadly))
                return new FloatMenuOption(label + "（" + "NoPath".Translate() + "）", null);
            if (!actor.CanReserve(clickedPawn))
                return new FloatMenuOption(label + "（" + "Reserved".Translate() + "）", null);
            return new FloatMenuOption(label, () => actor.jobs.TryTakeOrderedJob(
                JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("MAP_GD5_TalkToBlackHiveVisitor"), clickedPawn), JobTag.Misc));
        }
    }

    public sealed class JobDriver_TalkToBlackHiveVisitor : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(TargetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFailCondition(() => !GD5BlackHiveEndingService.CanSpeak(pawn)
                || TargetA.Pawn == null || GameComponent_GD5StoryState.Current?.CanTalkTo(TargetA.Pawn) != true);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Do(() => GD5BlackHiveEndingService.OpenConversation(pawn, TargetA.Pawn));
        }
    }
}
