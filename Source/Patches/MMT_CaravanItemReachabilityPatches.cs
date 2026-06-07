using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace MMT
{
    [HarmonyPatch(typeof(Dialog_FormCaravan), "CheckForErrors")]
    public static class MMT_CaravanItemReachabilityPatches
    {
        [HarmonyPrefix]
        public static bool Prefix(Dialog_FormCaravan __instance, List<Pawn> pawns, ref bool __result)
        {
            bool reform = Traverse.Create(__instance).Field("reform").GetValue<bool>();
            if (!reform || !pawns.Any(MMT_TravelUtility.CanActAsIndependentCaravanOwner))
            {
                return true;
            }

            __result = CheckForErrorsWithMmtCollector(__instance, pawns, reform);
            return false;
        }

        private static bool CheckForErrorsWithMmtCollector(Dialog_FormCaravan dialog, List<Pawn> pawns, bool reform)
        {
            Traverse dialogTraverse = Traverse.Create(dialog);
            if (dialogTraverse.Property("MustChooseRoute").GetValue<bool>()
                && !dialogTraverse.Field("destinationTile").GetValue<PlanetTile>().Valid)
            {
                Messages.Message("MessageMustChooseRouteFirst".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }

            PlanetTile startingTile = dialogTraverse.Field("startingTile").GetValue<PlanetTile>();
            if (!reform && !startingTile.Valid)
            {
                Messages.Message("MessageNoValidExitTile".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }

            if (!pawns.Any(x => CaravanUtility.IsOwner(x, Faction.OfPlayer) && !x.Downed))
            {
                if (ModsConfig.IdeologyActive)
                {
                    Messages.Message(
                        "CaravanMustHaveAtLeastOneNonSlaveColonist".Translate(),
                        MessageTypeDefOf.RejectInput,
                        historical: false);
                }
                else
                {
                    Messages.Message(
                        "CaravanMustHaveAtLeastOneColonist".Translate(),
                        MessageTypeDefOf.RejectInput,
                        historical: false);
                }

                return false;
            }

            if (!reform && dialog.MassUsage > dialog.MassCapacity)
            {
                Messages.Message("TooBigCaravanMassUsage".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }

            List<TransferableOneWay> transferables = dialog.transferables;
            for (int num = 0; num < transferables.Count; num++)
            {
                if (transferables[num].ThingDef.category != ThingCategory.Item)
                {
                    continue;
                }

                int countToTransfer = transferables[num].CountToTransfer;
                int reachableStackCount = 0;
                if (countToTransfer <= 0)
                {
                    continue;
                }

                for (int num3 = 0; num3 < transferables[num].things.Count; num3++)
                {
                    Thing t = transferables[num].things[num3];
                    if (!t.Spawned
                        || pawns.Any(x =>
                            MMT_TravelUtility.CanActAsCaravanCollector(x)
                            && x.CanReach(t, PathEndMode.Touch, Danger.Deadly)))
                    {
                        reachableStackCount += t.stackCount;
                        if (reachableStackCount >= countToTransfer)
                        {
                            break;
                        }
                    }
                }

                if (reachableStackCount < countToTransfer)
                {
                    if (countToTransfer == 1)
                    {
                        Messages.Message(
                            "CaravanItemIsUnreachableSingle".Translate(transferables[num].ThingDef.label),
                            MessageTypeDefOf.RejectInput,
                            historical: false);
                    }
                    else
                    {
                        Messages.Message(
                            "CaravanItemIsUnreachableMulti".Translate(countToTransfer, transferables[num].ThingDef.label),
                            MessageTypeDefOf.RejectInput,
                            historical: false);
                    }

                    return false;
                }
            }

            return true;
        }
    }
}
