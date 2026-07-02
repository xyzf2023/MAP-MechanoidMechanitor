using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class EmergencyMechanicalConsciousnessTransferUtility
    {
        public const string JusticeDefName = "MAP_Mech_Justice";
        public const string EmergencyTransferHediffDefName = "MAP_EmergencyConsciousnessTransfer";
        public const int EmergencyTransferDurationTicks = 60000;

        private static readonly HashSet<Pawn> attemptGuard = new HashSet<Pawn>();
        private static HediffDef? cachedEmergencyTransferHediffDef;

        public static bool HasEmergencyConsciousnessTransferHediff(Pawn? pawn)
        {
            HediffDef? def = GetEmergencyTransferHediffDef();
            if (pawn == null || def == null || pawn.health?.hediffSet == null)
            {
                return false;
            }

            return pawn.health.hediffSet.HasHediff(def);
        }

        public static bool TryBeginEmergencyTransferAttempt(Pawn? pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed)
            {
                return false;
            }

            if (!ModsConfig.BiotechActive
                || !JusticeScenarioUtility.IsJusticeScenarioActive
                || !GameComponent_MechanoidMechanitorRegistry.IsMechanicalConsciousnessHost(pawn)
                || !MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
            {
                return false;
            }

            return attemptGuard.Add(pawn);
        }

        public static void TryExecuteEmergencyTransfer(Pawn pawn)
        {
            try
            {
                TryEmergencyTransferInternal(pawn);
            }
            catch (Exception ex)
            {
                LogEmergencyTriggerFailure(pawn, ex);
            }
        }

        public static void ClearAttemptGuard(Pawn? pawn)
        {
            if (pawn != null)
            {
                attemptGuard.Remove(pawn);
            }
        }

        public static Pawn? SelectEmergencyTransferTarget(Pawn source)
        {
            Pawn? firstOtherMechanitor = null;
            HashSet<Pawn> seen = new HashSet<Pawn>();

            foreach (Pawn candidate in GameComponent_MechanoidMechanitorRegistry
                         .GetMechanicalConsciousnessCandidates())
            {
                if (candidate == null
                    || candidate.Destroyed
                    || !seen.Add(candidate))
                {
                    continue;
                }

                if (!MechanicalConsciousnessTransferUtility.CanTransferMechanicalConsciousness(
                        source,
                        candidate))
                {
                    continue;
                }

                if (IsJustice(candidate))
                {
                    return candidate;
                }

                if (firstOtherMechanitor == null)
                {
                    firstOtherMechanitor = candidate;
                }
            }

            return firstOtherMechanitor;
        }

        public static void ApplyOrRefreshEmergencyConsciousnessTransferHediff(Pawn target)
        {
            if (target == null || target.Destroyed)
            {
                return;
            }

            HediffDef? def = GetEmergencyTransferHediffDef();
            if (def == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 转移后 Hediff 失败：未找到 " +
                    $"{EmergencyTransferHediffDefName}，target={target.LabelShort} " +
                    $"（{target.ThingID}）。");
                return;
            }

            if (target.health?.hediffSet == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 转移后 Hediff 失败：target={target.LabelShort} " +
                    $"（{target.ThingID}）缺少 health tracker。");
                return;
            }

            try
            {
                Hediff? existing = target.health.hediffSet.GetFirstHediffOfDef(def);
                if (existing != null)
                {
                    RefreshEmergencyTransferDuration(existing, target);
                    return;
                }

                Hediff hediff = HediffMaker.MakeHediff(def, target);
                target.health.AddHediff(hediff);
                RefreshEmergencyTransferDuration(hediff, target);
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 转移后 Hediff 失败：target={target.LabelShort} " +
                    $"（{target.ThingID}）：{ex}");
            }
        }

        private static bool ShouldAttemptEmergencyTransfer(Pawn source)
        {
            return ModsConfig.BiotechActive
                && JusticeScenarioUtility.IsJusticeScenarioActive
                && !source.Dead
                && !source.Destroyed
                && GameComponent_MechanoidMechanitorRegistry.IsMechanicalConsciousnessHost(source)
                && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(source);
        }

        private static void TryEmergencyTransferInternal(Pawn source)
        {
            if (!ShouldAttemptEmergencyTransfer(source))
            {
                return;
            }

            if (!GameComponent_MechanoidMechanitorRegistry.IsMechanicalConsciousnessHost(source))
            {
                return;
            }

            Pawn? target = SelectEmergencyTransferTarget(source);
            if (target == null)
            {
                return;
            }

            Pawn? hostBefore =
                GameComponent_MechanoidMechanitorRegistry.CurrentMechanicalConsciousnessHost;
            if (!MechanicalConsciousnessTransferUtility.TryTransferMechanicalConsciousness(
                    source,
                    target))
            {
                Log.Error(
                    "[MAP-机械族机械师] 紧急意识转移失败：source={source.LabelShort} " +
                    $"（{source.ThingID}），target={target.LabelShort} " +
                    $"（{target.ThingID}），hostBefore={hostBefore?.LabelShort ?? "null"}。");
                return;
            }

            ApplyOrRefreshEmergencyConsciousnessTransferHediff(target);
        }

        private static bool IsJustice(Pawn pawn)
        {
            return pawn.def?.defName == JusticeDefName;
        }

        private static HediffDef? GetEmergencyTransferHediffDef()
        {
            return cachedEmergencyTransferHediffDef ??=
                DefDatabase<HediffDef>.GetNamedSilentFail(EmergencyTransferHediffDefName);
        }

        private static void RefreshEmergencyTransferDuration(Hediff hediff, Pawn target)
        {
            HediffComp_Disappears? disappears = hediff.TryGetComp<HediffComp_Disappears>();
            if (disappears == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 转移后 Hediff 失败：target={target.LabelShort} " +
                    $"（{target.ThingID}）上的 {EmergencyTransferHediffDefName} 缺少 HediffComp_Disappears。");
                return;
            }

            disappears.SetDuration(EmergencyTransferDurationTicks);
        }

        private static void LogEmergencyTriggerFailure(Pawn source, Exception ex)
        {
            Pawn? currentHost =
                GameComponent_MechanoidMechanitorRegistry.CurrentMechanicalConsciousnessHost;
            Log.Error(
                "[MAP-机械族机械师] 紧急意识转移触发失败：source={source.LabelShort} " +
                $"（{source.ThingID}），currentHost={currentHost?.LabelShort ?? "null"}：{ex}");
        }
    }
}
