using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [HarmonyPatch]
    public static class MechClusterIncidentFriendlyLetterPatch
    {
        private static MethodBase? TargetMethod()
        {
            return AccessTools.Method(
                typeof(IncidentWorker),
                "SendStandardLetter",
                new[]
                {
                    typeof(TaggedString),
                    typeof(TaggedString),
                    typeof(LetterDef),
                    typeof(IncidentParms),
                    typeof(LookTargets),
                    typeof(NamedArgument[])
                });
        }

        private static void Prefix(
            IncidentWorker __instance,
            ref TaggedString baseLetterLabel,
            ref TaggedString baseLetterText,
            ref LetterDef baseLetterDef)
        {
            if (__instance is not IncidentWorker_MechCluster
                || !MechanoidMechanitorMechHiveCommunicationUtility
                    .TryGetContactableMechHive(out _))
            {
                return;
            }

            baseLetterLabel =
                "MAP_MechanoidMechanitor.MechHiveCommunication.SpecialProtocols.Cluster.LetterLabel"
                    .Translate();
            baseLetterText =
                "MAP_MechanoidMechanitor.MechHiveCommunication.SpecialProtocols.Cluster.LetterText"
                    .Translate();
            baseLetterDef = LetterDefOf.PositiveEvent;
        }
    }
}
