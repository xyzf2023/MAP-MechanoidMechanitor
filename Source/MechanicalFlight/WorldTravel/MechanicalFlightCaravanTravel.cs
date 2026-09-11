using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [DefOf]
    public static class MechanicalFlightWorldDefOf
    {
        public static WorldObjectDef MAP_MechanicalFlyingCaravan = null!;

        static MechanicalFlightWorldDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(MechanicalFlightWorldDefOf));
        }
    }

    public sealed class WorldObject_MechanicalFlyingCaravan : TravellingTransporters
    {
        public override string GetInspectString()
        {
            string baseText = base.GetInspectString();
            string destinationLabel = destinationTile.Valid
                ? Find.WorldObjects.ObjectsAt(destinationTile).FirstOrDefault()?.Label
                    ?? destinationTile.ToString()
                : "Unknown".Translate();
            string flightText = "MAP_MechanicalFlight_CaravanInspect".Translate(
                destinationLabel, Pawns.Count());
            return baseText.NullOrEmpty() ? flightText : baseText + "\n" + flightText;
        }
    }

    public sealed class MechanicalFlyingCaravanArrivalAction : TransportersArrivalAction
    {
        private string originalCaravanName = string.Empty;
        private bool arrivalStarted;

        public override bool GeneratesMap => false;

        public MechanicalFlyingCaravanArrivalAction()
        {
        }

        public MechanicalFlyingCaravanArrivalAction(string originalCaravanName)
        {
            this.originalCaravanName = originalCaravanName ?? string.Empty;
        }

        public override FloatMenuAcceptanceReport StillValid(
            IEnumerable<IThingHolder> pods, PlanetTile destinationTile)
        {
            if (arrivalStarted || !destinationTile.Valid)
            {
                return false;
            }
            // 目标状态变化也交给自定义抵达逻辑处理，避免原版运输舱回退分支接管内容。
            return true;
        }

        public override void Arrived(
            List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            if (arrivalStarted)
            {
                return;
            }
            arrivalStarted = true;

            List<Pawn> pawns = new();
            List<Thing> looseThings = new();
            for (int i = 0; i < transporters.Count; i++)
            {
                ThingOwner container = transporters[i].innerContainer;
                List<Thing> snapshot = container.ToList();
                for (int j = 0; j < snapshot.Count; j++)
                {
                    Thing thing = snapshot[j];
                    container.Remove(thing);
                    if (thing is Pawn pawn && !pawn.Dead)
                    {
                        pawns.Add(pawn);
                    }
                    else
                    {
                        looseThings.Add(thing);
                    }
                }
            }

            if (pawns.Count == 0)
            {
                // 保留内容而不是销毁，方便在异常情况下通过存档或 DEV 手段恢复。
                RestoreToContainers(transporters, looseThings);
                Log.Error("[MAP-机械族机械师] 机械飞行远行队抵达失败：没有可用于重建远行队的存活成员。");
                return;
            }

            PlanetTile landingTile = tile;
            if (Find.World.Impassable(landingTile)
                && !GenWorldClosest.TryFindClosestPassableTile(landingTile, out landingTile))
            {
                RestoreToContainers(transporters, pawns.Cast<Thing>().Concat(looseThings));
                Log.Error("[MAP-机械族机械师] 机械飞行远行队抵达失败：无法找到可通行的降落地块。");
                return;
            }

            Caravan? caravan = null;
            try
            {
                caravan = CaravanMaker.MakeCaravan(
                    pawns, Faction.OfPlayer, landingTile,
                    addToWorldPawnsIfNotAlready: true);
                if (!originalCaravanName.NullOrEmpty())
                {
                    caravan.Name = originalCaravanName;
                }

                for (int i = 0; i < looseThings.Count; i++)
                {
                    Thing thing = looseThings[i];
                    if (!thing.Destroyed)
                    {
                        CaravanInventoryUtility.GiveThing(caravan, thing);
                    }
                }

                Messages.Message("MAP_MechanicalFlight_CaravanArrived".Translate(), caravan,
                    MessageTypeDefOf.TaskCompletion, false);
                Find.WorldObjects.WorldObjectAt<PeaceTalks>(tile)?.Notify_CaravanArrived(caravan);
            }
            catch (Exception exception)
            {
                Log.Error("[MAP-机械族机械师] 机械飞行远行队重建时发生异常：" + exception);
                if (caravan == null || caravan.Destroyed)
                {
                    RestoreToContainers(transporters,
                        pawns.Cast<Thing>().Where(thing => thing.ParentHolder == null)
                            .Concat(looseThings.Where(thing => thing.ParentHolder == null)));
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref originalCaravanName, "originalCaravanName", string.Empty);
            Scribe_Values.Look(ref arrivalStarted, "arrivalStarted", defaultValue: false);
        }

        private static void RestoreToContainers(
            List<ActiveTransporterInfo> transporters, IEnumerable<Thing> things)
        {
            ActiveTransporterInfo? first = transporters.FirstOrDefault();
            if (first == null)
            {
                return;
            }
            foreach (Thing thing in things.ToList())
            {
                if (thing != null && !thing.Destroyed && thing.ParentHolder == null)
                {
                    first.innerContainer.TryAdd(thing);
                }
            }
        }
    }

    public static class MechanicalFlightCaravanUtility
    {
        private static readonly Texture2D FlightIcon =
            ContentFinder<Texture2D>.Get("UI/Commands/MechanicalFlight", false)
            ?? CompLaunchable.LaunchCommandTex;

        public static AcceptanceReport CanLaunch(Caravan? caravan)
        {
            if (caravan == null || caravan.Destroyed || !caravan.Spawned
                || caravan.Faction != Faction.OfPlayer)
            {
                return "MAP_MechanicalFlight_CaravanUnavailable".Translate();
            }

            bool hasMechanoid = false;
            foreach (Pawn pawn in caravan.PawnsListForReading)
            {
                if (pawn.RaceProps?.IsMechanoid != true)
                {
                    continue;
                }
                hasMechanoid = true;
                if (!GameComponent_MechanicalFlightRegistry.IsAuthorized(pawn))
                {
                    return "MAP_MechanicalFlight_CaravanMemberCannotFly".Translate(pawn.LabelShort);
                }
            }

            if (!hasMechanoid)
            {
                return "MAP_MechanicalFlight_CaravanNoMechanoid".Translate();
            }
            if (!caravan.PawnsListForReading.Any(pawn =>
                    !pawn.Dead && CaravanUtility.IsOwner(pawn, Faction.OfPlayer)))
            {
                return "MAP_MechanicalFlight_CaravanNoOwner".Translate();
            }
            if (!caravan.Tile.Valid || !caravan.Tile.LayerDef.canFormCaravans)
            {
                return "MAP_MechanicalFlight_CaravanUnavailable".Translate();
            }
            return true;
        }

        public static AcceptanceReport CanFlyTo(Caravan? caravan, PlanetTile destinationTile)
        {
            AcceptanceReport launchReport = CanLaunch(caravan);
            if (!launchReport.Accepted)
            {
                return launchReport;
            }
            if (!destinationTile.Valid || destinationTile == caravan!.Tile)
            {
                return "MAP_MechanicalFlight_CaravanInvalidDestination".Translate();
            }
            if (destinationTile.Layer != caravan.Tile.Layer
                || !destinationTile.LayerDef.canFormCaravans)
            {
                return "MAP_MechanicalFlight_CaravanWrongLayer".Translate();
            }
            if (Find.World.Impassable(destinationTile))
            {
                return "MAP_MechanicalFlight_CaravanImpassableDestination".Translate();
            }
            return true;
        }

        public static Command_Action MakeCommand(Caravan caravan)
        {
            Command_Action command = new()
            {
                defaultLabel = "MAP_MechanicalFlight_CaravanLaunchLabel".Translate(),
                defaultDesc = "MAP_MechanicalFlight_CaravanLaunchDesc".Translate(),
                icon = FlightIcon,
                action = () => BeginTargeting(caravan)
            };
            AcceptanceReport report = CanLaunch(caravan);
            if (!report.Accepted)
            {
                command.Disable(report.Reason);
            }
            return command;
        }

        private static void BeginTargeting(Caravan caravan)
        {
            AcceptanceReport report = CanLaunch(caravan);
            if (!report.Accepted)
            {
                Messages.Message(report.Reason, caravan,
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            PlanetTile origin = caravan.Tile;
            CameraJumper.TryJump(new GlobalTargetInfo(origin));
            Find.WorldSelector.ClearSelection();
            Find.WorldTargeter.BeginTargeting(
                target => TryChooseDestination(caravan, target),
                canTargetTiles: true,
                mouseAttachment: FlightIcon,
                closeWorldTabWhenFinished: false,
                onUpdate: null,
                extraLabelGetter: target => GetTargetingLabel(caravan, target),
                canSelectTarget: null,
                originForClosest: origin,
                showCancelButton: true);
        }

        private static bool TryChooseDestination(Caravan caravan, GlobalTargetInfo target)
        {
            PlanetTile destination = target.Tile;
            AcceptanceReport report = CanFlyTo(caravan, destination);
            if (!report.Accepted)
            {
                Messages.Message(report.Reason, MessageTypeDefOf.RejectInput, false);
                return false;
            }
            return TryLaunch(caravan, destination);
        }

        private static TaggedString GetTargetingLabel(
            Caravan caravan, GlobalTargetInfo target)
        {
            AcceptanceReport report = CanFlyTo(caravan, target.Tile);
            if (!report.Accepted)
            {
                return report.Reason;
            }
            WorldObject? targetObject = target.HasWorldObject ? target.WorldObject : null;
            string destinationLabel = targetObject?.Label ?? target.Tile.ToString();
            return "MAP_MechanicalFlight_CaravanTargetLabel".Translate(destinationLabel);
        }

        private static bool TryLaunch(Caravan caravan, PlanetTile destinationTile)
        {
            AcceptanceReport report = CanFlyTo(caravan, destinationTile);
            if (!report.Accepted)
            {
                Messages.Message(report.Reason, caravan,
                    MessageTypeDefOf.RejectInput, false);
                return false;
            }

            int expectedPawnCount = caravan.PawnsListForReading.Count;
            ActiveTransporterInfo transporter = new();
            WorldObject_MechanicalFlyingCaravan flyingCaravan =
                (WorldObject_MechanicalFlyingCaravan)WorldObjectMaker.MakeWorldObject(
                    MechanicalFlightWorldDefOf.MAP_MechanicalFlyingCaravan);
            flyingCaravan.Tile = caravan.Tile;
            flyingCaravan.SetFaction(Faction.OfPlayer);
            flyingCaravan.destinationTile = destinationTile;
            flyingCaravan.arrivalAction =
                new MechanicalFlyingCaravanArrivalAction(caravan.Name);

            try
            {
                LoadCaravanCargo(caravan.pawns, transporter.innerContainer);
                transporter.innerContainer.TryAddRangeOrTransfer(caravan.pawns);
                if (transporter.innerContainer.OfType<Pawn>().Count() != expectedPawnCount)
                {
                    throw new InvalidOperationException("远行队成员转移数量不一致。");
                }

                Find.WorldObjects.Add(flyingCaravan);
                flyingCaravan.AddTransporter(transporter, justLeftTheMap: false);
            }
            catch (Exception exception)
            {
                Log.Error("[MAP-机械族机械师] 机械飞行远行队起飞失败：" + exception);
                RollBackLaunch(caravan, flyingCaravan, transporter);
                Messages.Message("MAP_MechanicalFlight_CaravanLaunchFailed".Translate(), caravan,
                    MessageTypeDefOf.RejectInput, false);
                return false;
            }

            caravan.Destroy();
            Find.WorldSelector.ClearSelection();
            Find.WorldSelector.Select(flyingCaravan);
            return true;
        }

        private static void LoadCaravanCargo(
            IEnumerable<Pawn> caravanPawns, ThingOwner destination)
        {
            foreach (Pawn pawn in caravanPawns)
            {
                int safety = 1000;
                while (pawn.inventory?.FirstUnloadableThing.Thing != null && safety-- > 0)
                {
                    ThingCount unloadable = pawn.inventory.FirstUnloadableThing;
                    pawn.inventory.innerContainer.TryTransferToContainer(
                        unloadable.Thing, destination, unloadable.Count);
                }
            }
        }

        private static void RollBackLaunch(
            Caravan caravan,
            WorldObject_MechanicalFlyingCaravan flyingCaravan,
            ActiveTransporterInfo transporter)
        {
            if (flyingCaravan.Spawned && !flyingCaravan.Destroyed)
            {
                flyingCaravan.Destroy();
            }

            List<Thing> snapshot = transporter.innerContainer.ToList();
            for (int i = 0; i < snapshot.Count; i++)
            {
                Thing thing = snapshot[i];
                transporter.innerContainer.Remove(thing);
                if (thing is Pawn pawn && !pawn.Dead)
                {
                    caravan.AddPawn(pawn, addCarriedPawnToWorldPawnsIfAny: false);
                }
                else if (!thing.Destroyed)
                {
                    CaravanInventoryUtility.GiveThing(caravan, thing);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Caravan), nameof(Caravan.GetGizmos))]
    internal static class MechanicalFlightCaravanGizmoPatch
    {
        public static IEnumerable<Gizmo> Postfix(
            IEnumerable<Gizmo> __result, Caravan __instance)
        {
            foreach (Gizmo gizmo in __result)
            {
                yield return gizmo;
            }

            if (__instance != null && __instance.Faction == Faction.OfPlayer
                && __instance.PawnsListForReading.Any(pawn =>
                    pawn.RaceProps?.IsMechanoid == true))
            {
                yield return MechanicalFlightCaravanUtility.MakeCommand(__instance);
            }
        }
    }
}
