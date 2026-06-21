using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class WorkModeUtility
    {
        private static readonly HashSet<int> mobileCombatPawnIds = new HashSet<int>();
        private static readonly HashSet<string> justiceWorkModeDefNames = new HashSet<string>
        {
            "MAP_WorkMode_EfficientExecution",
            "MAP_WorkMode_MobileCombat",
            "MAP_WorkMode_MobileCombat_Guard",
            "MAP_WorkMode_FortifiedDefense"
        };
        private static readonly HashSet<string> justiceSelfOnlyWorkModeDefNames = new HashSet<string>
        {
            "MAP_WorkMode_AutonomousDirective"
        };
        private static readonly HashSet<string> justiceWorkEquivalentModeDefNames = new HashSet<string>
        {
            "MAP_WorkMode_EfficientExecution",
            "MAP_WorkMode_MobileCombat",
            "MAP_WorkMode_FortifiedDefense"
        };
        private static readonly HashSet<string> justiceEscortEquivalentModeDefNames = new HashSet<string>
        {
            "MAP_WorkMode_MobileCombat_Guard"
        };
        private static readonly HashSet<string> justiceWorkModeHediffDefNames = new HashSet<string>
        {
            "MAP_Justice_WorkMode_EfficientExecution",
            "MAP_Justice_WorkMode_MobileCombat",
            "MAP_Justice_WorkMode_FortifiedDefense"
        };
        private static HediffDef? efficientExecutionDef;
        private static HediffDef? mobileCombatDef;
        private static HediffDef? fortifiedDefenseDef;

        private static HediffDef? GetEfficientExecutionDef()
        {
            return efficientExecutionDef ??= DefDatabase<HediffDef>.GetNamedSilentFail("MAP_Justice_WorkMode_EfficientExecution");
        }

        private static HediffDef? GetMobileCombatDef()
        {
            return mobileCombatDef ??= DefDatabase<HediffDef>.GetNamedSilentFail("MAP_Justice_WorkMode_MobileCombat");
        }

        private static HediffDef? GetFortifiedDefenseDef()
        {
            return fortifiedDefenseDef ??= DefDatabase<HediffDef>.GetNamedSilentFail("MAP_Justice_WorkMode_FortifiedDefense");
        }

        public static void SetMobileCombatFlag(Pawn pawn, bool active)
        {
            if (pawn == null)
            {
                return;
            }

            int id = pawn.thingIDNumber;
            if (id <= 0)
            {
                return;
            }

            if (active)
            {
                mobileCombatPawnIds.Add(id);
            }
            else
            {
                mobileCombatPawnIds.Remove(id);
            }
        }

        public static bool HasMobileCombatFlag(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            return mobileCombatPawnIds.Contains(pawn.thingIDNumber);
        }

        public static bool IsMobileCombat(Pawn pawn)
        {
            return HasMobileCombatFlag(pawn);
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

        public static bool IsJusticeControlGroup(MechanitorControlGroup controlGroup)
        {
            Pawn? mechanitor = controlGroup?.Tracker?.Pawn;
            return mechanitor != null && MAPMechanitorNodeUtility.IsMechanitorNodeController(mechanitor);
        }

        public static bool IsJusticeWorkMode(MechWorkModeDef workMode)
        {
            return workMode != null && justiceWorkModeDefNames.Contains(workMode.defName);
        }

        public static bool IsJusticeSelfOnlyWorkMode(MechWorkModeDef workMode)
        {
            return workMode != null && justiceSelfOnlyWorkModeDefNames.Contains(workMode.defName);
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
                return justiceWorkEquivalentModeDefNames.Contains(actualMode.defName);
            }

            if (requestedMode == MechWorkModeDefOf.Escort)
            {
                return justiceEscortEquivalentModeDefNames.Contains(actualMode.defName);
            }

            return false;
        }

        public static bool IsJusticeWorkMode(HediffDef def)
        {
            return def != null && justiceWorkModeHediffDefNames.Contains(def.defName);
        }

        public static void SyncJusticeWithGroup1(MechanitorControlGroup? controlGroup)
        {
            MechanitorControlGroup? group = controlGroup;
            if (group == null || !IsJusticeControlGroup(group) || group.Index != 1)
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
