using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace MAP_MechanoidMechanitor
{
    public sealed class JobDriver_AnnihilationCannon : JobDriver
    {
        private int chargeTicks;
        private int chargeDuration;
        private bool launched;
        private IntVec3 chargePosition = IntVec3.Invalid;
        private Map? chargeMap;
        private Sustainer? aimingSound;
        private Mote_AnnihilationWarmup? aimMote;
        private Mote_AnnihilationWarmup? chargeMote;
        private Mote_AnnihilationWarmup? targetMote;
        private CompAnnihilationCannon? Cannon => pawn.GetComp<CompAnnihilationCannon>();
        internal float ChargeProgress => chargeDuration > 0 ? (float)chargeTicks / chargeDuration : 0f;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFailCondition(() => !CanContinueCharge());
            AddFinishAction(_ =>
            {
                aimingSound?.End();
                aimingSound = null;
                // 与原版 Stance_Warmup 一致：只停止维护，不直接 Destroy 特效。
                // Aim 自行淡出，Charge/Target 在下一次 Mote 更新时消失。
                aimMote = null;
                chargeMote = null;
                targetMote = null;
                chargeDuration = 0;
            });
            yield return Toils_General.StopDead();
            Toil charge = ToilMaker.MakeToil("AnnihilationCharge");
            charge.defaultCompleteMode = ToilCompleteMode.Never;
            charge.handlingFacing = true;
            charge.initAction = () =>
            {
                // 开始蓄力时采样瞄准时间系数；实际时长随 Job 存档，读档不重复乘算。
                chargeDuration = Cannon!.WarmupTicksFor(pawn);
                chargePosition = pawn.Position;
                chargeMap = pawn.Map;
                MaintainVisuals();
            };
            charge.tickAction = () =>
            {
                if (!CanContinueCharge())
                {
                    EndJobWith(JobCondition.InterruptForced);
                    return;
                }
                pawn.rotationTracker.FaceCell(job.targetA.Cell);
                if (aimingSound == null || aimingSound.Ended)
                    aimingSound = DefDatabase<SoundDef>.GetNamedSilentFail("HellsphereCannon_Aiming")
                        ?.TrySpawnSustainer(SoundInfo.InMap(pawn, MaintenanceType.PerTick));
                aimingSound?.Maintain();
                MaintainVisuals();
                chargeTicks++;
                if (chargeTicks >= chargeDuration) ReadyForNextToil();
            };
            charge.WithProgressBar(TargetIndex.A, () => ChargeProgress);
            yield return charge;
            Toil launch = ToilMaker.MakeToil("AnnihilationLaunch");
            launch.defaultCompleteMode = ToilCompleteMode.Instant;
            launch.initAction = () =>
            {
                if (launched || Cannon == null) return;
                // Toil 切换到发射时再验证一次，受控/位移后不能补发已经取消的攻击。
                if (!CanContinueCharge())
                {
                    EndJobWith(JobCondition.InterruptForced);
                    return;
                }
                // 状态先提交，防止读档/Job 重入导致重复发射。
                launched = true;
                AnnihilationShot shot = (AnnihilationShot)ThingMaker.MakeThing(AnnihilationCannonDefOf.MAP_AnnihilationShot);
                shot.Initialize(pawn, job.targetA.Cell, Cannon.Props);
                GenSpawn.Spawn(shot, pawn.Position, pawn.Map);
                Cannon.NotifyLaunched();
                DefDatabase<SoundDef>.GetNamedSilentFail("Shot_HellsphereCannonGun")
                    ?.PlayOneShot(new TargetInfo(pawn));
            };
            yield return launch;
        }

        private bool CanContinueCharge()
        {
            CompAnnihilationCannon? cannon = Cannon;
            if (cannon == null || !cannon.CanOperate(pawn)
                || !cannon.ValidTarget(pawn, job.targetA.Cell)) return false;
            if (chargeDuration > 0 && !chargePosition.IsValid)
            {
                // 旧存档缺少锚点时，等 Pawn 真正回到地图后再补齐，不能在 PostLoadInit 读取 Map。
                chargePosition = pawn.Position;
                chargeMap = pawn.Map;
            }
            // StopDead 执行前允许 Pawn 仍在移动；开始蓄力后锁定地图和所在格。
            return chargeDuration <= 0 || (pawn.Map == chargeMap && pawn.Position == chargePosition
                && pawn.pather?.Moving != true && pawn.stances?.FullBodyBusy != true);
        }

        private void MaintainVisuals()
        {
            MaintainMote(ref aimMote, AnnihilationCannonDefOf.Mote_MAP_AnnihilationAim, 0);
            MaintainMote(ref chargeMote, AnnihilationCannonDefOf.Mote_MAP_AnnihilationCharge, 1);
            MaintainMote(ref targetMote, AnnihilationCannonDefOf.Mote_MAP_AnnihilationTarget, 2);
        }

        private void MaintainMote(ref Mote_AnnihilationWarmup? mote, ThingDef def, int part)
        {
            if (mote == null || mote.Destroyed)
            {
                mote = (Mote_AnnihilationWarmup)ThingMaker.MakeThing(def);
                mote.UpdateCharge(pawn, job.targetA.Cell, ChargeProgress, chargeTicks / 60f, part);
                GenSpawn.Spawn(mote, pawn.Position, pawn.Map);
                // Mote 不存档；读档后按已保存的蓄力进度恢复淡入年龄。
                mote.ForceSpawnTick(Find.TickManager.TicksGame - chargeTicks);
            }
            mote.UpdateCharge(pawn, job.targetA.Cell, ChargeProgress, chargeTicks / 60f, part);
            mote.Maintain();
        }

        public override void ExposeData()
        {
            // base 在 PostLoadInit 会重建 Toil；不在重建过程中重置已保存的进度。
            Scribe_Values.Look(ref chargeTicks, "annihilationChargeTicks");
            Scribe_Values.Look(ref chargeDuration, "annihilationChargeDuration");
            Scribe_Values.Look(ref launched, "annihilationLaunched");
            Scribe_Values.Look(ref chargePosition, "annihilationChargePosition", IntVec3.Invalid);
            Scribe_References.Look(ref chargeMap, "annihilationChargeMap");
            base.ExposeData();
        }
    }
}
