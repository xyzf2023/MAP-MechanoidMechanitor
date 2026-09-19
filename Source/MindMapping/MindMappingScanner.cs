using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
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

            if (IsInitialized(scanner) || scanner.Occupant != null
                || scanner.TryGetInnerInteractableThingOwner()?.Any == true)
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

            if (mindMappingMode == value)
            {
                return true;
            }

            ThingOwner? owner = scanner.TryGetInnerInteractableThingOwner();
            if (IsInitialized(scanner) || owner?.Any == true)
            {
                // 与按钮门控一致：先通过原版取消流程退还配料，不能在模式切换中销毁内容。
                return false;
            }

            // 空闲扫描仪也可能保留尚未入舱的人选，切换配方后必须取消旧选择。
            FabricationTicksField.SetValue(scanner, 0);
            SelectedPawnField.SetValue(scanner, null);
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
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            List<CodeInstruction> codes = instructions.ToList();
            MethodInfo eject = AccessTools.Method(typeof(Building_SubcoreScanner),
                nameof(Building_SubcoreScanner.EjectContents));
            MethodInfo make = AccessTools.Method(typeof(ThingMaker), nameof(ThingMaker.MakeThing),
                new[] { typeof(ThingDef), typeof(ThingDef) });
            MethodInfo capture = AccessTools.Method(typeof(MindMappingScanner_Completion_Patch),
                nameof(CaptureBeforeEjection));
            MethodInfo makeOutput = AccessTools.Method(typeof(MindMappingScanner_Completion_Patch),
                nameof(MakeOutput));
            if (eject == null || make == null || capture == null || makeOutput == null)
            {
                Log.Error("[MAP-机械族机械师] 心智映射完成补丁缺少目标方法，保持原版流程。");
                return codes;
            }

            List<int> ejects = new List<int>();
            List<int> outputs = new List<int>();
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].Calls(eject)) ejects.Add(i);
                if (codes[i].Calls(make)) outputs.Add(i);
            }
            if (ejects.Count != 1 || outputs.Count != 1 || ejects[0] >= outputs[0]
                || codes.Skip(ejects[0]).Take(outputs[0] - ejects[0] + 1)
                    .Any(code => code.blocks.Count != 0
                        || code.opcode.FlowControl == FlowControl.Branch
                        || code.opcode.FlowControl == FlowControl.Cond_Branch)
                || codes.Skip(ejects[0] + 1).Take(outputs[0] - ejects[0])
                    .Any(code => code.labels.Count != 0))
            {
                Log.Error("[MAP-机械族机械师] 心智映射完成分支匹配失败，保持原版流程。");
                return codes;
            }

            // 局部变量仅属于本次 Tick；在原版确实完成扫描、毁脑之前捕获一次。
            LocalBuilder data = generator.DeclareLocal(typeof(MindMappingData));
            List<CodeInstruction> result = new List<CodeInstruction>();
            for (int i = 0; i < codes.Count; i++)
            {
                CodeInstruction code = new CodeInstruction(codes[i]);
                if (i == ejects[0])
                {
                    CodeInstruction duplicate = new CodeInstruction(OpCodes.Dup);
                    duplicate.labels.AddRange(code.labels);
                    code.labels.Clear();
                    result.Add(duplicate);
                    result.Add(new CodeInstruction(OpCodes.Call, capture));
                    result.Add(new CodeInstruction(OpCodes.Stloc, data));
                }
                if (i == outputs[0])
                {
                    result.Add(new CodeInstruction(OpCodes.Ldloc, data));
                    code.opcode = OpCodes.Call;
                    code.operand = makeOutput;
                }
                result.Add(code);
            }
            return result;
        }

        private static MindMappingData? CaptureBeforeEjection(Building_SubcoreScanner scanner)
        {
            return MindMappingScannerUtility.IsMappingMode(scanner) && scanner.Occupant != null
                ? MindMappingData.Capture(scanner.Occupant)
                : null;
        }

        private static Thing MakeOutput(ThingDef def, ThingDef stuff, MindMappingData? data)
        {
            if (data == null)
            {
                return ThingMaker.MakeThing(def, stuff);
            }

            Thing core = ThingMaker.MakeThing(
                MAPMechanitor_ThingDefOf.MAP_MindMappingAutonomousDirectiveCore);
            core.TryGetComp<CompMindMappingAutonomousDirectiveCore>()?.Store(data);
            // 放置、消息、音效、扫描仪复位继续由原版完成分支执行。
            return core;
        }
    }
}
