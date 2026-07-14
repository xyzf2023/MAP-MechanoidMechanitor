using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class MAPTemporaryPawnTrackerState
    {
        public Pawn? Pawn;
        public Pawn_StoryTracker? OriginalStory;
        public Pawn_StoryTracker? TemporaryStory;
        public Pawn_SkillTracker? OriginalSkills;
        public Pawn_SkillTracker? TemporarySkills;
    }

    public sealed class MAPTrySendPatchState
    {
        public List<MAPTemporaryPawnTrackerState> Entries { get; } =
            new List<MAPTemporaryPawnTrackerState>();
    }

    [HarmonyPatch(typeof(CaravanUtility), nameof(CaravanUtility.IsOwner))]
    public static class MAPCaravanUtilityIsOwnerPatch
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

            if (!MAPTravelUtility.CanActAsIndependentCaravanOwner(pawn))
            {
                return;
            }

            __result = true;
        }
    }

    [HarmonyPatch(typeof(FormCaravanComp), nameof(FormCaravanComp.CanFormOrReformCaravanNow), MethodType.Getter)]
    public static class MAPFormCaravanCompCanFormOrReformCaravanNowPatch
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

            if (MAPTravelUtility.MapHasIndependentCaravanOwner(mapParent.Map))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(FormCaravanComp), nameof(FormCaravanComp.CanReformNow))]
    public static class MAPFormCaravanCompCanReformNowPatch
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

            if (MAPTravelUtility.MapHasIndependentCaravanOwner(mapParent.Map))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(FormCaravanComp), nameof(FormCaravanComp.GetGizmos))]
    public static class MAPFormCaravanCompGetGizmosPatch
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

            if (!MAPTravelUtility.MapHasIndependentCaravanOwner(mapParent.Map))
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
    public static class MAPDialogFormCaravanTrySendPatch
    {
        private const int RestoreTemporaryTrackerFailureLogKeyBase = 0x4D415054; // "MAPT"

        [HarmonyPrefix]
        public static void Prefix(Dialog_FormCaravan __instance, out MAPTrySendPatchState __state)
        {
            __state = new MAPTrySendPatchState();

            if (__instance.transferables == null)
            {
                return;
            }

            List<Pawn> pawns = TransferableUtility.GetPawnsFromTransferables(__instance.transferables);
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (!MAPTravelUtility.CanActAsIndependentCaravanOwner(pawn))
                {
                    continue;
                }

                Pawn_StoryTracker? originalStory = pawn.story;
                Pawn_SkillTracker? originalSkills = pawn.skills;
                Pawn_StoryTracker? temporaryStory = null;
                Pawn_SkillTracker? temporarySkills = null;

                if (originalStory == null)
                {
                    temporaryStory = new Pawn_StoryTracker(pawn);
                }

                if (originalSkills == null)
                {
                    temporarySkills = new Pawn_SkillTracker(pawn);
                }

                if (temporaryStory == null && temporarySkills == null)
                {
                    continue;
                }

                // 先记录，后赋值：确保中途异常时 Finalizer 仍能恢复已安装的临时 Tracker。
                __state.Entries.Add(new MAPTemporaryPawnTrackerState
                {
                    Pawn = pawn,
                    OriginalStory = originalStory,
                    TemporaryStory = temporaryStory,
                    OriginalSkills = originalSkills,
                    TemporarySkills = temporarySkills
                });

                if (temporaryStory != null)
                {
                    pawn.story = temporaryStory;
                }

                if (temporarySkills != null)
                {
                    pawn.skills = temporarySkills;
                }
            }
        }

        [HarmonyPostfix]
        public static void Postfix(MAPTrySendPatchState? __state)
        {
            RestoreTemporaryTrackers(__state);
        }

        [HarmonyFinalizer]
        public static Exception? Finalizer(Exception? __exception, MAPTrySendPatchState? __state)
        {
            RestoreTemporaryTrackers(__state);
            return __exception;
        }

        private static void RestoreTemporaryTrackers(MAPTrySendPatchState? state)
        {
            if (state?.Entries == null || state.Entries.Count == 0)
            {
                return;
            }

            for (int i = 0; i < state.Entries.Count; i++)
            {
                MAPTemporaryPawnTrackerState? entry = state.Entries[i];
                if (entry?.Pawn == null)
                {
                    continue;
                }

                try
                {
                    RestoreTemporaryStory(entry);
                    RestoreTemporarySkills(entry);
                }
                catch (Exception ex)
                {
                    Pawn pawn = entry.Pawn;
                    Log.ErrorOnce(
                        "[MAP-机械族机械师] 恢复临时远行队 Tracker 失败：" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{ex}",
                        unchecked(RestoreTemporaryTrackerFailureLogKeyBase + pawn.thingIDNumber));
                }
            }
        }

        private static void RestoreTemporaryStory(MAPTemporaryPawnTrackerState entry)
        {
            Pawn pawn = entry.Pawn!;
            if (entry.TemporaryStory == null
                || !ReferenceEquals(pawn.story, entry.TemporaryStory))
            {
                return;
            }

            pawn.story = entry.OriginalStory;
        }

        private static void RestoreTemporarySkills(MAPTemporaryPawnTrackerState entry)
        {
            Pawn pawn = entry.Pawn!;
            if (entry.TemporarySkills == null
                || !ReferenceEquals(pawn.skills, entry.TemporarySkills))
            {
                return;
            }

            pawn.skills = entry.OriginalSkills;
        }
    }
}
