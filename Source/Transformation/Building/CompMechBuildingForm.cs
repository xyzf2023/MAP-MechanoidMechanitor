using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_MechBuildingForm : CompProperties
    {
        // 只有允许直接建造的形态配置此项；转换而来的建筑沿用原始 Pawn。
        public PawnKindDef? constructedPawnKind;

        public CompProperties_MechBuildingForm()
        {
            compClass = typeof(CompMechBuildingForm);
        }
    }

    /// <summary>
    /// 建筑形态专用交互层。身份引用仍由通用 CompMechFormCarrier 保存。
    /// 同时保存初始耐久与可修复损伤额度，供恢复机械体时结算建筑阶段损伤和维修。
    /// </summary>
    public sealed class CompMechBuildingForm : ThingComp
    {
        public const int RestoreDurationTicks = 180;

        // 队列执行失败时结束表现；读档仍由原有的剩余时间恢复进度。
        internal float RestoreProgress => restoreInProgress
            && (!restoreQueued || GameComponent_MechBuildingConversionQueue.IsRestoreQueued(parent))
                ? Mathf.Clamp01(1f - (float)remainingRestoreTicks / RestoreDurationTicks) : 0f;

        private IntVec3 lastMapPosition = IntVec3.Invalid;
        private Rot4 lastMapRotation = Rot4.South;
        private bool restoreInProgress;
        private bool restoreQueued;
        private int remainingRestoreTicks;
        private Effecter? progressBarEffecter;
        private bool hasInitialHealthFraction;
        private float initialHealthFraction = 1f;
        // -1 表示旧存档没有修复额度快照；有耐久基线时按恢复时的可修复损伤兼容。
        private float initialRepairableDamage = -1f;
        private bool hasDestructionHealthSnapshot;
        private float destructionHealthFraction = 1f;

        // 建筑形态自己的权威身份与能源快照。通用形态记录只负责
        // Pawn/Building 链接，不承担具体能力数据。
        private readonly MechBuildingSourceState sourceState = new MechBuildingSourceState();

        internal Pawn? StoredSourcePawn => sourceState.StoredSourcePawn;

        internal MechBuildingSourceState SourceState => sourceState;

        internal float InitialRepairableDamage => initialRepairableDamage;

        internal void CaptureInitialHealthFraction(float fraction, float repairableDamage)
        {
            initialHealthFraction = Mathf.Clamp01(fraction);
            hasInitialHealthFraction = true;
            initialRepairableDamage = Mathf.Max(0f, repairableDamage);
        }

        internal bool TryGetDurabilityFractions(
            bool useDestructionSnapshot,
            out float initialFraction,
            out float currentFraction)
        {
            initialFraction = Mathf.Clamp01(initialHealthFraction);
            currentFraction = useDestructionSnapshot
                && hasDestructionHealthSnapshot
                    ? Mathf.Clamp01(destructionHealthFraction)
                    : GetCurrentHealthFraction();
            return hasInitialHealthFraction;
        }

        internal void CaptureSourceState(Pawn source)
        {
            sourceState.CaptureSourceState(source);
        }

        internal void EnsureSourceStateForRecovery(Pawn source)
        {
            sourceState.EnsureSourceStateForRecovery(source, parent.Faction);
        }

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
            Scribe_Values.Look(
                ref hasInitialHealthFraction,
                "hasInitialHealthFraction",
                defaultValue: false);
            Scribe_Values.Look(
                ref initialHealthFraction,
                "initialHealthFraction",
                defaultValue: 1f);
            Scribe_Values.Look(
                ref initialRepairableDamage,
                "initialRepairableDamage",
                defaultValue: -1f);
            Scribe_Values.Look(
                ref hasDestructionHealthSnapshot,
                "hasDestructionHealthSnapshot",
                defaultValue: false);
            Scribe_Values.Look(
                ref destructionHealthFraction,
                "destructionHealthFraction",
                defaultValue: 1f);
            // 保持既有建筑存档的节点层级和键名。
            sourceState.ExposeData();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                restoreQueued = false;
                progressBarEffecter = null;
                destructionHealthFraction =
                    Mathf.Clamp01(destructionHealthFraction);
                initialHealthFraction = Mathf.Clamp01(initialHealthFraction);
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

            CompMechFormCarrier? carrier =
                parent.TryGetComp<CompMechFormCarrier>();
            PawnKindDef? constructedKind =
                ((CompProperties_MechBuildingForm)props).constructedPawnKind;
            if (!respawningAfterLoad && constructedKind != null
                && carrier?.Committed == false && StoredSourcePawn == null
                && parent.Faction == Faction.OfPlayer)
            {
                MechBuildingConversionService.TryInitializeConstructedBuilding(
                    parent, constructedKind);
            }
            Pawn? source = carrier?.SourcePawn ?? StoredSourcePawn;
            if (carrier?.Committed == true
                && carrier.CarrierForm == MechTransformationForm.Building
                && source != null
                && !source.Destroyed
                && !source.Discarded)
            {
                EnsureSourceStateForRecovery(source);
                if (!source.Spawned && !Find.WorldPawns.Contains(source))
                {
                    Find.WorldPawns.PassToWorld(
                        source,
                        PawnDiscardDecideMode.KeepForever);
                }

                MechFusionSourceUtility.ApplyDormantGuard(source);
            }
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
                    out string? failureReason))
            {
                CancelRestoreWarmup();
                if (parent.Spawned && !parent.Destroyed)
                {
                    MechBuildingConversionService.Reject(
                        parent, failureReason, sendFailureMessage: true);
                }
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
                icon = CompMechBuildingConversion.GetGizmoIcon(
                    carrier.SourcePawn ?? StoredSourcePawn),
                action = BeginRestoreWarmup
            };

            if (!MechBuildingConversionService.CanRestore(
                    parent,
                    allowDestroyedCarrier: false,
                    out string? disabledReason))
            {
                command.Disable(
                    MechBuildingConversionService.PlayerFailureReason(disabledReason));
            }

            yield return command;
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            if (parent.Spawned
                && parent.Map == Find.CurrentMap
                && parent.Faction == Faction.OfPlayer
                && parent.GetComp<CompPower>() != null)
            {
                // 复用原版电网叠加层；电源关闭时仍允许查看线路。
                OverlayDrawHandler.DrawPowerGridOverlayThisFrame();
            }
        }

        public override string CompInspectStringExtra()
        {
            CompMechFormCarrier? carrier =
                parent.TryGetComp<CompMechFormCarrier>();
            Pawn? sourcePawn = carrier?.SourcePawn ?? StoredSourcePawn;
            if (carrier?.Committed != true || sourcePawn == null)
            {
                return string.Empty;
            }

            return "MAP_MechanoidMechanitor.Transformation.Building.StoredPawn"
                .Translate(sourcePawn.LabelShortCap);
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            destructionHealthFraction = GetCurrentHealthFraction();
            hasDestructionHealthSnapshot = true;
            CancelRestoreWarmup();
            base.PostDestroy(mode, previousMap);
            if (previousMap == null)
            {
                return;
            }

            CompMechFormCarrier? carrier =
                parent.TryGetComp<CompMechFormCarrier>();
            Pawn? sourcePawn = carrier?.SourcePawn ?? StoredSourcePawn;
            if (carrier?.Committed != true
                || carrier.CarrierForm != MechTransformationForm.Building
                || sourcePawn == null)
            {
                return;
            }

            GameComponent_MechBuildingConversionQueue.QueueEmergencyRestore(
                parent,
                sourcePawn,
                previousMap,
                lastMapPosition.IsValid ? lastMapPosition : parent.Position,
                lastMapRotation);
        }

        private float GetCurrentHealthFraction()
        {
            if (!parent.def.useHitPoints || parent.MaxHitPoints <= 0)
            {
                return 1f;
            }

            return Mathf.Clamp01(
                (float)parent.HitPoints / parent.MaxHitPoints);
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
                MechBuildingConversionService.Reject(
                    parent, failureReason, sendFailureMessage: true);
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
            MechBuildingConversionService.Reject(
                parent, failureReason, sendFailureMessage: true);
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
