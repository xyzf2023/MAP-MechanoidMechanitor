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
    /// </summary>
    public sealed class CompMechBuildingForm : ThingComp
    {
        private IntVec3 lastMapPosition = IntVec3.Invalid;
        private Rot4 lastMapRotation = Rot4.South;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            lastMapPosition = parent.Position;
            lastMapRotation = parent.Rotation;
        }

        public override void PostDeSpawn(
            Map map,
            DestroyMode mode = DestroyMode.Vanish)
        {
            lastMapPosition = parent.Position;
            lastMapRotation = parent.Rotation;
            base.PostDeSpawn(map, mode);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            CompMechFormCarrier? carrier =
                parent.TryGetComp<CompMechFormCarrier>();
            if (carrier?.Committed != true
                || carrier.CarrierForm != MechTransformationForm.Building)
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
                action = delegate
                {
                    if (!GameComponent_MechBuildingConversionQueue
                            .TryQueueRestore(parent, out string? failureReason))
                    {
                        Messages.Message(
                            failureReason
                                ?? "MAP_MechanoidMechanitor.Transformation.Building.RestoreFailed"
                                    .Translate(),
                            parent,
                            MessageTypeDefOf.RejectInput,
                            historical: false);
                    }
                }
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
    }
}
