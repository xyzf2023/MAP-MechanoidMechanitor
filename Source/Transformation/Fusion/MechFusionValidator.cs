using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体唯一的统一 Validator。按钮显示与实际执行都必须调用这里，
    /// 实际执行前会再次完整校验，不依赖按钮显示时的判断结果。
    /// </summary>
    public static class MechFusionValidator
    {
        public static bool CanStart(
            Pawn? source,
            Pawn? wearer,
            out string? failureReason)
        {
            failureReason = null;
            if (source == null
                || source.Destroyed
                || source.Discarded
                || source.Dead
                || !source.Spawned
                || source.Map == null)
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Fusion.Failure.SourceUnavailable"
                        .Translate();
                return false;
            }

            if (!MechFusionEligibilityUtility.EnsureEligibilityRecord(source)
                || !MechFusionEligibilityUtility.HasFusionEligibility(source))
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Fusion.Failure.NoEligibility"
                        .Translate();
                return false;
            }

            if (!MechTransformationUtility.IsInPawnForm(source)
                || MechTransformationUtility.IsTransitionInProgress(source))
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Fusion.Failure.SourceFormInvalid"
                        .Translate();
                return false;
            }

            if (wearer == null
                || wearer.Destroyed
                || wearer.Discarded
                || wearer.Dead
                || !wearer.Spawned
                || wearer.Map == null)
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Fusion.Failure.TargetUnavailable"
                        .Translate();
                return false;
            }

            if (!wearer.RaceProps.Humanlike)
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Fusion.Failure.TargetNotHumanlike"
                        .Translate();
                return false;
            }

            if (wearer.Faction == null || !wearer.Faction.IsPlayerSafe())
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Fusion.Failure.TargetUnavailable"
                        .Translate();
                return false;
            }

            if (wearer.apparel == null)
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Fusion.Failure.CannotWear"
                        .Translate();
                return false;
            }

            if (wearer.Map != source.Map)
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Fusion.Failure.NotSameMap"
                        .Translate();
                return false;
            }

            if (!IsOverseerOf(source, wearer))
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Fusion.Failure.TargetNotOverseer"
                        .Translate();
                return false;
            }

            if (GameComponent_MechFusionSessionRegistry.TryGetSessionForWearer(
                    wearer,
                    out _)
                || GameComponent_MechFusionSessionRegistry.TryGetSessionForSource(
                    source,
                    out _))
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Fusion.Failure.SessionBusy"
                        .Translate();
                return false;
            }

            ThingDef? shellDef = GetShellDef();
            if (shellDef == null)
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Fusion.Failure.ShellMissing"
                        .Translate();
                return false;
            }

            if (!ApparelUtility.HasPartsToWear(wearer, shellDef))
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Fusion.Failure.CannotWear"
                        .Translate();
                return false;
            }

            return true;
        }

        internal static ThingDef? GetShellDef()
        {
            return DefDatabase<ThingDef>.GetNamedSilentFail(
                MechFusionDefNames.ShellDefName);
        }

        internal static bool IsOverseerOf(Pawn source, Pawn wearer)
        {
            if (ReferenceEquals(source.GetOverseer(), wearer))
            {
                return true;
            }

            List<Pawn>? controlled = wearer.mechanitor?.ControlledPawns;
            return controlled != null && controlled.Contains(source);
        }
    }
}
