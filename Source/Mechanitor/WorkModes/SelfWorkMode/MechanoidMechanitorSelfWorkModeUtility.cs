using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public static class MechanoidMechanitorSelfWorkModeUtility
    {
        public const string AutonomousDirectiveDefName = "MAP_WorkMode_AutonomousDirective";
        private const string SelfShutdownDefName = "SelfShutdown";
        private const string AutonomousDirectiveHediffDefName =
            "MAP_MechanoidMechanitor_SelfWorkMode_AutonomousDirective";
        private const string SelfRepairHediffDefName =
            "MAP_MechanoidMechanitor_SelfWorkMode_SelfRepair";

        private static MechWorkModeDef? autonomousDirectiveDef;
        private static HediffDef? autonomousDirectiveHediffDef;
        private static HediffDef? selfRepairHediffDef;

        public static bool HasSelfWorkMode(Pawn? pawn) =>
            MechanoidMechanitorCapabilityUtility.HasCapability(
                pawn,
                MechanoidMechanitorCapability.SelfWorkMode);

        public static bool HasSelfWorkModeHediffs(Pawn? pawn)
        {
            if (pawn?.health?.hediffSet == null)
            {
                return false;
            }

            HediffDef? autonomous = TryGetAutonomousDirectiveHediffDef();
            HediffDef? selfRepair = TryGetSelfRepairHediffDef();
            return (autonomous != null && pawn.health.hediffSet.HasHediff(autonomous))
                || (selfRepair != null && pawn.health.hediffSet.HasHediff(selfRepair));
        }

        /// <summary>
        /// 按科研解锁与当前本体工作模式同步两种健康状态。
        /// 未解锁时移除状态但不改变工作模式。
        /// </summary>
        public static void SyncSelfWorkModeEffects(Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead || pawn.health?.hediffSet == null)
            {
                return;
            }

            HediffDef? autonomousHediff = TryGetAutonomousDirectiveHediffDef();
            HediffDef? selfRepairHediff = TryGetSelfRepairHediffDef();
            if (autonomousHediff == null && selfRepairHediff == null)
            {
                return;
            }

            bool unlocked =
                ResearchFeatureUnlockUtility.IsAutonomousDirectiveOptimizationUnlocked();
            if (!HasSelfWorkMode(pawn) || !unlocked)
            {
                if (autonomousHediff != null)
                {
                    RemoveAllHediffsOfDef(pawn, autonomousHediff);
                }

                if (selfRepairHediff != null)
                {
                    RemoveAllHediffsOfDef(pawn, selfRepairHediff);
                }

                return;
            }

            if (!TryGetCurrentMode(pawn, out MechWorkModeDef? mode) || mode == null)
            {
                if (autonomousHediff != null)
                {
                    RemoveAllHediffsOfDef(pawn, autonomousHediff);
                }

                if (selfRepairHediff != null)
                {
                    RemoveAllHediffsOfDef(pawn, selfRepairHediff);
                }

                return;
            }

            ApplyUnlockedModeHediffs(pawn, mode, autonomousHediff, selfRepairHediff);
        }

        public static bool TryGetCurrentMode(Pawn? pawn, out MechWorkModeDef? mode)
        {
            mode = null;
            if (pawn == null)
            {
                return false;
            }

            CompMechanoidMechanitorSelfWorkModeUser? nativeComp =
                CompMechanoidMechanitorSelfWorkModeUser.GetFor(pawn);
            if (nativeComp != null)
            {
                mode = nativeComp.CurrentSelfWorkMode;
                return true;
            }

            if (GameComponent_MechanoidMechanitorRegistry.TryGetAcquiredMechanitorRecord(
                    pawn,
                    out MechanoidMechanitorRecord? record)
                && record != null)
            {
                mode = SanitizeWorkMode(record.SelfWorkMode);
                return true;
            }

            return false;
        }

        public static bool IsSelfShutdown(Pawn? pawn)
        {
            return TryGetCurrentMode(pawn, out MechWorkModeDef? mode)
                && mode?.defName == SelfShutdownDefName;
        }

        public static void SetSelfWorkMode(Pawn pawn, MechWorkModeDef? mode)
        {
            CompMechanoidMechanitorSelfWorkModeUser? nativeComp =
                CompMechanoidMechanitorSelfWorkModeUser.GetFor(pawn);
            if (nativeComp != null)
            {
                nativeComp.SetSelfWorkMode(mode);
                return;
            }

            if (!GameComponent_MechanoidMechanitorRegistry.TryGetAcquiredMechanitorRecord(
                    pawn,
                    out MechanoidMechanitorRecord? record)
                || record == null)
            {
                return;
            }

            MechWorkModeDef sanitized = SanitizeWorkMode(mode);
            if (SanitizeWorkMode(record.SelfWorkMode) == sanitized)
            {
                return;
            }

            record.SelfWorkMode = sanitized;
            ApplyAcquiredSelfWorkMode(pawn, sanitized);
            NotifyModeChanged(pawn, sanitized);
        }

        public static void AddSelfWorkModeFloatMenuOptions(
            List<FloatMenuOption> options,
            Pawn pawn)
        {
            MechWorkModeDef autonomous = GetAutonomousDirectiveDef();
            options.Add(new FloatMenuOption(
                autonomous.LabelCap,
                () => SetSelfWorkMode(pawn, autonomous),
                autonomous.uiIcon,
                Color.white));

            MechWorkModeDef selfShutdown = MechWorkModeDefOf.SelfShutdown;
            options.Add(new FloatMenuOption(
                selfShutdown.LabelCap,
                () => SetSelfWorkMode(pawn, selfShutdown),
                selfShutdown.uiIcon,
                Color.white));
        }

        public static MechWorkModeDef SanitizeWorkMode(MechWorkModeDef? mode)
        {
            if (mode?.defName == AutonomousDirectiveDefName)
            {
                return mode;
            }

            if (mode?.defName == SelfShutdownDefName)
            {
                return MechWorkModeDefOf.SelfShutdown;
            }

            return GetAutonomousDirectiveDef();
        }

        public static void ApplyAcquiredSelfWorkMode(Pawn pawn, MechWorkModeDef mode)
        {
            SyncSelfWorkModeEffects(pawn);
        }

        public static void NotifyModeChanged(Pawn pawn, MechWorkModeDef mode)
        {
            PawnComponentsUtility.AddAndRemoveDynamicComponents(pawn, actAsIfSpawned: true);
            if (mode != MechWorkModeDefOf.Recharge
                && pawn.CurJobDef == JobDefOf.MechCharge
                && pawn.IsCharging())
            {
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }

            pawn.TryGetComp<CompCanBeDormant>()?.WakeUp();
            pawn.jobs?.CheckForJobOverride();
        }

        private static void ApplyUnlockedModeHediffs(
            Pawn pawn,
            MechWorkModeDef mode,
            HediffDef? autonomousHediff,
            HediffDef? selfRepairHediff)
        {
            if (mode.defName == AutonomousDirectiveDefName)
            {
                if (selfRepairHediff != null)
                {
                    RemoveAllHediffsOfDef(pawn, selfRepairHediff);
                }

                if (autonomousHediff != null
                    && pawn.health.hediffSet.GetFirstHediffOfDef(autonomousHediff) == null)
                {
                    pawn.health.AddHediff(autonomousHediff);
                }

                return;
            }

            if (autonomousHediff != null)
            {
                RemoveAllHediffsOfDef(pawn, autonomousHediff);
            }

            if (selfRepairHediff != null
                && pawn.health.hediffSet.GetFirstHediffOfDef(selfRepairHediff) == null)
            {
                pawn.health.AddHediff(selfRepairHediff);
            }
        }

        private static MechWorkModeDef GetAutonomousDirectiveDef()
        {
            return autonomousDirectiveDef ??=
                DefDatabase<MechWorkModeDef>.GetNamed(AutonomousDirectiveDefName);
        }

        private static HediffDef? TryGetAutonomousDirectiveHediffDef()
        {
            return autonomousDirectiveHediffDef ??=
                DefDatabase<HediffDef>.GetNamedSilentFail(AutonomousDirectiveHediffDefName);
        }

        private static HediffDef? TryGetSelfRepairHediffDef()
        {
            return selfRepairHediffDef ??=
                DefDatabase<HediffDef>.GetNamedSilentFail(SelfRepairHediffDefName);
        }

        private static void RemoveAllHediffsOfDef(Pawn pawn, HediffDef def)
        {
            HediffSet hediffSet = pawn.health.hediffSet;
            for (int i = hediffSet.hediffs.Count - 1; i >= 0; i--)
            {
                Hediff hediff = hediffSet.hediffs[i];
                if (hediff.def == def)
                {
                    pawn.health.RemoveHediff(hediff);
                }
            }
        }
    }
}
