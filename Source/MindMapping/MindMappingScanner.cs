using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_MindMappingScanner : CompProperties
    {
        public CompProperties_MindMappingScanner()
            : base(typeof(CompMindMappingScanner))
        {
        }
    }

    public sealed class CompMindMappingScanner : ThingComp
    {
        private static readonly FieldInfo InitScannerField = AccessTools.Field(
            typeof(Building_SubcoreScanner),
            "initScanner");

        private static readonly FieldInfo FabricationTicksField = AccessTools.Field(
            typeof(Building_SubcoreScanner),
            "fabricationTicksLeft");

        private static readonly FieldInfo SelectedPawnField = AccessTools.Field(
            typeof(Building_Enterable),
            "selectedPawn");

        private bool mindMappingMode;

        public bool MindMappingMode => mindMappingMode;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!(parent is Building_SubcoreScanner scanner)
                || !MindMappingScannerUtility.IsRipscanner(scanner)
                || !ResearchFeatureUnlockUtility.IsMindMappingUnlocked())
            {
                yield break;
            }

            Command_Toggle command = new Command_Toggle
            {
                defaultLabel = "MAP_MindMapping.Scanner.Toggle".Translate(),
                defaultDesc = "MAP_MindMapping.Scanner.ToggleDesc".Translate(),
                icon = scanner.InitScannerIcon.Texture,
                isActive = () => mindMappingMode,
                toggleAction = () => TrySetMode(!mindMappingMode),
                activateSound = SoundDefOf.Tick_Tiny
            };

            if (IsInitialized(scanner) || scanner.Occupant != null)
            {
                command.Disable("MAP_MindMapping.Scanner.ToggleDisabled".Translate());
            }

            yield return command;
        }

        public bool TrySetMode(bool value)
        {
            if (!(parent is Building_SubcoreScanner scanner) || scanner.Occupant != null)
            {
                return false;
            }

            ThingOwner? owner = scanner.TryGetInnerInteractableThingOwner();
            if (IsInitialized(scanner) || owner?.Any == true)
            {
                owner?.ClearAndDestroyContents();
                InitScannerField.SetValue(scanner, false);
                FabricationTicksField.SetValue(scanner, 0);
                SelectedPawnField.SetValue(scanner, null);
            }

            mindMappingMode = value;
            return true;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref mindMappingMode, "mindMappingMode", false);
        }

        private static bool IsInitialized(Building_SubcoreScanner scanner)
            => (bool)(InitScannerField.GetValue(scanner) ?? false);
    }

    internal static class MindMappingScannerUtility
    {
        public static bool IsRipscanner(Building_SubcoreScanner? scanner)
            => scanner?.def?.defName == "SubcoreRipscanner";

        public static bool IsMappingMode(Building_SubcoreScanner? scanner)
        {
            return IsRipscanner(scanner)
                && scanner!.TryGetComp<CompMindMappingScanner>()?.MindMappingMode == true;
        }

        public static int BlankCoreCount(Building_SubcoreScanner scanner)
        {
            ThingOwner? owner = scanner.TryGetInnerInteractableThingOwner();
            if (owner == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < owner.Count; i++)
            {
                Thing thing = owner[i];
                if (thing.def == MAPMechanitor_ThingDefOf.MAP_MindMappingAutonomousDirectiveCore
                    && thing.TryGetComp<CompMindMappingAutonomousDirectiveCore>()?.IsBlank == true)
                {
                    count += thing.stackCount;
                }
            }

            return count;
        }
    }

    [HarmonyPatch(typeof(Building_SubcoreScanner), "get_AllRequiredIngredientsLoaded")]
    internal static class MindMappingScanner_AllIngredients_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(Building_SubcoreScanner __instance, ref bool __result)
        {
            if (!MindMappingScannerUtility.IsMappingMode(__instance))
            {
                return true;
            }

            __result = MindMappingScannerUtility.BlankCoreCount(__instance) >= 1;
            return false;
        }
    }

    [HarmonyPatch(typeof(Building_SubcoreScanner), nameof(Building_SubcoreScanner.GetRequiredCountOf))]
    internal static class MindMappingScanner_RequiredCount_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            Building_SubcoreScanner __instance,
            ThingDef thingDef,
            ref int __result)
        {
            if (!MindMappingScannerUtility.IsMappingMode(__instance))
            {
                return true;
            }

            __result = thingDef == MAPMechanitor_ThingDefOf.MAP_MindMappingAutonomousDirectiveCore
                ? 1 - MindMappingScannerUtility.BlankCoreCount(__instance)
                : 0;
            return false;
        }
    }

    [HarmonyPatch(typeof(Building_SubcoreScanner), nameof(Building_SubcoreScanner.CanAcceptIngredient))]
    internal static class MindMappingScanner_AcceptIngredient_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            Building_SubcoreScanner __instance,
            Thing thing,
            ref bool __result)
        {
            if (!MindMappingScannerUtility.IsMappingMode(__instance))
            {
                return true;
            }

            __result = thing?.def == MAPMechanitor_ThingDefOf.MAP_MindMappingAutonomousDirectiveCore
                && thing.TryGetComp<CompMindMappingAutonomousDirectiveCore>()?.IsBlank == true
                && MindMappingScannerUtility.BlankCoreCount(__instance) < 1;
            return false;
        }
    }

    [HarmonyPatch(typeof(Building_SubcoreScanner), nameof(Building_SubcoreScanner.CanAcceptPawn))]
    internal static class MindMappingScanner_AcceptPawn_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            Building_SubcoreScanner __instance,
            Pawn selPawn,
            ref AcceptanceReport __result)
        {
            if (!MindMappingScannerUtility.IsMappingMode(__instance)
                || __instance.State != SubcoreScannerState.WaitingForIngredients)
            {
                return true;
            }

            if (!selPawn.IsColonist && !selPawn.IsSlaveOfColony && !selPawn.IsPrisonerOfColony)
            {
                __result = false;
            }
            else if (__instance.SelectedPawn != null && __instance.SelectedPawn != selPawn)
            {
                __result = false;
            }
            else if (!__instance.PowerOn)
            {
                __result = "CannotUseNoPower".Translate();
            }
            else
            {
                __result = "MAP_MindMapping.Scanner.RequiresCore".Translate(
                    MAPMechanitor_ThingDefOf.MAP_MindMappingAutonomousDirectiveCore.LabelCap);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(Building_SubcoreScanner), "AppendIngredientsList")]
    internal static class MindMappingScanner_IngredientsList_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(Building_SubcoreScanner __instance, StringBuilder sb)
        {
            if (!MindMappingScannerUtility.IsMappingMode(__instance))
            {
                return true;
            }

            sb.AppendInNewLine(
                $" - {MAPMechanitor_ThingDefOf.MAP_MindMappingAutonomousDirectiveCore.LabelCap} " +
                $"{MindMappingScannerUtility.BlankCoreCount(__instance)} / 1");
            return false;
        }
    }

    [HarmonyPatch(typeof(Building_SubcoreScanner), nameof(Building_SubcoreScanner.GetGizmos))]
    internal static class MindMappingScanner_Gizmos_Patch
    {
        [HarmonyPostfix]
        private static IEnumerable<Gizmo> Postfix(
            IEnumerable<Gizmo> __result,
            Building_SubcoreScanner __instance)
        {
            foreach (Gizmo gizmo in __result)
            {
                if (MindMappingScannerUtility.IsMappingMode(__instance)
                    && gizmo is Command_Action command
                    && command.defaultLabel == "SubcoreScannerStart".Translate())
                {
                    StringBuilder description = new StringBuilder();
                    description.Append("SubcoreScannerProduces".Translate() + " " +
                        MAPMechanitor_ThingDefOf.MAP_MindMappingAutonomousDirectiveCore.label + ".");
                    description.Append("\n\n");
                    description.Append("DurationHours".Translate() + ": " +
                        __instance.def.building.subcoreScannerTicks.ToStringTicksToPeriod());
                    description.Append("\n\n");
                    description.Append("SubcoreScannerStartDesc".Translate(
                        __instance.def.label,
                        MAPMechanitor_ThingDefOf.MAP_MindMappingAutonomousDirectiveCore.LabelCap + " x1"));
                    command.defaultDesc = description.ToString();
                }

                yield return gizmo;
            }
        }
    }

    [HarmonyPatch(typeof(Building_SubcoreScanner), "Tick")]
    internal static class MindMappingScanner_Completion_Patch
    {
        private static readonly FieldInfo FabricationTicksField = AccessTools.Field(
            typeof(Building_SubcoreScanner),
            "fabricationTicksLeft");

        internal sealed class CompletionState
        {
            public MindMappingData? Data;
            public Dictionary<string, int> ExistingOutputCounts = new Dictionary<string, int>();
        }

        [HarmonyPrefix]
        private static void Prefix(
            Building_SubcoreScanner __instance,
            out CompletionState? __state)
        {
            __state = null;
            if (!MindMappingScannerUtility.IsMappingMode(__instance)
                || __instance.State != SubcoreScannerState.Occupied
                || (int)(FabricationTicksField.GetValue(__instance) ?? 0) > 1
                || __instance.Occupant == null
                || __instance.Map == null)
            {
                return;
            }

            CompletionState state = new CompletionState
            {
                Data = MindMappingData.Capture(__instance.Occupant)
            };
            List<Thing> existing = __instance.Map.listerThings.ThingsOfDef(
                __instance.def.building.subcoreScannerOutputDef);
            for (int i = 0; i < existing.Count; i++)
            {
                state.ExistingOutputCounts[existing[i].ThingID] = existing[i].stackCount;
            }

            __state = state;
        }

        [HarmonyPostfix]
        private static void Postfix(
            Building_SubcoreScanner __instance,
            CompletionState? __state)
        {
            if (__state?.Data == null || __instance.Map == null)
            {
                return;
            }

            IntVec3 outputCell = __instance.InteractionCell;
            List<Thing> outputs = __instance.Map.listerThings.ThingsOfDef(
                __instance.def.building.subcoreScannerOutputDef);
            Thing? vanillaOutput = outputs.FirstOrDefault(thing =>
            {
                __state.ExistingOutputCounts.TryGetValue(thing.ThingID, out int previousCount);
                return thing.stackCount > previousCount;
            });
            if (vanillaOutput != null)
            {
                outputCell = vanillaOutput.Position;
                if (vanillaOutput.stackCount > 1)
                {
                    vanillaOutput.SplitOff(1).Destroy(DestroyMode.Vanish);
                }
                else
                {
                    vanillaOutput.Destroy(DestroyMode.Vanish);
                }
            }
            else
            {
                Log.Error("[MAP-机械族机械师] 心智映射扫描完成后未找到原版产物，改为直接生成映射核心。");
            }

            Thing mappedCore = ThingMaker.MakeThing(
                MAPMechanitor_ThingDefOf.MAP_MindMappingAutonomousDirectiveCore);
            mappedCore.TryGetComp<CompMindMappingAutonomousDirectiveCore>()?.Store(__state.Data);
            GenPlace.TryPlaceThing(mappedCore, outputCell, __instance.Map, ThingPlaceMode.Near);
        }
    }
}
