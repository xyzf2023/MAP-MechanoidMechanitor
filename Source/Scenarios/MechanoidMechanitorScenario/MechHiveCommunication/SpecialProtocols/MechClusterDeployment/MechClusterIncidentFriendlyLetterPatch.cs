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
            LookTargets lookTargets,
            ref TaggedString baseLetterLabel,
            ref TaggedString baseLetterText,
            ref LetterDef baseLetterDef)
        {
            if (__instance is not IncidentWorker_MechCluster
                || !MechanoidMechanitorMechHiveCommunicationUtility
                    .TryGetContactableMechHive(out Faction mechHive))
            {
                return;
            }

            Faction? player = Faction.OfPlayerSilentFail;
            if (player == null || lookTargets?.targets == null || lookTargets.targets.Count == 0)
            {
                return;
            }

            // 原版随机集群同样归属机械巢，可以保留盟友提示；但第三方替换的敌方、
            // 无阵营或无法确认归属的目标不能仅凭全局外交状态被标为友方。
            foreach (GlobalTargetInfo target in lookTargets.targets)
            {
                Thing? thing = target.Thing;
                if (thing == null || thing.Destroyed || thing.Faction != mechHive
                    || thing.HostileTo(player))
                {
                    return;
                }
            }

            baseLetterLabel =
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.Cluster.Letter.Label"
                    .Translate();
            baseLetterText =
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.SpecialProtocols.Cluster.Letter.Text"
                    .Translate();
            baseLetterDef = LetterDefOf.PositiveEvent;
        }
    }
}
