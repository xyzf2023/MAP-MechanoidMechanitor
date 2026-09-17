using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
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
    /// 同时保存进入建筑形态时的实际耐久比例，供恢复机械体时只结算建筑阶段新增损伤。
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
        private bool hasInitialHealthFraction;
        private float initialHealthFraction = 1f;
        private bool hasDestructionHealthSnapshot;
        private float destructionHealthFraction = 1f;

        // 建筑形态自己的权威身份与能源快照。通用形态记录只负责
        // Pawn/Building 链接，不承担具体能力数据。
        private Pawn? storedSourcePawn;
        private Faction? originalSourceFaction;
        private Pawn? originalOverseer;
        private int originalControlGroupIndex = -1;
        private bool sourceStateCaptured;
        private float storedEnergy;
        private float storedMaxEnergy;
        private bool energyCaptured;

        internal Pawn? StoredSourcePawn => storedSourcePawn;

        internal void CaptureInitialHealthFraction(float fraction)
        {
            initialHealthFraction = Mathf.Clamp01(fraction);
            hasInitialHealthFraction = true;
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
            storedSourcePawn = source;
            originalSourceFaction = source.Faction;
            originalOverseer = source.GetOverseer()
                ?? MAPOverseerRelationDirectionUtility.FindActualOverseer(source);
            originalControlGroupIndex =
                MechControlGroupPositionUtility.Capture(
                    originalOverseer,
                    source);
            sourceStateCaptured = true;
            CaptureEnergy(source);
        }

        internal void EnsureSourceStateForRecovery(Pawn source)
        {
            storedSourcePawn ??= source;
            if (!sourceStateCaptured)
            {
                Faction? candidate = parent.Faction;
                Faction? player = Faction.OfPlayerSilentFail;
                if (candidate == null
                    || (player != null && candidate.HostileTo(player)))
                {
                    candidate = source.Faction;
                }

                if (player != null
                    && (candidate == null || candidate.HostileTo(player)))
                {
                    // 旧存档中的建筑转换只允许玩家安全阵营来源。
                    candidate = player;
                }

                originalSourceFaction = candidate;
                originalOverseer = source.GetOverseer()
                    ?? MAPOverseerRelationDirectionUtility.FindActualOverseer(
                        source);
                originalControlGroupIndex =
                    MechControlGroupPositionUtility.Capture(
                        originalOverseer,
                        source);
                sourceStateCaptured = true;
            }

            if (!energyCaptured)
            {
                // 旧存档无法追溯转换瞬间的电量，只能以当前真实 Pawn 值迁移；
                // 新转换始终在离开地图前精确捕获。
                CaptureEnergy(source);
            }
        }

        internal bool RestoreSourceIdentity(Pawn source)
        {
            EnsureSourceStateForRecovery(source);
            if (source.Faction != originalSourceFaction)
            {
                source.SetFactionDirect(originalSourceFaction);
            }

            Pawn? overseer = originalOverseer;
            if (source.Dead
                || overseer == null
                || overseer.Dead
                || overseer.Destroyed
                || overseer.Discarded
                || MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(source))
            {
                return true;
            }

            return MechControlGroupPositionUtility.TryRestore(
                overseer,
                source,
                originalControlGroupIndex);
        }

        internal bool TryWriteBackEnergy(Pawn source)
        {
            EnsureSourceStateForRecovery(source);
            if (!energyCaptured || source.Dead)
            {
                return true;
            }

            Pawn_NeedsTracker? needs = source.needs;
            if (needs == null)
            {
                return false;
            }

            Need_MechEnergy? energy = needs.energy;
            if (energy == null)
            {
                needs.AddOrRemoveNeedsAsAppropriate();
                energy = needs.energy;
            }

            if (energy == null)
            {
                return false;
            }

            energy.CurLevel = Mathf.Clamp(storedEnergy, 0f, energy.MaxLevel);
            return true;
        }

        private void CaptureEnergy(Pawn source)
        {
            Need_MechEnergy? energy = source.needs?.energy;
            float fallbackMax = source.RaceProps?.maxMechEnergy ?? 100f;
            storedMaxEnergy = energy?.MaxLevel ?? fallbackMax;
            storedEnergy = energy?.CurLevel ?? storedMaxEnergy;
            if (storedMaxEnergy > 0f)
            {
                storedEnergy = Mathf.Clamp(storedEnergy, 0f, storedMaxEnergy);
            }

            energyCaptured = true;
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
                ref hasDestructionHealthSnapshot,
                "hasDestructionHealthSnapshot",
                defaultValue: false);
            Scribe_Values.Look(
                ref destructionHealthFraction,
                "destructionHealthFraction",
                defaultValue: 1f);
            Scribe_References.Look(ref storedSourcePawn, "storedSourcePawn");
            Scribe_References.Look(
                ref originalSourceFaction,
                "originalSourceFaction");
            Scribe_References.Look(ref originalOverseer, "originalOverseer");
            Scribe_Values.Look(
                ref originalControlGroupIndex,
                "originalControlGroupIndex",
                -1);
            Scribe_Values.Look(
                ref sourceStateCaptured,
                "sourceStateCaptured",
                defaultValue: false);
            Scribe_Values.Look(ref storedEnergy, "storedEnergy");
            Scribe_Values.Look(ref storedMaxEnergy, "storedMaxEnergy");
            Scribe_Values.Look(
                ref energyCaptured,
                "energyCaptured",
                defaultValue: false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                restoreQueued = false;
                progressBarEffecter = null;
                destructionHealthFraction =
                    Mathf.Clamp01(destructionHealthFraction);
                initialHealthFraction = Mathf.Clamp01(initialHealthFraction);
                storedEnergy = Mathf.Max(0f, storedEnergy);
                storedMaxEnergy = Mathf.Max(0f, storedMaxEnergy);
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
            Pawn? source = carrier?.SourcePawn ?? storedSourcePawn;
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
                icon = ContentFinder<Texture2D>.Get("UI/BuildingConversion"),
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
            Pawn? sourcePawn = carrier?.SourcePawn ?? storedSourcePawn;
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
            Pawn? sourcePawn = carrier?.SourcePawn ?? storedSourcePawn;
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
