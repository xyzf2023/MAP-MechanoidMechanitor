using System.Collections.Generic;
using MAP_MechanoidMechanitor.Scenarios;
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
        private const string AutonomousDirectiveHediffDefName = "MAP_Justice_SelfWorkMode_AutonomousDirective";
        private const string SelfRepairHediffDefName = "MAP_Justice_SelfWorkMode_SelfRepair";

        private static MechWorkModeDef? autonomousDirectiveDef;
        private static HediffDef? autonomousDirectiveHediffDef;
        private static HediffDef? selfRepairHediffDef;

        public static bool HasSelfWorkMode(Pawn? pawn) =>
            MechanoidMechanitorCapabilityUtility.HasCapability(
                pawn,
                MechanoidMechanitorCapability.SelfWorkMode);

        public static bool TryGetCurrentMode(Pawn? pawn, out MechWorkModeDef? mode)
        {
            mode = null;
            if (pawn == null)
            {
                return false;
            }

            CompJusticeSelfWorkMode? nativeComp = CompJusticeSelfWorkMode.GetFor(pawn);
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
            CompJusticeSelfWorkMode? nativeComp = CompJusticeSelfWorkMode.GetFor(pawn);
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
            if (pawn.health?.hediffSet == null)
            {
                return;
            }

            HediffDef autonomousHediff = GetAutonomousDirectiveHediffDef();
            HediffDef selfRepairHediff = GetSelfRepairHediffDef();

            if (mode.defName == AutonomousDirectiveDefName)
            {
                RemoveAllHediffsOfDef(pawn, selfRepairHediff);
                if (pawn.health.hediffSet.GetFirstHediffOfDef(autonomousHediff) == null)
                {
                    pawn.health.AddHediff(autonomousHediff);
                }
            }
            else
            {
                RemoveAllHediffsOfDef(pawn, autonomousHediff);
                if (pawn.health.hediffSet.GetFirstHediffOfDef(selfRepairHediff) == null)
                {
                    pawn.health.AddHediff(selfRepairHediff);
                }
            }
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

        private static MechWorkModeDef GetAutonomousDirectiveDef()
        {
            return autonomousDirectiveDef ??=
                DefDatabase<MechWorkModeDef>.GetNamed(AutonomousDirectiveDefName);
        }

        private static HediffDef GetAutonomousDirectiveHediffDef()
        {
            return autonomousDirectiveHediffDef ??=
                DefDatabase<HediffDef>.GetNamed(AutonomousDirectiveHediffDefName);
        }

        private static HediffDef GetSelfRepairHediffDef()
        {
            return selfRepairHediffDef ??=
                DefDatabase<HediffDef>.GetNamed(SelfRepairHediffDefName);
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
