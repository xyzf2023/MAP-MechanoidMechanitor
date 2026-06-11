using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MMT
{
    public struct MMT_TrySendPatchState
    {
        public List<Pawn> StoryAdded;
        public List<Pawn> SkillsAdded;
    }

    [HarmonyPatch(typeof(CaravanUtility), nameof(CaravanUtility.IsOwner))]
    public static class MMT_CaravanUtilityIsOwnerPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, Faction caravanFaction, ref bool __result)
        {
            if (__result || pawn == null || caravanFaction == null)
            {
                return;
            }

            if (pawn.Faction != caravanFaction)
            {
                return;
            }

            if (!MMT_TravelUtility.CanActAsIndependentCaravanOwner(pawn))
            {
                return;
            }

            __result = true;
        }
    }

    [HarmonyPatch(typeof(FormCaravanComp), nameof(FormCaravanComp.CanFormOrReformCaravanNow), MethodType.Getter)]
    public static class MMT_FormCaravanCompCanFormOrReformCaravanNowPatch
    {
        [HarmonyPostfix]
        public static void Postfix(FormCaravanComp __instance, ref bool __result)
        {
            if (__result)
            {
                return;
            }

            if (__instance.parent is not MapParent mapParent || !mapParent.HasMap || !__instance.Reform)
            {
                return;
            }

            if (__instance.AnyActiveThreatNow)
            {
                return;
            }

            if (MMT_TravelUtility.MapHasIndependentCaravanOwner(mapParent.Map))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(FormCaravanComp), nameof(FormCaravanComp.CanReformNow))]
    public static class MMT_FormCaravanCompCanReformNowPatch
    {
        [HarmonyPostfix]
        public static void Postfix(FormCaravanComp __instance, ref bool __result)
        {
            if (__result)
            {
                return;
            }

            if (__instance.parent is not MapParent mapParent || !mapParent.HasMap || !__instance.Reform)
            {
                return;
            }

            if (!__instance.CanFormOrReformCaravanNow)
            {
                return;
            }

            if (MMT_TravelUtility.MapHasIndependentCaravanOwner(mapParent.Map))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(FormCaravanComp), nameof(FormCaravanComp.GetGizmos))]
    public static class MMT_FormCaravanCompGetGizmosPatch
    {
        [HarmonyPostfix]
        public static void Postfix(FormCaravanComp __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance.parent is not MapParent mapParent || !mapParent.HasMap)
            {
                return;
            }

            if (!__instance.Reform || !__instance.CanFormOrReformCaravanNow)
            {
                return;
            }

            List<Gizmo> gizmos = __result?.ToList() ?? new List<Gizmo>();
            if (gizmos.Any(g => g is Command_Action command && command.tutorTag == "ReformCaravan"))
            {
                return;
            }

            if (!MMT_TravelUtility.MapHasIndependentCaravanOwner(mapParent.Map))
            {
                return;
            }

            Command_Action commandAction = new Command_Action
            {
                defaultLabel = "CommandReformCaravan".Translate(),
                defaultDesc = "CommandReformCaravanDesc".Translate(),
                icon = FormCaravanComp.FormCaravanCommand,
                hotKey = KeyBindingDefOf.Misc2,
                tutorTag = "ReformCaravan",
                action = delegate
                {
                    if (ModsConfig.OdysseyActive && mapParent.Map.listerThings.ThingsOfDef(ThingDefOf.GravEngine).Any())
                    {
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("ConfirmLoseGravship".Translate(), OpenReformDialog));
                    }
                    else if (ModsConfig.OdysseyActive
                        && mapParent.Map.listerThings.ThingsInGroup(ThingRequestGroup.PassengerShuttle).Any())
                    {
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("ConfirmLoseShuttle".Translate(), OpenReformDialog));
                    }
                    else
                    {
                        OpenReformDialog();
                    }
                }
            };

            if (GenHostility.AnyHostileActiveThreatToPlayer(mapParent.Map, countDormantPawnsAsHostile: true))
            {
                commandAction.Disable("CommandReformCaravanFailHostilePawns".Translate());
            }

            gizmos.Add(commandAction);
            __result = gizmos;

            void OpenReformDialog()
            {
                Find.WindowStack.Add(new Dialog_FormCaravan(mapParent.Map, reform: true));
            }
        }
    }

    [HarmonyPatch(typeof(Dialog_FormCaravan), "TrySend")]
    public static class MMT_DialogFormCaravanTrySendPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Dialog_FormCaravan __instance, ref MMT_TrySendPatchState __state)
        {
            __state = new MMT_TrySendPatchState
            {
                StoryAdded = new List<Pawn>(),
                SkillsAdded = new List<Pawn>()
            };

            if (__instance.transferables == null)
            {
                return;
            }

            List<Pawn> pawns = TransferableUtility.GetPawnsFromTransferables(__instance.transferables);
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (!MMT_TravelUtility.CanActAsIndependentCaravanOwner(pawn))
                {
                    continue;
                }

                if (pawn.story == null)
                {
                    pawn.story = new Pawn_StoryTracker(pawn);
                    __state.StoryAdded.Add(pawn);
                }

                if (pawn.skills == null)
                {
                    pawn.skills = new Pawn_SkillTracker(pawn);
                    __state.SkillsAdded.Add(pawn);
                }
            }
        }

        [HarmonyPostfix]
        public static void Postfix(MMT_TrySendPatchState __state)
        {
            for (int i = 0; i < __state.SkillsAdded.Count; i++)
            {
                __state.SkillsAdded[i].skills = null;
            }

            for (int i = 0; i < __state.StoryAdded.Count; i++)
            {
                __state.StoryAdded[i].story = null;
            }
        }
    }
}
