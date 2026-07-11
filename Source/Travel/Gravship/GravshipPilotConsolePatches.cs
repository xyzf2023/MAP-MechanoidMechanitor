using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(CompPilotConsole), "ValidateNavigator")]
    public static class GravshipPilotConsole_ValidateNavigator_Patch
    {
        public static void Postfix(
            CompPilotConsole __instance,
            LocalTargetInfo target,
            ref AcceptanceReport __result)
        {
            if (__result.Accepted)
            {
                return;
            }

            if (target.Thing is not Pawn pawn)
            {
                return;
            }

            if (!CompGravshipPilotUser.PawnCanUseGravshipPilotConsole(pawn))
            {
                return;
            }

            if (pawn.Downed
                || pawn.health?.capacities?.CapableOf(PawnCapacityDefOf.Moving) != true)
            {
                __result = "Incapable".Translate();
                return;
            }

            if (pawn.skills == null
                || pawn.skills.GetSkill(SkillDefOf.Intellectual).TotallyDisabled)
            {
                __result = "IncapableOfCapacity".Translate(SkillDefOf.Intellectual.label).CapitalizeFirst();
                return;
            }

            if (!pawn.CanReach(__instance.parent, PathEndMode.InteractionCell, Danger.Deadly))
            {
                __result = "NoPath".Translate();
                return;
            }

            __result = AcceptanceReport.WasAccepted;
        }
    }

    [HarmonyPatch(typeof(JobDriver_PilotConsole), "MakeNewToils")]
    public static class GravshipPilotConsole_JobDriverMakeNewToils_Patch
    {
        public static bool Prefix(
            JobDriver_PilotConsole __instance,
            ref IEnumerable<Toil> __result)
        {
            if (!CompGravshipPilotUser.PawnCanUseGravshipPilotConsole(__instance.pawn))
            {
                return true;
            }

            __result = MakeReplacementToils(__instance);
            return false;
        }

        private static IEnumerable<Toil> MakeReplacementToils(JobDriver_PilotConsole driver)
        {
            const TargetIndex ConsoleInd = TargetIndex.A;

            driver.FailOnDespawnedNullOrForbidden(ConsoleInd);
            yield return Toils_Goto.GotoThing(ConsoleInd, PathEndMode.InteractionCell);

            Toil toil = ToilMaker.MakeToil("MAP_GravshipPilotConsole");
            toil.initAction = delegate
            {
                Thing thing = driver.job.GetTarget(ConsoleInd).Thing;
                if (thing == null)
                {
                    return;
                }

                Precept_Ritual? ritual = TryGetPlayerGravshipLaunchRitual();
                if (ritual == null)
                {
                    Messages.Message(
                        "MAP_MechanoidMechanitor.GravshipPilot.NoGravshipLaunchRitual".Translate(),
                        thing,
                        MessageTypeDefOf.RejectInput,
                        historical: false);
                    return;
                }

                Window window = ritual.GetRitualBeginWindow(
                    thing,
                    null,
                    null,
                    null,
                    null,
                    null);

                if (window != null)
                {
                    Find.WindowStack.Add(window);
                }
            };

            yield return toil;
        }

        private static Precept_Ritual? TryGetPlayerGravshipLaunchRitual()
        {
            Ideo? ideo = Faction.OfPlayer?.ideos?.PrimaryIdeo;
            if (ideo == null)
            {
                return null;
            }

            return ideo.GetPrecept(PreceptDefOf.GravshipLaunch) as Precept_Ritual;
        }
    }
}
