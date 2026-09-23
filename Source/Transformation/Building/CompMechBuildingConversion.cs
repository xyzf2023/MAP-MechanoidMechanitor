using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_MechBuildingConversion : CompProperties
    {
        public ThingDef? buildingFormDef;
        public ThingDef? buildingStuff;
        public int placementSearchRadius = 8;

        public CompProperties_MechBuildingConversion()
        {
            compClass = typeof(CompMechBuildingConversion);
        }
    }

    /// <summary>
    /// 通用机械体建筑转换组件。组件本身是资格与建筑配置的唯一事实来源，
    /// 并直接提供转换按钮，不经过科研能力同步器或 Pawn_AbilityTracker。
    /// </summary>
    [StaticConstructorOnStartup]
    public sealed class CompMechBuildingConversion : ThingComp
    {
        private static readonly Texture2D ConvertIcon =
            ContentFinder<Texture2D>.Get("UI/Commands/MM_BuildingConversion");

        public CompProperties_MechBuildingConversion Props =>
            (CompProperties_MechBuildingConversion)props;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent is not Pawn pawn
                || pawn.Faction != Faction.OfPlayer
                || !HasValidConfiguration())
            {
                yield break;
            }

            if (pawn.CurJobDef
                == MAPMechanitor_JobDefOf.MAP_MechConvertToBuilding)
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
                        MAPMechanitor_JobDefOf.MAP_MechConvertToBuilding,
                        pawn);
                    if (pawn.jobs == null)
                    {
                        MechBuildingConversionService.Reject(
                            pawn,
                            "Pawn.jobs 为空，无法接管建筑转换工作",
                            sendFailureMessage: true);
                    }
                    else if (!pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc))
                    {
                        MechBuildingConversionService.Reject(
                            pawn,
                            "TryTakeOrderedJob 返回 false，建筑转换工作未开始",
                            sendFailureMessage: true);
                    }
                }
            };

            if (!MechBuildingConversionService.CanConvert(
                    pawn,
                    out string? disabledReason))
            {
                command.Disable(
                    MechBuildingConversionService.PlayerFailureReason(disabledReason));
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
