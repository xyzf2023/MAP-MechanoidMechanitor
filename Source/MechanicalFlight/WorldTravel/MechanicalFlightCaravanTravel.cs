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

        // legacy 存档兼容字段：旧版本用 arrivalStarted 做一次性抵达保护。
        // 新逻辑完全不读取它（不参与 StillValid / Arrived / 防重入），
        // 仅保留 Scribe key 以免旧存档字段被丢弃；旧档中的 true 不再导致抵达被拒绝。
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
            // 在真正执行自定义抵达前完成可确定的前置检查；任一不满足即返回 false，
            // 让原版 TravellingTransporters 自行进入 fallback ArrivalAction 选择。
            if (!destinationTile.Valid
                || destinationTile.LayerDef == null
                || !destinationTile.LayerDef.canFormCaravans
                || !HasLivingPotentialCaravanOwner(pods))
            {
                return false;
            }

            // 必须存在合法的最终降落地块（当前 Tile 可通行，或能找到最近可通行 Tile）。
            return TryResolveLandingTile(destinationTile, out _);
        }

        public override void Arrived(
            List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            // 阶段 1：只做前置解析与快照，不修改任何容器，确保失败时可以安全兜底。
            List<Pawn> caravanPawns = new List<Pawn>();
            List<Thing> looseThings = new List<Thing>();
            for (int i = 0; i < transporters.Count; i++)
            {
                ThingOwner container = transporters[i].innerContainer;
                List<Thing> snapshot = container.ToList();
                for (int j = 0; j < snapshot.Count; j++)
                {
                    Thing thing = snapshot[j];
                    if (thing is Pawn pawn && !pawn.Dead && !pawn.Destroyed && !pawn.Discarded)
                    {
                        caravanPawns.Add(pawn);
                    }
                    else if (!thing.Destroyed && !thing.Discarded)
                    {
                        looseThings.Add(thing);
                    }
                }
            }

            // 核心前置条件不满足：不进入自定义事务，直接交还原版 FormCaravan 作为
            // 最终处理路径，避免内容被原版 transporters.Clear()/Destroy() 丢弃。
            if (Faction.OfPlayer == null
                || caravanPawns.Count == 0
                || !TryResolveLandingTile(tile, out PlanetTile landingTile)
                || landingTile.LayerDef == null
                || !landingTile.LayerDef.canFormCaravans)
            {
                Log.Error(
                    "[MAP-机械族机械师] 机械飞行远行队抵达失败：没有可用于重建远行队的存活成员" +
                    "或合法降落地块，已交还原版处理。");
                RunVanillaFormCaravanFallback(transporters, tile);
                return;
            }

            // 阶段 2：建立新的 Caravan。仅 Pawn 必须先离开旧容器（与原版 FormCaravan 一致）；
            // 物资仍留在容器中，等 Caravan 成为权威 Owner 后再逐项 best-effort 转移。
            for (int i = 0; i < caravanPawns.Count; i++)
            {
                caravanPawns[i].holdingOwner?.Remove(caravanPawns[i]);
            }

            Caravan? caravan;
            try
            {
                caravan = CaravanMaker.MakeCaravan(
                    caravanPawns, Faction.OfPlayer, landingTile,
                    addToWorldPawnsIfNotAlready: true);
            }
            catch (Exception exception)
            {
                // Caravan 尚未真正建立：可以安全恢复尚未进入任何 Caravan 的 Pawn，
                // 再交还原版 FormCaravan 兜底，避免重复归属或丢人。
                Log.Error("[MAP-机械族机械师] 机械飞行远行队重建时发生核心异常：" + exception);
                RestoreToContainers(
                    transporters,
                    caravanPawns.Where(pawn => pawn.GetCaravan() == null).Cast<Thing>());
                RunVanillaFormCaravanFallback(transporters, tile);
                return;
            }

            if (caravan == null)
            {
                Log.Error("[MAP-机械族机械师] 机械飞行远行队重建失败：CaravanMaker 返回空。");
                RestoreToContainers(
                    transporters,
                    caravanPawns.Where(pawn => pawn.GetCaravan() == null).Cast<Thing>());
                RunVanillaFormCaravanFallback(transporters, tile);
                return;
            }

            // 阶段 3/4：Caravan 已是权威 Owner，此后禁止把任何内容回滚到 transporter。
            TryRestoreCaravanName(caravan);
            TransferLooseThingsToCaravan(caravan, looseThings);
            NotifyCaravanArrived(caravan, tile);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref originalCaravanName, "originalCaravanName", string.Empty);
            // legacy 存档兼容：保留 Scribe key，仅回读，不参与任何业务逻辑。
            Scribe_Values.Look(ref arrivalStarted, "arrivalStarted", defaultValue: false);
        }

        private void TryRestoreCaravanName(Caravan caravan)
        {
            if (originalCaravanName.NullOrEmpty())
            {
                return;
            }

            try
            {
                caravan.Name = originalCaravanName;
            }
            catch (Exception exception)
            {
                Log.Error("[MAP-机械族机械师] 恢复机械飞行远行队名称失败：" + exception);
            }
        }

        private static void TransferLooseThingsToCaravan(
            Caravan caravan, List<Thing> looseThings)
        {
            for (int i = 0; i < looseThings.Count; i++)
            {
                Thing thing = looseThings[i];
                if (thing == null || thing.Destroyed || thing.Discarded)
                {
                    continue;
                }

                try
                {
                    // CaravanInventoryUtility.GiveThing 要求目标不在其它容器中，
                    // 因此先脱离旧运输舱容器，再交给已经建立的 Caravan。
                    thing.holdingOwner?.Remove(thing);
                    CaravanInventoryUtility.GiveThing(caravan, thing);
                }
                catch (Exception exception)
                {
                    // 单个物资转移失败只记录，不回滚已经成功建立的 Caravan。
                    Log.Error(
                        "[MAP-机械族机械师] 机械飞行远行队抵达时转移物资失败：" +
                        thing + "：" + exception);
                }
            }
        }

        private static void NotifyCaravanArrived(Caravan caravan, PlanetTile tile)
        {
            // 非关键通知：任一失败都不得影响已经建立的 Caravan。
            try
            {
                Messages.Message("MAP_MechanicalFlight_CaravanArrived".Translate(), caravan,
                    MessageTypeDefOf.TaskCompletion, false);
            }
            catch (Exception exception)
            {
                Log.Error("[MAP-机械族机械师] 机械飞行远行队抵达消息发送失败：" + exception);
            }

            try
            {
                Find.WorldObjects.WorldObjectAt<PeaceTalks>(tile)
                    ?.Notify_CaravanArrived(caravan);
            }
            catch (Exception exception)
            {
                Log.Error("[MAP-机械族机械师] 机械飞行远行队 PeaceTalks 通知失败：" + exception);
            }
        }

        private static bool TryResolveLandingTile(
            PlanetTile desiredTile, out PlanetTile landingTile)
        {
            landingTile = desiredTile;
            if (!desiredTile.Valid)
            {
                return false;
            }

            if (!Find.World.Impassable(landingTile))
            {
                return true;
            }

            return GenWorldClosest.TryFindClosestPassableTile(landingTile, out landingTile);
        }

        private static bool HasLivingPotentialCaravanOwner(IEnumerable<IThingHolder> pods)
        {
            Faction? playerFaction = Faction.OfPlayer;
            if (playerFaction == null)
            {
                return false;
            }

            foreach (IThingHolder pod in pods)
            {
                ThingOwner? container = pod.GetDirectlyHeldThings();
                if (container == null)
                {
                    continue;
                }

                for (int i = 0; i < container.Count; i++)
                {
                    if (container[i] is Pawn pawn
                        && !pawn.Dead
                        && !pawn.Destroyed
                        && !pawn.Discarded
                        && CaravanUtility.IsOwner(pawn, playerFaction))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void RunVanillaFormCaravanFallback(
            List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            try
            {
                // 复用原版 FormCaravan：它在 Arrived 内自行完成移除、建队与物资转移，
                // 不依赖 transporter 在返回后继续存在，与原版生命周期完全一致。
                new TransportersArrivalAction_FormCaravan()
                    .Arrived(transporters, tile);
            }
            catch (Exception exception)
            {
                Log.Error(
                    "[MAP-机械族机械师] 机械飞行远行队交还原版 FormCaravan 兜底失败：" +
                    exception);
            }
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
