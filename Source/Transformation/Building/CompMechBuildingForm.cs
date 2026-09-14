using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_MechBuildingForm : CompProperties
    {
        public CompProperties_MechBuildingForm()
        {
            compClass = typeof(CompMechBuildingForm);
        }
    }

    /// <summary>
    /// 建筑形态专用交互层。身份引用仍由通用 CompMechFormCarrier 保存。
    /// 建筑恢复机械体时由本 Comp 维护 180 ticks 读条，完成后仍通过安全队列执行实体替换。
    /// </summary>
    public sealed class CompMechBuildingForm : ThingComp
    {
        public const int RestoreDurationTicks = 180;

        private IntVec3 lastMapPosition = IntVec3.Invalid;
        private Rot4 lastMapRotation = Rot4.South;
        private bool restoreInProgress;
        private bool restoreQueued;
        private int remainingRestoreTicks;
        private Effecter? progressBarEffecter;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(
                ref restoreInProgress,
                "restoreInProgress",
                defaultValue: false);
            Scribe_Values.Look(
                ref remainingRestoreTicks,
                "remainingRestoreTicks",
                defaultValue: 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                restoreQueued = false;
                progressBarEffecter = null;
                if (!restoreInProgress)
                {
                    remainingRestoreTicks = 0;
                }
                else if (remainingRestoreTicks < 0)
                {
                    remainingRestoreTicks = 0;
                }
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            lastMapPosition = parent.Position;
            lastMapRotation = parent.Rotation;
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!restoreInProgress || restoreQueued)
            {
                return;
            }

            if (!MechBuildingConversionService.CanRestore(
                    parent,
                    allowDestroyedCarrier: false,
                    out _))
            {
                CancelRestoreWarmup();
                return;
            }

            if (remainingRestoreTicks > 0)
            {
                remainingRestoreTicks--;
                TickProgressBar();
            }

            if (remainingRestoreTicks <= 0)
            {
                CompleteRestoreWarmup();
            }
        }

        public override void PostDeSpawn(
            Map map,
            DestroyMode mode = DestroyMode.Vanish)
        {
            lastMapPosition = parent.Position;
            lastMapRotation = parent.Rotation;
            CancelRestoreWarmup();
            base.PostDeSpawn(map, mode);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            CompMechFormCarrier? carrier =
                parent.TryGetComp<CompMechFormCarrier>();
            if (carrier?.Committed != true
                || carrier.CarrierForm != MechTransformationForm.Building
                || restoreInProgress
                || restoreQueued)
            {
                yield break;
            }

            Command_Action command = new Command_Action
            {
                defaultLabel =
                    "MAP_MechanoidMechanitor.Transformation.Building.Restore.Label".Translate(),
                defaultDesc =
                    "MAP_MechanoidMechanitor.Transformation.Building.Restore.Description".Translate(),
                icon = TexCommand.Install,
                action = BeginRestoreWarmup
            };

            if (!MechBuildingConversionService.CanRestore(
                    parent,
                    allowDestroyedCarrier: false,
                    out string? disabledReason))
            {
                command.Disable(
                    disabledReason
                        ?? "MAP_MechanoidMechanitor.Transformation.Building.RestoreFailed"
                            .Translate());
            }

            yield return command;
        }

        public override string CompInspectStringExtra()
        {
            CompMechFormCarrier? carrier =
                parent.TryGetComp<CompMechFormCarrier>();
            Pawn? sourcePawn = carrier?.SourcePawn;
            if (carrier?.Committed != true || sourcePawn == null)
            {
                return string.Empty;
            }

            return "MAP_MechanoidMechanitor.Transformation.Building.StoredPawn"
                .Translate(sourcePawn.LabelShortCap);
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            CancelRestoreWarmup();
            base.PostDestroy(mode, previousMap);
            if (previousMap == null)
            {
                return;
            }

            CompMechFormCarrier? carrier =
                parent.TryGetComp<CompMechFormCarrier>();
            if (carrier?.Committed != true
                || carrier.CarrierForm != MechTransformationForm.Building
                || carrier.SourcePawn == null)
            {
                return;
            }

            GameComponent_MechBuildingConversionQueue.QueueEmergencyRestore(
                parent,
                carrier.SourcePawn,
                previousMap,
                lastMapPosition.IsValid ? lastMapPosition : parent.Position,
                lastMapRotation);
        }

        private void BeginRestoreWarmup()
        {
            if (restoreInProgress || restoreQueued)
            {
                return;
            }

            if (!MechBuildingConversionService.CanRestore(
                    parent,
                    allowDestroyedCarrier: false,
                    out string? failureReason))
            {
                Messages.Message(
                    failureReason
                        ?? "MAP_MechanoidMechanitor.Transformation.Building.RestoreFailed"
                            .Translate(),
                    parent,
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            restoreInProgress = true;
            restoreQueued = false;
            remainingRestoreTicks = RestoreDurationTicks;
        }

        private void CompleteRestoreWarmup()
        {
            CleanupProgressBar();
            if (GameComponent_MechBuildingConversionQueue.TryQueueRestore(
                    parent,
                    out string? failureReason))
            {
                restoreQueued = true;
                return;
            }

            CancelRestoreWarmup();
            Messages.Message(
                failureReason
                    ?? "MAP_MechanoidMechanitor.Transformation.Building.RestoreFailed"
                        .Translate(),
                parent,
                MessageTypeDefOf.RejectInput,
                historical: false);
        }

        private void CancelRestoreWarmup()
        {
            restoreInProgress = false;
            restoreQueued = false;
            remainingRestoreTicks = 0;
            CleanupProgressBar();
        }

        private void TickProgressBar()
        {
            if (!parent.Spawned || parent.Map == null)
            {
                CancelRestoreWarmup();
                return;
            }

            if (progressBarEffecter == null)
            {
                progressBarEffecter = EffecterDefOf.ProgressBar.Spawn();
            }

            progressBarEffecter.EffectTick(parent, TargetInfo.Invalid);
            MoteProgressBar? mote =
                ((SubEffecter_ProgressBar)progressBarEffecter.children[0]).mote;
            if (mote != null)
            {
                mote.progress =
                    1f - (float)remainingRestoreTicks / RestoreDurationTicks;
                mote.offsetZ = -0.5f;
            }
        }

        private void CleanupProgressBar()
        {
            progressBarEffecter?.Cleanup();
            progressBarEffecter = null;
        }
    }
}
