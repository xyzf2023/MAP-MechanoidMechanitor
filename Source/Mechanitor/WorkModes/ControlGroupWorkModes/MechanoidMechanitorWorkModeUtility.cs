using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class MechanoidMechanitorWorkModeUtility
    {
        private sealed class MobileCombatMarker
        {
        }

        private static readonly ConditionalWeakTable<Pawn, MobileCombatMarker> mobileCombatPawns =
            new ConditionalWeakTable<Pawn, MobileCombatMarker>();

        private static readonly HashSet<string> mechanoidMechanitorWorkModeDefNames = new HashSet<string>
        {
            "MAP_WorkMode_EfficientExecution",
            "MAP_WorkMode_MobileCombat",
            "MAP_WorkMode_MobileCombat_Guard",
            "MAP_WorkMode_FortifiedDefense"
        };
        private static readonly HashSet<string> mechanoidMechanitorSelfOnlyWorkModeDefNames = new HashSet<string>
        {
            "MAP_WorkMode_AutonomousDirective"
        };
        private static readonly HashSet<string> mechanoidMechanitorWorkEquivalentModeDefNames = new HashSet<string>
        {
            "MAP_WorkMode_EfficientExecution",
            "MAP_WorkMode_MobileCombat",
            "MAP_WorkMode_FortifiedDefense"
        };
        private static readonly HashSet<string> mechanoidMechanitorEscortEquivalentModeDefNames = new HashSet<string>
        {
            "MAP_WorkMode_MobileCombat_Guard"
        };
        private static readonly HashSet<string> mechanoidMechanitorWorkModeHediffDefNames = new HashSet<string>
        {
            "MAP_MechanoidMechanitor_WorkMode_EfficientExecution",
            "MAP_MechanoidMechanitor_WorkMode_MobileCombat",
            "MAP_MechanoidMechanitor_WorkMode_FortifiedDefense"
        };
        private static HediffDef? efficientExecutionDef;
        private static HediffDef? mobileCombatDef;
        private static HediffDef? fortifiedDefenseDef;

        private static HediffDef? GetEfficientExecutionDef()
        {
            return efficientExecutionDef ??= DefDatabase<HediffDef>.GetNamedSilentFail(
                "MAP_MechanoidMechanitor_WorkMode_EfficientExecution");
        }

        private static HediffDef? GetMobileCombatDef()
        {
            return mobileCombatDef ??= DefDatabase<HediffDef>.GetNamedSilentFail(
                "MAP_MechanoidMechanitor_WorkMode_MobileCombat");
        }

        private static HediffDef? GetFortifiedDefenseDef()
        {
            return fortifiedDefenseDef ??= DefDatabase<HediffDef>.GetNamedSilentFail(
                "MAP_MechanoidMechanitor_WorkMode_FortifiedDefense");
        }

        public static void SetMobileCombatFlag(Pawn pawn, bool active)
        {
            if (pawn == null)
            {
                return;
            }

            mobileCombatPawns.Remove(pawn);
            if (active)
            {
                mobileCombatPawns.Add(pawn, new MobileCombatMarker());
            }
        }

        public static bool HasMobileCombatFlag(Pawn pawn)
        {
            return pawn != null && mobileCombatPawns.TryGetValue(pawn, out _);
        }

        public static bool IsMobileCombat(Pawn pawn)
        {
            return HasMobileCombatFlag(pawn);
        }

        public static void EnsureMobileCombatHediff(Pawn? pawn)
        {
            if (pawn?.health?.hediffSet == null || JusticePawnUtility.IsBossJustice(pawn))
            {
                return;
            }

            HediffDef? mobileDef = GetMobileCombatDef();
            if (mobileDef == null)
            {
                Log.ErrorOnce(
                    "[MAP] MAP_MechanoidMechanitor_WorkMode_MobileCombat HediffDef missing.",
                    87422031);
                return;
            }

            if (pawn.health.hediffSet.GetFirstHediffOfDef(mobileDef) == null)
            {
                pawn.health.AddHediff(mobileDef);
            }
        }

        public static void ApplyWorkModeHediff(Pawn pawn, MechWorkModeDef workMode)
        {
            if (pawn?.health?.hediffSet == null)
            {
                return;
            }

            HediffDef? efficientDef = GetEfficientExecutionDef();
            HediffDef? mobileDef = GetMobileCombatDef();
            HediffDef? fortifiedDef = GetFortifiedDefenseDef();

            if (efficientDef != null)
            {
                Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(efficientDef);
                if (existing != null)
                {
                    pawn.health.RemoveHediff(existing);
                }
            }
            if (mobileDef != null)
            {
                Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(mobileDef);
                if (existing != null)
                {
                    pawn.health.RemoveHediff(existing);
                }
            }
            if (fortifiedDef != null)
            {
                Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(fortifiedDef);
                if (existing != null)
                {
                    pawn.health.RemoveHediff(existing);
                }
            }

            if (workMode == null)
            {
                return;
            }

            HediffDef? targetDef = null;
            if (workMode.defName == "MAP_WorkMode_EfficientExecution")
            {
                targetDef = efficientDef;
            }
            else if (workMode.defName == "MAP_WorkMode_MobileCombat" ||
                     workMode.defName == "MAP_WorkMode_MobileCombat_Guard")
            {
                targetDef = mobileDef;
            }
            else if (workMode.defName == "MAP_WorkMode_FortifiedDefense")
            {
                targetDef = fortifiedDef;
            }

            if (targetDef != null && pawn.health.hediffSet.GetFirstHediffOfDef(targetDef) == null)
            {
                pawn.health.AddHediff(targetDef);
            }
        }

        public static bool IsMechanoidMechanitorControlGroup(MechanitorControlGroup controlGroup)
        {
            Pawn? mechanitor = controlGroup?.Tracker?.Pawn;
            return mechanitor != null
                && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(mechanitor);
        }

        public static bool IsMechanoidMechanitorWorkMode(MechWorkModeDef workMode)
        {
            return workMode != null
                && mechanoidMechanitorWorkModeDefNames.Contains(workMode.defName);
        }

        public static bool IsMechanoidMechanitorSelfOnlyWorkMode(MechWorkModeDef workMode)
        {
            return workMode != null
                && mechanoidMechanitorSelfOnlyWorkModeDefNames.Contains(workMode.defName);
        }

        public static bool SatisfiesVanillaWorkMode(
            MechWorkModeDef? actualMode,
            MechWorkModeDef? requestedMode)
        {
            if (actualMode == null || requestedMode == null)
            {
                return false;
            }

            if (actualMode == requestedMode)
            {
                return true;
            }

            if (requestedMode == MechWorkModeDefOf.Work)
            {
                return mechanoidMechanitorWorkEquivalentModeDefNames.Contains(actualMode.defName);
            }

            if (requestedMode == MechWorkModeDefOf.Escort)
            {
                return mechanoidMechanitorEscortEquivalentModeDefNames.Contains(actualMode.defName);
            }

            return false;
        }

        public static bool IsMechanoidMechanitorWorkMode(HediffDef def)
        {
            return def != null
                && mechanoidMechanitorWorkModeHediffDefNames.Contains(def.defName);
        }

        public static void SyncMechanitorWithPrimaryControlGroup(MechanitorControlGroup? controlGroup)
        {
            MechanitorControlGroup? group = controlGroup;
            if (group == null || !IsMechanoidMechanitorControlGroup(group) || group.Index != 1)
            {
                return;
            }

            Pawn? mechanitor = group.Tracker?.Pawn;
            if (mechanitor == null)
            {
                return;
            }

            MechWorkModeDef? workMode = group.WorkMode;
            if (workMode == null)
            {
                return;
            }

            ApplyWorkModeHediff(mechanitor, workMode);
        }
    }
}
