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
                ThingOwner? container = transporters[i]?.innerContainer;
                if (container == null)
                {
                    continue;
                }
                List<Thing> snapshot = container.ToList();
                for (int j = 0; j < snapshot.Count; j++)
                {
                    Thing thing = snapshot[j];
                    if (thing is Pawn pawn)
                    {
                        if (IsUsablePawn(pawn))
                        {
                            caravanPawns.Add(pawn);
                        }
                    }
                    else if (!thing.Destroyed && !thing.Discarded)
                    {
                        looseThings.Add(thing);
                    }
                }
            }

            PlanetTile landingTile = tile;
            bool canRunCustomArrival =
                Faction.OfPlayer != null
                && caravanPawns.Count > 0
                && TryResolveLandingTile(tile, out landingTile)
                && landingTile.LayerDef != null
                && landingTile.LayerDef.canFormCaravans;

            // 核心前置条件不满足：交还原版 FormCaravan 作为第一处理路径；
            // 若原版再次异常，仍由最终恢复逻辑保证 Pawn / 物资安全归属。
            if (!canRunCustomArrival)
            {
                Log.Error(
                    "[MAP-机械族机械师] 机械飞行远行队抵达失败：没有可用于重建远行队的存活成员" +
                    "或合法降落地块，已交还原版处理。");
                List<Caravan> caravansBeforeFallback = Find.WorldObjects.Caravans.ToList();
                if (!TryRunVanillaFormCaravanFallback(transporters, tile))
                {
                    FinalRecoverContents(
                        caravanPawns, looseThings, landingTile, caravansBeforeFallback);
                }
                return;
            }

            // 阶段 2：Pawn 必须先离开旧容器才能进入 Caravan（与原版 FormCaravan 一致）；
            // 物资仍留在容器中，等 Caravan 成为权威 Owner 后再逐项安全转移。
            for (int i = 0; i < caravanPawns.Count; i++)
            {
                RemoveFromHolder(caravanPawns[i]);
            }

            List<Caravan> caravansBefore = Find.WorldObjects.Caravans.ToList();
            Caravan? caravan = null;
            bool makeCaravanThrew = false;
            try
            {
                caravan = CaravanMaker.MakeCaravan(
                    caravanPawns, Faction.OfPlayer, landingTile,
                    addToWorldPawnsIfNotAlready: true);
            }
            catch (Exception exception)
            {
                makeCaravanThrew = true;
                Log.Error("[MAP-机械族机械师] 机械飞行远行队重建时发生核心异常：" + exception);
            }

            if (caravan != null)
            {
                EnsurePawnsHaveStableOwner(caravanPawns, caravan, "MakeCaravan 成功后的所有权校验");
                CompleteCaravanArrival(caravan, looseThings, tile);
                return;
            }

            if (!makeCaravanThrew)
            {
                Log.Error("[MAP-机械族机械师] 机械飞行远行队重建失败：CaravanMaker 返回空。");
            }

            // 阶段 3：CaravanMaker 不是事务式 API。异常后世界中可能已经留下一个
            // “半创建 Caravan”（已加入 WorldObjects，但未完成初始化或只加入了部分 Pawn）。
            // 优先修复它，避免创建第二个 Caravan 造成 Pawn 分裂。
            if (TryRecoverPartialCaravan(
                    caravansBefore,
                    caravanPawns,
                    landingTile,
                    ensureNewCaravanUniqueId: true,
                    out Caravan recoveredCaravan,
                    out List<Pawn> unplacedPawns))
            {
                Log.Error(
                    "[MAP-机械族机械师] 机械飞行远行队重建异常后接管 partial Caravan：" +
                    DescribeCaravan(recoveredCaravan) +
                    "；未入队 Pawn=" + unplacedPawns.Count);
                // 先完成 Pawn 所有权修复，再转移物资。否则 partial Caravan 在 AddPawn 前抛异常时
                // 可能暂时没有成员，提前处理物资会误判为“无接收者”并触发最终处置。
                EnsurePawnsHaveStableOwner(unplacedPawns, recoveredCaravan, "partial Caravan 修复");
                CompleteCaravanArrival(recoveredCaravan, looseThings, tile);
                return;
            }

            Log.Error(
                "[MAP-机械族机械师] 机械飞行远行队重建异常且未识别到 partial Caravan，" +
                "已将内容恢复至运输舱并交还原版 FormCaravan。");

            // 阶段 4：没有任何 partial Caravan 可修复，才允许恢复内容并进入原版兜底。
            RestoreToContainers(transporters, caravanPawns.Where(PawnNeedsStableOwner));
            List<Caravan> caravansBeforeFallbackTwo = Find.WorldObjects.Caravans.ToList();
            if (TryRunVanillaFormCaravanFallback(transporters, tile))
            {
                EnsurePawnsHaveStableOwner(caravanPawns, null, "原版 FormCaravan 兜底成功后的所有权校验");
                return;
            }

            // 阶段 5：原版兜底同样失败，进入最终恢复，绝不让 Pawn / 物资无主地留在运输舱里。
            Log.Error("[MAP-机械族机械师] 原版 FormCaravan 兜底再次失败，进入最终恢复。");
            FinalRecoverContents(
                caravanPawns, looseThings, landingTile, caravansBeforeFallbackTwo);
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
            try
            {
                if (!originalCaravanName.NullOrEmpty())
                {
                    caravan.Name = originalCaravanName;
                }
                else if (caravan.Name.NullOrEmpty())
                {
                    caravan.Name = CaravanNameGenerator.GenerateCaravanName(caravan);
                }
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

                if (!TryGiveThingToCaravanPawn(caravan, thing, out string failureReason))
                {
                    if (ThingHasStableOwner(thing))
                    {
                        Log.Warning(
                            "[MAP-机械族机械师] 机械飞行远行队物资已属于其它稳定 Caravan，不做处置：" +
                            thing + "；" + failureReason);
                        continue;
                    }
                    // 只有连“仍合法的 Caravan Pawn inventory”都无法承载时，才允许最终处置。
                    EmergencyDisposeThing(
                        thing, "Caravan=" + DescribeCaravan(caravan) + "；" + failureReason);
                }
            }
        }

        private static bool TryGiveThingToCaravanPawn(
            Caravan caravan, Thing thing, out string failureReason)
        {
            failureReason = string.Empty;
            ThingOwner? originalOwner;
            Pawn? receiver = null;
            try
            {
                // 原版 CaravanInventoryUtility.GiveThing 在找不到接收者时会直接 Destroy 物品。
                // 因此这里先做与原版一致的接收者预检，只有确认存在接收 Pawn 才脱离原 Owner。
                originalOwner = thing.holdingOwner;
                receiver = CaravanInventoryUtility.FindPawnToMoveInventoryTo(
                    thing, caravan.PawnsListForReading, null);
            }
            catch (Exception exception)
            {
                originalOwner = thing.holdingOwner;
                failureReason = "寻找接收 Pawn 时异常：" + exception;
            }

            if (receiver == null && caravan.PawnsListForReading.Count == 0)
            {
                failureReason = failureReason.NullOrEmpty()
                    ? "Caravan 没有任何可接收物资的 Pawn"
                    : failureReason;
                return false;
            }

            RemoveFromHolder(thing);

            if (receiver != null
                && receiver.inventory != null
                && receiver.inventory.innerContainer.TryAdd(thing))
            {
                return true;
            }

            // 预检接收者失败时的二级兜底：逐个尝试 Caravan 成员的 inventory（TryAdd 不会 Destroy 物品）。
            for (int i = 0; i < caravan.PawnsListForReading.Count; i++)
            {
                Pawn candidate = caravan.PawnsListForReading[i];
                if (candidate == null
                    || candidate == receiver
                    || candidate.inventory == null)
                {
                    continue;
                }
                if (candidate.inventory.innerContainer.TryAdd(thing))
                {
                    Log.Warning(
                        "[MAP-机械族机械师] 机械飞行远行队物资首选接收者失败，已改由其他成员接收：" +
                        thing + "；Caravan=" + DescribeCaravan(caravan));
                    return true;
                }
            }

            bool restored = false;
            if (originalOwner != null && !thing.Destroyed && thing.holdingOwner == null)
            {
                try
                {
                    restored = originalOwner.TryAdd(thing);
                }
                catch (Exception exception)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 恢复物资原 Owner 时异常：" +
                        thing + "：" + exception);
                }
            }

            failureReason =
                "receiver=" + (receiver?.ToString() ?? "null") +
                "；恢复原 Owner=" + restored +
                "；当前 ParentHolder=" +
                (thing.holdingOwner?.Owner.ToStringSafe() ?? "null") +
                (failureReason.NullOrEmpty() ? string.Empty : "；" + failureReason);
            Log.Error(
                "[MAP-机械族机械师] 机械飞行远行队物资接收失败：" +
                thing + "；" + failureReason);
            return false;
        }

        private static void EmergencyDisposeThing(Thing thing, string reason)
        {
            Log.Error(
                "[MAP-机械族机械师] 机械飞行远行队最终处置物资（无法安全归属）：" +
                thing + "；ParentHolder=" +
                (thing?.holdingOwner?.Owner.ToStringSafe() ?? "null") +
                "；原因：" + reason);
            try
            {
                if (thing == null || thing.Destroyed || thing.Discarded)
                {
                    return;
                }
                RemoveFromHolder(thing);
                thing.DestroyOrPassToWorld();
            }
            catch (Exception exception)
            {
                Log.Error("[MAP-机械族机械师] 最终处置物资时异常：" + exception);
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

        private void CompleteCaravanArrival(
            Caravan caravan, List<Thing> looseThings, PlanetTile tile)
        {
            // Caravan 已成为权威 Owner；此后禁止把任何内容回滚到运输舱。
            TryRestoreCaravanName(caravan);
            TransferLooseThingsToCaravan(caravan, looseThings);
            NotifyCaravanArrived(caravan, tile);
        }

        private void FinalRecoverContents(
            List<Pawn> caravanPawns,
            List<Thing> looseThings,
            PlanetTile landingTile,
            List<Caravan> caravansBefore)
        {
            Log.Error(
                "[MAP-机械族机械师] 机械飞行远行队进入最终恢复：Tile=" + landingTile +
                "；Pawn 数=" + caravanPawns.Count + "；物资数=" + looseThings.Count);

            // 优先级 1/2/3：已存在且合法的 Caravan、可修复的 partial Caravan、
            // 或原版兜底过程留下的 Caravan，统一按“识别 + 修复 + 合并”处理。
            if (TryRecoverPartialCaravan(
                    caravansBefore,
                    caravanPawns,
                    landingTile,
                    ensureNewCaravanUniqueId: false,
                    out Caravan recoveredCaravan,
                    out List<Pawn> unplacedPawns))
            {
                Log.Error(
                    "[MAP-机械族机械师] 最终恢复接管 Caravan：" + DescribeCaravan(recoveredCaravan));
                // 与主抵达路径保持同一事务顺序：先稳定 Pawn 所有权，再允许货物离开运输舱。
                EnsurePawnsHaveStableOwner(unplacedPawns, recoveredCaravan, "最终恢复");
                CompleteCaravanArrival(recoveredCaravan, looseThings, landingTile);
                return;
            }

            // 优先级 4：确实没有任何 Caravan 可用时，Pawn 转入世界 Pawn 最终归宿；
            // 物资在尝试过所有 Caravan 成员后仍无法归属时执行明确处置。
            Log.Error(
                "[MAP-机械族机械师] 最终恢复未找到任何可用 Caravan：" +
                "Pawn 转入世界 Pawn 最终归宿，物资执行最终处置。");
            EnsurePawnsHaveStableOwner(caravanPawns, null, "最终恢复：无可用 Caravan");
            for (int i = 0; i < looseThings.Count; i++)
            {
                Thing thing = looseThings[i];
                if (ThingHasStableOwner(thing))
                {
                    continue;
                }
                EmergencyDisposeThing(
                    thing, "最终恢复：没有可用于接收物资的 Caravan Pawn");
            }
        }

        private static bool TryRecoverPartialCaravan(
            List<Caravan> caravansBefore,
            List<Pawn> caravanPawns,
            PlanetTile landingTile,
            bool ensureNewCaravanUniqueId,
            out Caravan recoveredCaravan,
            out List<Pawn> unplacedPawns)
        {
            recoveredCaravan = null!;
            unplacedPawns = new List<Pawn>();

            List<Caravan> newCaravans = Find.WorldObjects.Caravans
                .Where(candidate => !caravansBefore.Contains(candidate))
                .ToList();
            List<Caravan> involvedCaravans = new List<Caravan>();
            for (int i = 0; i < caravanPawns.Count; i++)
            {
                Caravan? owner = caravanPawns[i]?.GetCaravan();
                if (owner != null && !involvedCaravans.Contains(owner))
                {
                    involvedCaravans.Add(owner);
                }
            }
            for (int i = 0; i < newCaravans.Count; i++)
            {
                if (!involvedCaravans.Contains(newCaravans[i]))
                {
                    involvedCaravans.Add(newCaravans[i]);
                }
            }

            // 只接管“仍在世界对象列表中且未被销毁”的 Caravan，损坏对象一律不动。
            List<Caravan> usableCaravans = involvedCaravans
                .Where(candidate => candidate != null
                    && !candidate.Destroyed
                    && candidate.Spawned
                    && (candidate.Faction == null || candidate.Faction == Faction.OfPlayer))
                .ToList();
            if (usableCaravans.Count == 0)
            {
                return false;
            }

            // 多个 partial Caravan 时禁止再建第三个：选择持有本次 Pawn 最多的作为主 Caravan，
            // 其余只收拢 Pawn，绝不在其它 Caravan 仍持有 Pawn 时销毁它。
            Caravan mainCaravan = usableCaravans
                .OrderByDescending(candidate => caravanPawns.Count(
                    pawn => pawn != null && pawn.GetCaravan() == candidate))
                .ThenBy(candidate => candidate.ID)
                .First();
            if (usableCaravans.Count > 1)
            {
                Log.Error(
                    "[MAP-机械族机械师] 机械飞行远行队检测到多个 partial Caravan：" +
                    string.Join("；", usableCaravans.Select(DescribeCaravan)) +
                    "。已选择主 Caravan=" + DescribeCaravan(mainCaravan));
            }

            for (int i = 0; i < usableCaravans.Count; i++)
            {
                Caravan other = usableCaravans[i];
                if (other == mainCaravan)
                {
                    continue;
                }
                for (int j = 0; j < caravanPawns.Count; j++)
                {
                    Pawn pawn = caravanPawns[j];
                    if (pawn == null || pawn.GetCaravan() != other)
                    {
                        continue;
                    }
                    other.RemovePawn(pawn);
                    if (!TryAddPawnToCaravan(mainCaravan, pawn))
                    {
                        PassPawnToWorldSafely(pawn, "多个 partial Caravan 合并失败");
                    }
                }

                if (other.PawnsListForReading.Count == 0
                    && CaravanInventoryUtility.AllInventoryItems(other).Count == 0)
                {
                    other.Destroy();
                }
                else
                {
                    Log.Error(
                        "[MAP-机械族机械师] 异常 Caravan 仍持有内容，保留不销毁：" +
                        DescribeCaravan(other));
                }
            }

            if (mainCaravan.Faction != Faction.OfPlayer)
            {
                mainCaravan.SetFaction(Faction.OfPlayer);
            }
            if (!mainCaravan.Tile.Valid && landingTile.Valid)
            {
                mainCaravan.Tile = landingTile;
            }
            if (ensureNewCaravanUniqueId && newCaravans.Contains(mainCaravan))
            {
                // 自定义 MakeCaravan 只有在最后一步才会设置 uniqueId；异常中断时必然尚未设置。
                // 原版 fallback 可能在建队成功后异常，此时 uniqueId 已设置，不能重复补设。
                TryEnsureCaravanUniqueId(mainCaravan);
            }

            for (int i = 0; i < caravanPawns.Count; i++)
            {
                Pawn pawn = caravanPawns[i];
                if (!IsUsablePawn(pawn) || mainCaravan.ContainsPawn(pawn))
                {
                    continue;
                }
                Caravan? owner = pawn.GetCaravan();
                if (owner != null && owner != mainCaravan
                    && !owner.Destroyed && owner.Spawned)
                {
                    continue;
                }
                if (!TryAddPawnToCaravan(mainCaravan, pawn))
                {
                    unplacedPawns.Add(pawn);
                }
            }

            recoveredCaravan = mainCaravan;
            return true;
        }

        private static void TryEnsureCaravanUniqueId(Caravan caravan)
        {
            try
            {
                caravan.SetUniqueId(Find.UniqueIDsManager.GetNextCaravanID());
            }
            catch (Exception exception)
            {
                Log.Error("[MAP-机械族机械师] 为 partial Caravan 补设 uniqueId 失败：" + exception);
            }
        }

        private static bool TryAddPawnToCaravan(Caravan caravan, Pawn pawn)
        {
            try
            {
                if (caravan.ContainsPawn(pawn))
                {
                    return true;
                }
                if (pawn.holdingOwner != null)
                {
                    pawn.holdingOwner.Remove(pawn);
                }
                caravan.AddPawn(pawn, addCarriedPawnToWorldPawnsIfAny: true);
                if (caravan.ContainsPawn(pawn))
                {
                    if (!pawn.IsWorldPawn())
                    {
                        Find.WorldPawns.PassToWorld(pawn);
                    }
                    return true;
                }
                Log.Error(
                    "[MAP-机械族机械师] 无法把 Pawn 加入 Caravan：" +
                    pawn + "；Caravan=" + DescribeCaravan(caravan));
            }
            catch (Exception exception)
            {
                Log.Error(
                    "[MAP-机械族机械师] 把 Pawn 加入 Caravan 时异常：" +
                    pawn + "：" + exception);

                // Caravan.AddPawn / 后续 PassToWorld 都可能在已经产生部分有效结果后抛异常。
                // catch 后必须重新检查真实所有权；只要 Pawn 已经属于一个仍有效的目标 Caravan，
                // 就将本次操作视为成功，禁止上层再次把它拆出 Caravan 并转成孤立 WorldPawn。
                if (!caravan.Destroyed && caravan.Spawned && caravan.ContainsPawn(pawn))
                {
                    Log.Warning(
                        "[MAP-机械族机械师] 加入 Caravan 过程中虽发生异常，但 Pawn 已具有稳定 Caravan 所有权：" +
                        pawn + "；Caravan=" + DescribeCaravan(caravan));
                    return true;
                }
            }
            return false;
        }

        private static void EnsurePawnsHaveStableOwner(
            IEnumerable<Pawn> pawns, Caravan? preferredCaravan, string context)
        {
            List<Pawn> pawnList = pawns.ToList();
            for (int i = 0; i < pawnList.Count; i++)
            {
                Pawn pawn = pawnList[i];
                if (!IsUsablePawn(pawn) || !PawnNeedsStableOwner(pawn))
                {
                    continue;
                }
                if (preferredCaravan != null
                    && !preferredCaravan.Destroyed
                    && preferredCaravan.Spawned
                    && TryAddPawnToCaravan(preferredCaravan, pawn))
                {
                    continue;
                }
                PassPawnToWorldSafely(pawn, context);
            }
        }

        private static void PassPawnToWorldSafely(Pawn pawn, string context)
        {
            try
            {
                // 原版 PassToWorld 前置条件：Pawn 未 Spawned 且未在 WorldPawns 中。
                if (pawn.Spawned)
                {
                    Log.Error(
                        "[MAP-机械族机械师] Pawn 最终归宿拒绝处理已在地图上的 Pawn：" +
                        pawn + "；" + context);
                    return;
                }
                RemoveFromHolder(pawn);
                if (!Find.WorldPawns.Contains(pawn))
                {
                    Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
                }
                // WorldPawnGC 只会保留 ForcefullyKeptPawns 中的无归属世界 Pawn，
                // 否则玩家机械体可能在后续 GC 中被静默回收。
                Find.WorldPawns.ForcefullyKeptPawns.Add(pawn);
                Log.Error(
                    "[MAP-机械族机械师] Pawn 最终归宿：已转入世界 Pawn 并锁定：" +
                    pawn + "；" + context);
            }
            catch (Exception exception)
            {
                Log.Error(
                    "[MAP-机械族机械师] Pawn 最终归宿处理异常：" + pawn + "：" + exception);
            }
        }

        private static void RemoveFromHolder(Thing thing)
        {
            if (thing == null || thing.Destroyed || thing.Discarded)
            {
                return;
            }
            try
            {
                thing.holdingOwner?.Remove(thing);
            }
            catch (Exception exception)
            {
                Log.Error(
                    "[MAP-机械族机械师] 内容脱离原 Owner 时异常：" + thing + "：" + exception);
            }
        }

        private static bool IsUsablePawn(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && !pawn.Destroyed && !pawn.Discarded;
        }

        private static bool PawnNeedsStableOwner(Pawn pawn)
        {
            if (!IsUsablePawn(pawn))
            {
                return false;
            }
            Caravan? caravan = pawn.GetCaravan();
            return caravan == null || caravan.Destroyed || !caravan.Spawned;
        }

        private static bool ThingHasStableOwner(Thing thing)
        {
            if (thing == null || thing.Destroyed || thing.Discarded)
            {
                return true;
            }
            Caravan? caravan = thing.GetCaravan();
            return caravan != null && !caravan.Destroyed && caravan.Spawned;
        }

        private static string DescribeCaravan(Caravan caravan)
        {
            if (caravan == null)
            {
                return "null";
            }
            return "ID=" + caravan.ID +
                "；Name=" + (caravan.Name ?? "null") +
                "；Pawn 数=" + caravan.PawnsListForReading.Count +
                "；Tile=" + caravan.Tile +
                "；Spawned=" + caravan.Spawned;
        }

        private static bool TryRunVanillaFormCaravanFallback(
            List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            try
            {
                // 复用原版 FormCaravan：它在 Arrived 内自行完成移除、建队与物资转移，
                // 不依赖 transporter 在返回后继续存在，与原版生命周期完全一致。
                new TransportersArrivalAction_FormCaravan()
                    .Arrived(transporters, tile);
                return true;
            }
            catch (Exception exception)
            {
                Log.Error(
                    "[MAP-机械族机械师] 机械飞行远行队交还原版 FormCaravan 兜底失败：" +
                    exception);
                return false;
            }
        }

        private static void RestoreToContainers(
            List<ActiveTransporterInfo> transporters, IEnumerable<Thing> things)
        {
            ActiveTransporterInfo? first = transporters.FirstOrDefault();
            if (first == null)
            {
                Log.Error("[MAP-机械族机械师] 没有可用运输舱，无法恢复内容。");
                return;
            }
            foreach (Thing thing in things.ToList())
            {
                if (thing == null || thing.Destroyed || thing.Discarded || thing.Spawned)
                {
                    continue;
                }
                try
                {
                    if (!first.innerContainer.Contains(thing) && thing.holdingOwner != null)
                    {
                        thing.holdingOwner.Remove(thing);
                    }
                    if (!first.innerContainer.Contains(thing)
                        && !first.innerContainer.TryAdd(thing))
                    {
                        Log.Error(
                            "[MAP-机械族机械师] 内容恢复至运输舱失败：" + thing +
                            "；ParentHolder=" +
                            (thing.holdingOwner?.Owner.ToStringSafe() ?? "null"));
                    }
                }
                catch (Exception exception)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 内容恢复至运输舱时异常：" +
                        thing + "：" + exception);
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
