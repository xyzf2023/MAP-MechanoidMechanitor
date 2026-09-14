using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_ChariotBuildingConversion : CompProperties
    {
        public ThingDef? buildingFormDef;
        public ThingDef? buildingStuff;
        public int placementSearchRadius = 8;

        public CompProperties_ChariotBuildingConversion()
        {
            compClass = typeof(CompChariotBuildingConversion);
        }
    }

    /// <summary>
    /// 战车专用建筑转换组件。组件本身是资格与建筑配置的唯一事实来源，
    /// 并直接提供转换按钮，不经过科研能力同步器或 Pawn_AbilityTracker。
    /// </summary>
    public sealed class CompChariotBuildingConversion : ThingComp
    {
        private static readonly Texture2D ConvertIcon =
            ContentFinder<Texture2D>.Get("UI/MM_Custom");

        public CompProperties_ChariotBuildingConversion Props =>
            (CompProperties_ChariotBuildingConversion)props;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent is not Pawn pawn
                || pawn.Faction != Faction.OfPlayer
                || !HasValidConfiguration())
            {
                yield break;
            }

            if (pawn.CurJobDef
                == MAPMechanitor_JobDefOf.MAP_ChariotConvertToBuilding)
            {
                yield break;
            }

            Command_Action command = new Command_Action
            {
                defaultLabel =
                    "MAP_MechanoidMechanitor.Transformation.Building.Convert.Label".Translate(),
                defaultDesc =
                    "MAP_MechanoidMechanitor.Transformation.Building.Convert.Description".Translate(),
                icon = ConvertIcon,
                action = delegate
                {
                    Job job = JobMaker.MakeJob(
                        MAPMechanitor_JobDefOf.MAP_ChariotConvertToBuilding,
                        pawn);
                    if (pawn.jobs == null
                        || !pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc))
                    {
                        Messages.Message(
                            "MAP_MechanoidMechanitor.Transformation.Building.Unavailable"
                                .Translate(),
                            pawn,
                            MessageTypeDefOf.RejectInput,
                            historical: false);
                    }
                }
            };

            if (!MechBuildingConversionService.CanConvert(
                    pawn,
                    out string? disabledReason))
            {
                command.Disable(
                    disabledReason
                        ?? "MAP_MechanoidMechanitor.Transformation.Building.Unavailable"
                            .Translate());
            }

            yield return command;
        }

        private bool HasValidConfiguration()
        {
            if (!MechBuildingConversionProfileUtility.IsBuildingFormDefValid(
                    Props.buildingFormDef,
                    out _))
            {
                return false;
            }

            ThingDef buildingDef = Props.buildingFormDef!;
            return !buildingDef.MadeFromStuff
                || (Props.buildingStuff != null && Props.buildingStuff.IsStuff);
        }
    }
}
