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
            "MAP_MechanoidMechanitor.MechHiveCommunication.Error.ConnectionLost";

        public const string ErrorNoMap =
            "MAP_MechanoidMechanitor.MechHiveCommunication.Error.NoMap";

        public const string ErrorNoDropSpot =
            "MAP_MechanoidMechanitor.MechHiveCommunication.Error.NoDropSpot";

        public const string ErrorInvalidOrder =
            "MAP_MechanoidMechanitor.MechHiveCommunication.Error.InvalidOrder";

        public const string ErrorInsufficientCredits =
            "MAP_MechanoidMechanitor.MechHiveCommunication.Error.InsufficientCredits";

        public const string ErrorGenerationFailed =
            "MAP_MechanoidMechanitor.MechHiveCommunication.Error.GenerationFailed";

        public const string ErrorChargeFailed =
            "MAP_MechanoidMechanitor.MechHiveCommunication.Error.ChargeFailed";

        public const string ErrorDropFailed =
            "MAP_MechanoidMechanitor.MechHiveCommunication.Error.DropFailed";

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

            Map? map = ResolveDeliveryMap(preferredMap);
            if (map == null)
            {
                return MechanoidOvermindDeliveryResult.Failed(ErrorNoMap);
            }

            IntVec3 dropCell = DropCellFinder.TradeDropSpot(map);
            if (!dropCell.IsValid)
            {
                return MechanoidOvermindDeliveryResult.Failed(ErrorNoDropSpot);
            }

            MechanoidOvermindPricingService.ClearThingMarketValueCache();
            if (!order.TryGetCosts(out _, out _, out int totalCost) || totalCost < 0)
            {
                return MechanoidOvermindDeliveryResult.Failed(ErrorInvalidOrder);
            }

            if (totalCost
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
            bool delivered = false;
            try
            {
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
                        totalCost))
                {
                    CleanupUndelivered(info);
                    return MechanoidOvermindDeliveryResult.Failed(ErrorChargeFailed);
                }

                spent = true;

                if (!MechanoidMechanitorMechHiveCommunicationUtility.TryGetContactableMechHive(
                        out _))
                {
                    GameComponent_MechanoidMechanitorStoryState.RefundPurgeDirectiveCredits(
                        totalCost);
                    spent = false;
                    CleanupUndelivered(info);
                    return MechanoidOvermindDeliveryResult.Failed(ErrorConnection);
                }

                DropPodUtility.MakeDropPodAt(dropCell, map, info);
                delivered = true;
                return MechanoidOvermindDeliveryResult.Succeeded(totalCost);
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP] MechanoidOvermindDeliveryService.TryDeliver failed: " + ex);
                if (delivered)
                {
                    // MakeDropPodAt 已接管容器，不得退款或清理空投内容。
                    return MechanoidOvermindDeliveryResult.Succeeded(totalCost);
                }

                if (spent)
                {
                    GameComponent_MechanoidMechanitorStoryState.RefundPurgeDirectiveCredits(
                        totalCost);
                }

                CleanupUndelivered(info);
                return MechanoidOvermindDeliveryResult.Failed(
                    spent ? ErrorDropFailed : ErrorGenerationFailed);
            }
        }

        private static Map? ResolveDeliveryMap(Map? preferredMap)
        {
            if (IsUsableMap(preferredMap))
            {
                return preferredMap;
            }

            if (IsUsableMap(Find.CurrentMap))
            {
                return Find.CurrentMap;
            }

            Map? homeMap = Find.AnyPlayerHomeMap;
            if (IsUsableMap(homeMap))
            {
                return homeMap;
            }

            List<Map> maps = Find.Maps;
            if (maps == null)
            {
                return null;
            }

            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (IsUsableMap(map) && map.ParentFaction != null && map.ParentFaction.IsPlayer)
                {
                    return map;
                }
            }

            return null;
        }

        private static bool IsUsableMap(Map? map)
        {
            return map != null && !map.Disposed;
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
                    || line.Count > MechanoidOvermindOrder.MaxCount)
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
                    || line.Count > MechanoidOvermindOrder.MaxCount)
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
            if (info?.innerContainer == null || !info.innerContainer.Any)
            {
                return;
            }

            info.innerContainer.ClearAndDestroyContents(DestroyMode.Vanish);
        }
    }
}
