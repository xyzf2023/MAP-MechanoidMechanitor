using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Diagnostics
{
    internal static class JusticeOverseerDiagnosticUtility
    {
        internal const string LogPrefix = "[MAP Justice Overseer Diagnostic]";

        internal static bool ShouldDiagnosePawn(Pawn? pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            if (CompJusticeSelfWorkMode.GetFor(pawn) != null)
            {
                return true;
            }

            return MAPMechanitorNodeUtility.HasNode(pawn)
                && !MAPMechanitorNodeUtility.RequiresExternalOverseer(pawn);
        }

        internal static bool IsJusticePawn(Pawn? pawn)
        {
            return pawn?.def?.defName == "MAP_Mech_Justice";
        }

        internal static string DescribePawn(Pawn? pawn)
        {
            if (pawn == null)
            {
                return "null";
            }

            string label = pawn.Name?.ToStringShort ?? pawn.LabelShortCap;
            return $"{pawn.def?.defName ?? "?"} id={pawn.thingIDNumber} label={label} faction={pawn.Faction?.Name ?? "null"}";
        }

        internal static void AppendPawnNodeInfo(StringBuilder sb, string prefix, Pawn? pawn)
        {
            sb.AppendLine($"{prefix}: {DescribePawn(pawn)}");
            sb.AppendLine($"{prefix} hasCompJusticeSelfWorkMode={(CompJusticeSelfWorkMode.GetFor(pawn) != null)}");
            sb.AppendLine($"{prefix} hasNode={MAPMechanitorNodeUtility.HasNode(pawn)}");
            sb.AppendLine($"{prefix} requiresExternalOverseer={MAPMechanitorNodeUtility.RequiresExternalOverseer(pawn)}");
        }

        internal static void LogIfDevMode(string message)
        {
            if (Prefs.DevMode)
            {
                Log.Message($"{LogPrefix} {message}");
            }
        }
    }

    [HarmonyPatch(typeof(PawnColumnWorker_Overseer), nameof(PawnColumnWorker_Overseer.DoCell))]
    public static class Patch_PawnColumnWorker_Overseer_DoCell_JusticeDiagnostic
    {
        private static readonly HashSet<int> DiagnosedPawnIds = new HashSet<int>();

        [HarmonyPostfix]
        public static void Postfix(Pawn pawn)
        {
            if (!JusticeOverseerDiagnosticUtility.ShouldDiagnosePawn(pawn))
            {
                return;
            }

            int pawnId = pawn.thingIDNumber;
            if (pawnId <= 0 || !DiagnosedPawnIds.Add(pawnId))
            {
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Overseer column cell diagnostic");
            sb.AppendLine($"subject: {JusticeOverseerDiagnosticUtility.DescribePawn(pawn)}");
            sb.AppendLine($"isJustice={JusticeOverseerDiagnosticUtility.IsJusticePawn(pawn)}");
            sb.AppendLine($"hasCompJusticeSelfWorkMode={(CompJusticeSelfWorkMode.GetFor(pawn) != null)}");
            sb.AppendLine($"hasNode={MAPMechanitorNodeUtility.HasNode(pawn)}");
            sb.AppendLine($"requiresExternalOverseer={MAPMechanitorNodeUtility.RequiresExternalOverseer(pawn)}");

            Pawn? overseer = pawn.GetOverseer();
            if (overseer == null)
            {
                sb.AppendLine("GetOverseer(): null");
            }
            else
            {
                sb.AppendLine($"GetOverseer(): {JusticeOverseerDiagnosticUtility.DescribePawn(overseer)}");
                sb.AppendLine(
                    $"DirectRelationExists(Overseer, overseer)={pawn.relations.DirectRelationExists(PawnRelationDefOf.Overseer, overseer)}");
            }

            int overseerRelationCount = pawn.relations.GetDirectRelationsCount(PawnRelationDefOf.Overseer);
            sb.AppendLine($"GetDirectRelationsCount(Overseer)={overseerRelationCount}");

            List<DirectPawnRelation> directRelations = pawn.relations.DirectRelations;
            for (int i = 0; i < directRelations.Count; i++)
            {
                DirectPawnRelation relation = directRelations[i];
                if (relation.def != PawnRelationDefOf.Overseer)
                {
                    continue;
                }

                Pawn? otherPawn = relation.otherPawn;
                sb.AppendLine($"  Overseer relation[{i}]: other={JusticeOverseerDiagnosticUtility.DescribePawn(otherPawn)}");
                sb.AppendLine($"    otherHasNode={MAPMechanitorNodeUtility.HasNode(otherPawn)}");
                sb.AppendLine($"    otherMechanitorNull={(otherPawn?.mechanitor == null)}");
                if (otherPawn?.mechanitor != null && otherPawn != null)
                {
                    MechanitorControlGroup? controlGroup = otherPawn.mechanitor.GetControlGroup(pawn);
                    sb.AppendLine($"    otherMechanitor.GetControlGroup(subject)={(controlGroup == null ? "null" : $"group#{controlGroup.Index}")}");
                }
            }

            CompOverseerSubject? overseerSubject = pawn.OverseerSubject;
            sb.AppendLine($"OverseerSubject={(overseerSubject == null ? "null" : overseerSubject.GetType().Name)}");
            if (overseerSubject != null)
            {
                sb.AppendLine($"OverseerSubject.State={overseerSubject.State}");
            }

            JusticeOverseerDiagnosticUtility.LogIfDevMode(sb.ToString().TrimEnd());
        }
    }

    [HarmonyPatch(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.AddDirectRelation))]
    public static class Patch_Pawn_RelationsTracker_AddDirectRelation_JusticeDiagnostic
    {
        private static readonly FieldInfo RelationsPawnField =
            AccessTools.Field(typeof(Pawn_RelationsTracker), "pawn");

        [HarmonyPrefix]
        public static void Prefix(Pawn_RelationsTracker __instance, PawnRelationDef def, Pawn otherPawn)
        {
            if (def != PawnRelationDefOf.Overseer)
            {
                return;
            }

            Pawn? owner = GetRelationsPawn(__instance);
            if (!ShouldLogRelationAttempt(owner, otherPawn))
            {
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("AddDirectRelation Overseer");
            JusticeOverseerDiagnosticUtility.AppendPawnNodeInfo(sb, "owner", owner);
            JusticeOverseerDiagnosticUtility.AppendPawnNodeInfo(sb, "otherPawn", otherPawn);

            Pawn? existingOverseer = owner?.GetOverseer();
            sb.AppendLine($"owner.GetOverseer() before={(existingOverseer == null ? "null" : JusticeOverseerDiagnosticUtility.DescribePawn(existingOverseer))}");
            if (owner != null)
            {
                sb.AppendLine($"owner.GetDirectRelationsCount(Overseer) before={owner.relations.GetDirectRelationsCount(PawnRelationDefOf.Overseer)}");
            }

            if (owner != null
                && JusticeOverseerDiagnosticUtility.IsJusticePawn(owner)
                && MAPMechanitorNodeUtility.HasNode(owner)
                && !MAPMechanitorNodeUtility.RequiresExternalOverseer(owner))
            {
                sb.AppendLine("WARNING: Attempting to add Overseer relation to overseerless MAP mechanitor node");
            }

            if (owner != null
                && CompJusticeSelfWorkMode.GetFor(owner) != null
                && !MAPMechanitorNodeUtility.RequiresExternalOverseer(owner))
            {
                sb.AppendLine("WARNING: Attempting to add Overseer relation to justice self-work-mode pawn");
            }

            sb.AppendLine("StackTrace:");
            sb.AppendLine(System.Environment.StackTrace);

            JusticeOverseerDiagnosticUtility.LogIfDevMode(sb.ToString().TrimEnd());
        }

        private static Pawn? GetRelationsPawn(Pawn_RelationsTracker relations)
        {
            if (relations == null || RelationsPawnField == null)
            {
                return null;
            }

            return RelationsPawnField.GetValue(relations) as Pawn;
        }

        private static bool ShouldLogRelationAttempt(Pawn? owner, Pawn? otherPawn)
        {
            return JusticeOverseerDiagnosticUtility.ShouldDiagnosePawn(owner)
                || JusticeOverseerDiagnosticUtility.ShouldDiagnosePawn(otherPawn)
                || MAPMechanitorNodeUtility.HasNode(owner)
                || MAPMechanitorNodeUtility.HasNode(otherPawn);
        }
    }

    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.CanControlMech))]
    public static class Patch_MechanitorUtility_CanControlMech_JusticeDiagnostic
    {
        private static readonly HashSet<string> LoggedKeys = new HashSet<string>();

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Pawn pawn, Pawn mech, ref AcceptanceReport __result)
        {
            if (!ShouldDiagnoseCanControlMech(mech))
            {
                return;
            }

            string key = $"{pawn?.thingIDNumber ?? 0}:{mech.thingIDNumber}:{__result.Accepted}:{__result.Reason ?? ""}";
            if (!LoggedKeys.Add(key))
            {
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CanControlMech diagnostic");
            sb.AppendLine($"controller: {JusticeOverseerDiagnosticUtility.DescribePawn(pawn)}");
            sb.AppendLine($"targetMech: {JusticeOverseerDiagnosticUtility.DescribePawn(mech)}");
            sb.AppendLine($"result.Accepted={__result.Accepted}");
            sb.AppendLine($"result.Reason={__result.Reason ?? "null"}");
            sb.AppendLine(
                $"IsProtectedMechanitorTarget(mech, controller)={MAPMechanitorControlProtectionUtility.IsProtectedMechanitorTarget(mech, pawn)}");

            JusticeOverseerDiagnosticUtility.LogIfDevMode(sb.ToString().TrimEnd());
        }

        private static bool ShouldDiagnoseCanControlMech(Pawn? mech)
        {
            if (mech == null)
            {
                return false;
            }

            if (CompJusticeSelfWorkMode.GetFor(mech) != null)
            {
                return true;
            }

            return MAPMechanitorNodeUtility.HasNode(mech)
                && !MAPMechanitorNodeUtility.RequiresExternalOverseer(mech);
        }
    }
}
