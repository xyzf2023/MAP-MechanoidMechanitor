using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public readonly struct MechanoidOvermindDeliveryResult
    {
        public bool Success { get; }

        public string? ErrorKey { get; }

        public int SpentAmount { get; }

        private MechanoidOvermindDeliveryResult(bool success, string? errorKey, int spentAmount)
        {
            Success = success;
            ErrorKey = errorKey;
            SpentAmount = spentAmount;
        }

        public static MechanoidOvermindDeliveryResult Succeeded(int spentAmount)
        {
            return new MechanoidOvermindDeliveryResult(true, null, spentAmount);
        }

        public static MechanoidOvermindDeliveryResult Failed(string errorKey)
        {
            return new MechanoidOvermindDeliveryResult(false, errorKey, 0);
        }
    }

    public static class MechanoidOvermindDeliveryService
    {
        public const string ErrorConnection =
            "MAP_MechanoidMechanitor.PurgeDirective.Communication.Error.ConnectionLost";

        public const string ErrorNoMap =
            "MAP_MechanoidMechanitor.PurgeDirective.Communication.Error.NoMap";

        public const string ErrorNoDropSpot =
            "MAP_MechanoidMechanitor.PurgeDirective.Communication.Error.NoDropSpot";

        public const string ErrorInvalidOrder =
            "MAP_MechanoidMechanitor.PurgeDirective.Communication.Error.InvalidOrder";

        public const string ErrorInsufficientCredits =
            "MAP_MechanoidMechanitor.PurgeDirective.Communication.Error.InsufficientCredits";

        public const string ErrorGenerationFailed =
            "MAP_MechanoidMechanitor.PurgeDirective.Communication.Error.GenerationFailed";

        public const string ErrorChargeFailed =
            "MAP_MechanoidMechanitor.PurgeDirective.Communication.Error.ChargeFailed";

        public const string ErrorRatingLocked =
            "MAP_PurgeDirectiveRating.Delivery.ErrorRatingLocked";

        public const string ErrorDropFailed =
            "MAP_MechanoidMechanitor.PurgeDirective.Communication.Error.DropFailed";

        private static int lastDropResolveErrorTick = int.MinValue;

        private static string? lastDropResolveErrorKey;

        public static bool TryResolveTradeDropTarget(
            Map? preferredMap,
            out Map? map,
            out IntVec3 cell)
        {
            map = null;
            cell = IntVec3.Invalid;
            try
            {
                Map? resolved = ResolvePlayerDeliveryMap(preferredMap);
                if (resolved == null)
                {
                    return false;
                }

                map = resolved;
                IntVec3 spot = DropCellFinder.TradeDropSpot(resolved);
                if (!spot.IsValid)
                {
                    return false;
                }

                cell = spot;
                return true;
            }
            catch (Exception ex)
            {
                LogDropResolveErrorOnce(ErrorNoDropSpot, ex);
                return false;
            }
        }

        public static MechanoidOvermindDeliveryResult TryDeliver(
            MechanoidOvermindOrder order,
            Map? preferredMap)
        {
            if (order == null || order.IsEmpty)
            {
                return MechanoidOvermindDeliveryResult.Failed(ErrorInvalidOrder);
            }

            if (!MechanoidMechanitorMechHiveCommunicationUtility.TryGetContactableMechHive(out _))
            {
                return MechanoidOvermindDeliveryResult.Failed(ErrorConnection);
            }

            Map? map;
            IntVec3 dropCell;
            try
            {
                if (!TryResolveTradeDropTarget(preferredMap, out map, out dropCell)
                    || map == null
                    || !dropCell.IsValid)
                {
                    if (map == null)
                    {
                        return MechanoidOvermindDeliveryResult.Failed(ErrorNoMap);
                    }

                    return MechanoidOvermindDeliveryResult.Failed(ErrorNoDropSpot);
                }
            }
            catch (Exception)
            {
                return MechanoidOvermindDeliveryResult.Failed(ErrorNoDropSpot);
            }

            MechanoidOvermindPricingService.ClearThingMarketValueCache();
            GameComponent_OvermindEconomy.Current?.Prepare(true);
            // 先校验订单本身有效（pre-discount），再计算含评级折扣的最终费用。
            if (!order.TryGetCosts(out _, out _, out int preTotalCost) || preTotalCost < 0)
            {
                return MechanoidOvermindDeliveryResult.Failed(ErrorInvalidOrder);
            }

            if (!MechanoidOvermindRatingPricingService.TryCalculateFinalOrderCosts(
                    order,
                    out _,
                    out _,
                    out _,
                    out _,
                    out int finalCost))
            {
                return MechanoidOvermindDeliveryResult.Failed(ErrorInvalidOrder);
            }

            // 最终执行入口：按当前有效评级权限重新校验每一行，防止降级后已下单内容被违规交付。
            foreach (MechanoidOvermindOrderLine_Mech mechLine in order.MechLines)
            {
                if (!PurgeDirectiveRatingUtility.IsMechUnlocked(
                        mechLine.Kind?.race?.race?.mechWeightClass))
                {
                    return MechanoidOvermindDeliveryResult.Failed(ErrorRatingLocked);
                }
            }

            foreach (MechanoidOvermindOrderLine_Thing thingLine in order.ThingLines)
            {
                if (!PurgeDirectiveRatingUtility.IsThingUnlocked(thingLine.Spec?.Def))
                {
                    return MechanoidOvermindDeliveryResult.Failed(ErrorRatingLocked);
                }
            }

            if (finalCost
                > GameComponent_MechanoidMechanitorStoryState.GetPurgeDirectiveRewardPoints())
            {
                return MechanoidOvermindDeliveryResult.Failed(ErrorInsufficientCredits);
            }

            if (Faction.OfPlayerSilentFail == null)
            {
                return MechanoidOvermindDeliveryResult.Failed(ErrorConnection);
            }

            ActiveTransporterInfo? info = null;
            bool spent = false;
            OvermindEconomyReservation? reservation = null;
            try
            {
                if (GameComponent_OvermindEconomy.Enabled && order.ThingLines.Count > 0)
                {
                    GameComponent_OvermindEconomy? economy = GameComponent_OvermindEconomy.Current;
                    if (economy == null || !economy.TryReserve(order, out reservation))
                        return MechanoidOvermindDeliveryResult.Failed(GameComponent_OvermindEconomy.SupplyError);
                }

                info = new ActiveTransporterInfo
                {
                    leaveSlag = false,
                    openDelay = ActiveTransporterInfo.DefaultOpenDelay
                };

                if (!TryFillTransporter(order, info))
                {
                    CleanupUndelivered(info);
                    return MechanoidOvermindDeliveryResult.Failed(ErrorGenerationFailed);
                }

                if (!GameComponent_MechanoidMechanitorStoryState.TrySpendPurgeDirectiveCredits(
                        finalCost))
                {
                    CleanupUndelivered(info);
                    return MechanoidOvermindDeliveryResult.Failed(ErrorChargeFailed);
                }

                spent = true;

                if (!MechanoidMechanitorMechHiveCommunicationUtility.TryGetContactableMechHive(
                        out Faction mechHive))
                {
                    SafeRefund(finalCost);
                    spent = false;
                    CleanupUndelivered(info);
                    return MechanoidOvermindDeliveryResult.Failed(ErrorConnection);
                }

                // MakeDropPodAt 正常返回即视为投送已提交（Contents 已挂到 ActiveTransporter）。
                // 传入实际联络的机械巢派系，由原版按 FactionDef.dropPodActive / dropPodIncoming 选用机械族空投仓。
                DropPodUtility.MakeDropPodAt(dropCell, map, info, mechHive);
                reservation?.Commit();
                return MechanoidOvermindDeliveryResult.Succeeded(finalCost);
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP] MechanoidOvermindDeliveryService.TryDeliver failed: " + ex);

                if (IsDropCommitted(info))
                {
                    reservation?.Commit();
                    // 投送对象已进入地图持有链：不退款、不清理内容。
                    return MechanoidOvermindDeliveryResult.Succeeded(finalCost);
                }

                AbortUncommittedDrop(info);
                if (spent)
                {
                    SafeRefund(finalCost);
                }

                return MechanoidOvermindDeliveryResult.Failed(
                    spent ? ErrorDropFailed : ErrorGenerationFailed);
            }
            finally
            {
                reservation?.Dispose();
            }
        }

        public static Map? ResolvePlayerDeliveryMap(Map? preferredMap)
        {
            if (IsPlayerOwnedMap(preferredMap))
            {
                return preferredMap;
            }

            Map? homeMap = Find.AnyPlayerHomeMap;
            if (IsPlayerOwnedMap(homeMap))
            {
                return homeMap;
            }

            if (IsPlayerOwnedMap(Find.CurrentMap))
            {
                return Find.CurrentMap;
            }

            List<Map> maps = Find.Maps;
            if (maps == null)
            {
                return null;
            }

            for (int i = 0; i < maps.Count; i++)
            {
                Map candidate = maps[i];
                if (IsPlayerOwnedMap(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        public static bool IsPlayerOwnedMap(Map? map)
        {
            if (map == null || map.Disposed)
            {
                return false;
            }

            if (map.IsPlayerHome)
            {
                return true;
            }

            Faction? parentFaction = map.ParentFaction;
            return parentFaction != null && parentFaction.IsPlayer;
        }

        private static bool IsDropCommitted(ActiveTransporterInfo? info)
        {
            if (info?.parent is not ActiveTransporter transporter)
            {
                return false;
            }

            try
            {
                if (transporter.Destroyed)
                {
                    return false;
                }

                if (transporter.SpawnedOrAnyParentSpawned)
                {
                    return true;
                }

                // 已挂到 Skyfaller 且该 Skyfaller 已在地图上，同样视为已提交。
                if (transporter.ParentHolder is Skyfaller skyfaller
                    && skyfaller.SpawnedOrAnyParentSpawned)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // 持有链查询异常时保守视为未提交，走清理退款。
            }

            return false;
        }

        private static void AbortUncommittedDrop(ActiveTransporterInfo? info)
        {
            if (info == null)
            {
                return;
            }

            try
            {
                if (info.parent is ActiveTransporter transporter
                    && !transporter.Destroyed
                    && !transporter.SpawnedOrAnyParentSpawned)
                {
                    Thing? holderThing = transporter.ParentHolder as Thing;

                    try
                    {
                        transporter.Contents = null;
                    }
                    catch (Exception)
                    {
                        // ignore
                    }

                    if (holderThing is Skyfaller skyfaller
                        && !skyfaller.Destroyed
                        && !skyfaller.SpawnedOrAnyParentSpawned)
                    {
                        try
                        {
                            skyfaller.innerContainer.Remove(transporter);
                        }
                        catch (Exception)
                        {
                            // ignore
                        }

                        try
                        {
                            if (!skyfaller.Destroyed)
                            {
                                skyfaller.Destroy(DestroyMode.Vanish);
                            }
                        }
                        catch (Exception)
                        {
                            // ignore
                        }
                    }

                    try
                    {
                        if (!transporter.Destroyed)
                        {
                            transporter.Destroy(DestroyMode.Vanish);
                        }
                    }
                    catch (Exception)
                    {
                        // ignore
                    }
                }
            }
            catch (Exception)
            {
                // ignore
            }

            CleanupUndelivered(info);
        }

        private static void SafeRefund(int amount)
        {
            try
            {
                GameComponent_MechanoidMechanitorStoryState.RefundPurgeDirectiveCredits(amount);
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP] MechanoidOvermindDeliveryService refund failed: " + ex);
            }
        }

        private static void LogDropResolveErrorOnce(string errorKey, Exception ex)
        {
            int tick = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (lastDropResolveErrorKey == errorKey
                && tick - lastDropResolveErrorTick < 300)
            {
                return;
            }

            lastDropResolveErrorKey = errorKey;
            lastDropResolveErrorTick = tick;
            Log.Warning(
                "[MAP] MechanoidOvermindDeliveryService.TryResolveTradeDropTarget failed: "
                + ex);
        }

        private static bool TryFillTransporter(
            MechanoidOvermindOrder order,
            ActiveTransporterInfo info)
        {
            IReadOnlyList<MechanoidOvermindOrderLine_Mech> mechLines = order.MechLines;
            for (int i = 0; i < mechLines.Count; i++)
            {
                MechanoidOvermindOrderLine_Mech line = mechLines[i];
                if (line?.Kind == null
                    || line.Count <= 0
                    || line.Count > MechanoidOvermindOrder.MaxCount
                    || MechanoidOvermindCatalogService.IsMechPawnKindBlacklisted(line.Kind))
                {
                    return false;
                }

                for (int n = 0; n < line.Count; n++)
                {
                    Pawn? pawn = GenerateMech(line.Kind);
                    if (pawn == null)
                    {
                        return false;
                    }

                    if (!info.innerContainer.TryAdd(pawn))
                    {
                        if (!pawn.Destroyed)
                        {
                            pawn.Destroy(DestroyMode.Vanish);
                        }

                        return false;
                    }
                }
            }

            IReadOnlyList<MechanoidOvermindOrderLine_Thing> thingLines = order.ThingLines;
            for (int i = 0; i < thingLines.Count; i++)
            {
                MechanoidOvermindOrderLine_Thing line = thingLines[i];
                if (line?.Spec?.Def == null
                    || line.Count <= 0
                    || line.Count > MechanoidOvermindOrder.ThingMaxCount
                    || !MechanoidOvermindCatalogService.TryFindThingCatalogEntry(line.Spec.Def, out _))
                {
                    return false;
                }

                if (!TryAddThings(info, line.Spec, line.Count))
                {
                    return false;
                }
            }

            return info.innerContainer.Count > 0;
        }

        private static Pawn? GenerateMech(PawnKindDef kind)
        {
            try
            {
                Faction? player = Faction.OfPlayerSilentFail;
                if (player == null)
                {
                    return null;
                }

                PawnGenerationRequest request = new PawnGenerationRequest(
                    kind,
                    player,
                    PawnGenerationContext.NonPlayer,
                    null,
                    forceGenerateNewPawn: true,
                    allowDead: false,
                    allowDowned: false,
                    canGeneratePawnRelations: false,
                    mustBeCapableOfViolence: false,
                    1f,
                    forceAddFreeWarmLayerIfNeeded: false,
                    allowGay: true,
                    allowPregnant: false,
                    allowFood: true,
                    allowAddictions: false,
                    inhabitant: false,
                    certainlyBeenInCryptosleep: false,
                    forceRedressWorldPawnIfFormerColonist: false,
                    worldPawnFactionDoesntMatter: false,
                    0f,
                    0f);

                return PawnGenerator.GeneratePawn(request);
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "[MAP] Failed to generate mechanoid " + kind.defName + ": " + ex);
                return null;
            }
        }

        private static bool TryAddThings(
            ActiveTransporterInfo info,
            MechanoidOvermindThingSpec spec,
            int count)
        {
            ThingDef def = spec.Def;
            if (def.category == ThingCategory.Building && def.Minifiable)
            {
                for (int i = 0; i < count; i++)
                {
                    Thing? building = CreateConfiguredThing(spec, 1);
                    if (building == null)
                    {
                        return false;
                    }

                    MinifiedThing? minified = building.MakeMinified();
                    if (minified == null)
                    {
                        if (!building.Destroyed)
                        {
                            building.Destroy(DestroyMode.Vanish);
                        }

                        return false;
                    }

                    if (!info.innerContainer.TryAdd(minified))
                    {
                        if (!minified.Destroyed)
                        {
                            minified.Destroy(DestroyMode.Vanish);
                        }

                        return false;
                    }
                }

                return true;
            }

            int remaining = count;
            while (remaining > 0)
            {
                int stackCount = remaining;
                if (def.stackLimit > 0 && stackCount > def.stackLimit)
                {
                    stackCount = def.stackLimit;
                }

                Thing? thing = CreateConfiguredThing(spec, stackCount);
                if (thing == null)
                {
                    return false;
                }

                if (!info.innerContainer.TryAdd(thing))
                {
                    if (!thing.Destroyed)
                    {
                        thing.Destroy(DestroyMode.Vanish);
                    }

                    return false;
                }

                remaining -= stackCount;
            }

            return true;
        }

        private static Thing? CreateConfiguredThing(MechanoidOvermindThingSpec spec, int stackCount)
        {
            Thing? thing = null;
            try
            {
                if (spec.Def.MadeFromStuff)
                {
                    if (spec.Stuff == null)
                    {
                        return null;
                    }

                    thing = ThingMaker.MakeThing(spec.Def, spec.Stuff);
                }
                else
                {
                    thing = ThingMaker.MakeThing(spec.Def);
                }

                if (thing == null)
                {
                    return null;
                }

                if (spec.HasQuality)
                {
                    CompQuality? qualityComp = thing.TryGetComp<CompQuality>();
                    if (qualityComp == null)
                    {
                        thing.Destroy(DestroyMode.Vanish);
                        return null;
                    }

                    qualityComp.SetQuality(spec.Quality, null);
                }

                if (thing.def.useHitPoints)
                {
                    thing.HitPoints = thing.MaxHitPoints;
                }

                if (stackCount > 1)
                {
                    thing.stackCount = stackCount;
                }

                return thing;
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "[MAP] Failed to create thing " + spec.Def.defName + ": " + ex);
                if (thing != null && !thing.Destroyed)
                {
                    thing.Destroy(DestroyMode.Vanish);
                }

                return null;
            }
        }

        private static void CleanupUndelivered(ActiveTransporterInfo? info)
        {
            try
            {
                if (info?.innerContainer == null || !info.innerContainer.Any)
                {
                    return;
                }

                info.innerContainer.ClearAndDestroyContents(DestroyMode.Vanish);
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "[MAP] MechanoidOvermindDeliveryService.CleanupUndelivered failed: " + ex);
            }
        }
    }
}
