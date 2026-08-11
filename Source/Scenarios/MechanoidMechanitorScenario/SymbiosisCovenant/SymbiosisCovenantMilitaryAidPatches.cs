using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [HarmonyPatch(typeof(IncidentWorker_RaidEnemy), "TryExecuteWorker")]
    public static class SymbiosisCovenantMilitaryAidRaidListenerPatch
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(
            IncidentWorker_RaidEnemy __instance,
            IncidentParms parms,
            bool __result)
        {
            if (__result)
            {
                SymbiosisCovenantMilitaryAidUtility.NotifyRaidSucceeded(__instance, parms);
            }
        }
    }

    [HarmonyPatch(
        typeof(GameComponent_SymbiosisCovenantState),
        nameof(GameComponent_SymbiosisCovenantState.GameComponentTick))]
    public static class SymbiosisCovenantMilitaryAidGameComponentTickPatch
    {
        public static void Postfix(GameComponent_SymbiosisCovenantState __instance)
        {
            SymbiosisCovenantMilitaryAidUtility.Tick(__instance);
        }
    }

    [HarmonyPatch(
        typeof(GameComponent_SymbiosisCovenantState),
        nameof(GameComponent_SymbiosisCovenantState.ExposeData))]
    public static class SymbiosisCovenantMilitaryAidExposeDataPatch
    {
        public static void Postfix(GameComponent_SymbiosisCovenantState __instance)
        {
            SymbiosisCovenantMilitaryAidUtility.ExposeData(__instance);
        }
    }

    [HarmonyPatch(
        typeof(Dialog_SymbiosisCovenantDev),
        nameof(Dialog_SymbiosisCovenantDev.DoWindowContents))]
    public static class SymbiosisCovenantMilitaryAidDevDialogPatch
    {
        public static void Postfix(Rect inRect)
        {
            if (!Prefs.DevMode)
            {
                return;
            }

            Rect buttonRect = new Rect(inRect.xMax - 410f, inRect.y, 195f, 28f);
            if (!Widgets.ButtonText(
                    buttonRect,
                    "MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Button".Translate()))
            {
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption(
                    "MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.ForceOffer".Translate(),
                    () => ShowResult(
                        SymbiosisCovenantMilitaryAidUtility.DevForceOfferForCurrentThreat())),
                new FloatMenuOption(
                    "MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.ClearState".Translate(),
                    () => ShowResult(
                        SymbiosisCovenantMilitaryAidUtility.DevClearCurrentMapState())),
                new FloatMenuOption(
                    SymbiosisCovenantMilitaryAidUtility.GetDevStatus(),
                    null)
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void ShowResult(bool success)
        {
            Messages.Message(
                success
                    ? "MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Success".Translate()
                    : "MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Failed".Translate(),
                success ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput,
                historical: false);
        }
    }
}
