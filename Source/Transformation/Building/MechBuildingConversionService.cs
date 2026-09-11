using System;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 建筑转换的唯一写入入口。具体机械体只提供“能力标记”和“建筑 Def 配置”，
    /// 不再各自实现 Pawn 收存、恢复与回滚。
    /// </summary>
    public static class MechBuildingConversionService
    {
        private const int RestoreSearchRadius = 8;

        public static bool CanConvert(Pawn? pawn, out string? failureReason)
        {
            failureReason = null;
            if (pawn == null
                || pawn.Destroyed
                || pawn.Discarded
                || pawn.Dead
                || pawn.Faction == null
                || !pawn.Faction.IsPlayerSafe()
                || !pawn.Spawned
                || pawn.Map == null)
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Transformation.Building.PawnUnavailable".Translate();
                return false;
            }

            if (pawn.Downed)
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Transformation.Building.PawnDowned".Translate();
                return false;
            }

            if (!MechCapabilityUtility.HasCapability(
                    pawn,
                    MechCapabilityIds.BuildingConversion))
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Transformation.Building.NoCapability".Translate();
                return false;
            }

            if (GameComponent_MechTransformationRegistry.TryGetRecord(
                    pawn,
                    out MechTransformationRecord? record)
                && record != null
                && (record.TransitionInProgress
                    || record.CurrentForm != MechTransformationForm.Pawn))
            {
                failureReason = record.TransitionInProgress
                    ? "MAP_MechanoidMechanitor.Transformation.InProgress".Translate()
                    : "MAP_MechanoidMechanitor.Transformation.Building.NotPawnForm".Translate();
                return false;
            }

            if (!MechBuildingConversionProfileUtility.TryGetProfile(
                    pawn,
                    out DefModExtension_MechBuildingConversion? profile)
                || profile == null)
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Transformation.Building.MissingProfile".Translate();
                return false;
            }

            if (!MechBuildingConversionProfileUtility.IsBuildingFormDefValid(
                    profile.buildingFormDef,
                    out failureReason))
            {
                return false;
            }

            ThingDef buildingDef = profile.buildingFormDef!;
            if (buildingDef.MadeFromStuff
                && (profile.buildingStuff == null
                    || !profile.buildingStuff.IsStuff))
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Transformation.Building.InvalidStuff".Translate();
                return false;
            }

            return true;
        }

        public static bool CanRestore(
            Thing? carrier,
            bool allowDestroyedCarrier,
            out string? failureReason)
        {
            failureReason = null;
            if (carrier == null
                || carrier.Discarded
                || (!allowDestroyedCarrier && carrier.Destroyed))
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Transformation.Building.CarrierUnavailable".Translate();
                return false;
            }

            CompMechFormCarrier? carrierComp =
                carrier.TryGetComp<CompMechFormCarrier>();
            Pawn? sourcePawn = carrierComp?.SourcePawn;
            if (carrierComp?.Committed != true
                || carrierComp.CarrierForm != MechTransformationForm.Building
                || sourcePawn == null
                || sourcePawn.Destroyed
                || sourcePawn.Discarded)
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Transformation.Building.BrokenLink".Translate();
                return false;
            }

            if (!GameComponent_MechTransformationRegistry.TryGetRecord(
                    sourcePawn,
                    out MechTransformationRecord? record)
                || record == null
                || record.CurrentForm != MechTransformationForm.Building
                || !ReferenceEquals(record.ExternalCarrier, carrier))
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Transformation.Building.BrokenLink".Translate();
                return false;
            }

            if (record.TransitionInProgress)
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Transformation.InProgress".Translate();
                return false;
            }

            if (!allowDestroyedCarrier
                && (!carrier.Spawned || carrier.Map == null))
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Transformation.Building.CarrierUnavailable".Translate();
                return false;
            }

            if (sourcePawn.Dead)
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Transformation.Building.SourceDead".Translate();
                return false;
            }

            return true;
        }

        internal static bool TryConvert(Pawn pawn, bool sendFailureMessage)
        {
            if (!CanConvert(pawn, out string? failureReason))
            {
                Reject(pawn, failureReason, sendFailureMessage);
                return false;
            }

            if (!MechBuildingConversionProfileUtility.TryGetProfile(
                    pawn,
                    out DefModExtension_MechBuildingConversion? profile)
                || profile == null)
            {
                return false;
            }

            ThingDef buildingDef = profile.buildingFormDef!;
            Map map = pawn.Map;
            IntVec3 originalPosition = pawn.Position;
            Rot4 originalRotation = pawn.Rotation;
            Rot4 buildingRotation = buildingDef.rotatable
                ? originalRotation
                : buildingDef.defaultPlacingRot;
            int searchRadius = Math.Max(0, profile.placementSearchRadius);
            IntVec3 spawnCell = CellFinder.FindNoWipeSpawnLocNear(
                originalPosition,
                map,
                buildingDef,
                buildingRotation,
                searchRadius,
                cell => CanPlaceWithoutWiping(
                    pawn,
                    buildingDef,
                    map,
                    cell,
                    buildingRotation));

            if (!spawnCell.IsValid)
            {
                Reject(
                    pawn,
                    "MAP_MechanoidMechanitor.Transformation.Building.NoPlacement".Translate(),
                    sendFailureMessage);
                return false;
            }

            Thing? building = null;
            bool pawnStored = false;
            bool buildingSpawned = false;
            bool transitionStarted = false;
            try
            {
                building = ThingMaker.MakeThing(
                    buildingDef,
                    buildingDef.MadeFromStuff ? profile.buildingStuff : null);
                CompMechFormCarrier? carrierComp =
                    building.TryGetComp<CompMechFormCarrier>();
                CompMechBuildingForm? buildingComp =
                    building.TryGetComp<CompMechBuildingForm>();
                if (carrierComp == null || buildingComp == null)
                {
                    Reject(
                        pawn,
                        "MAP_MechanoidMechanitor.Transformation.Building.MissingCarrierComp"
                            .Translate(),
                        sendFailureMessage);
                    return false;
                }

                if (!GameComponent_MechTransformationRegistry.TryBeginTransition(
                        pawn,
                        MechTransformationForm.Building,
                        out _,
                        out failureReason))
                {
                    Reject(pawn, failureReason, sendFailureMessage);
                    return false;
                }

                transitionStarted = true;
                building.SetFaction(pawn.Faction);
                pawn.DeSpawn(DestroyMode.Vanish);
                Find.WorldPawns.PassToWorld(
                    pawn,
                    PawnDiscardDecideMode.KeepForever);
                pawnStored = true;

                GenSpawn.Spawn(
                    building,
                    spawnCell,
                    map,
                    buildingRotation,
                    WipeMode.Vanish);
                buildingSpawned = true;

                if (!GameComponent_MechTransformationRegistry.TryCommitTransition(
                        pawn,
                        building,
                        out failureReason))
                {
                    RollBackConversion(
                        pawn,
                        building,
                        map,
                        originalPosition,
                        originalRotation,
                        pawnStored,
                        buildingSpawned);
                    Reject(pawn, failureReason, sendFailureMessage);
                    return false;
                }

                Messages.Message(
                    "MAP_MechanoidMechanitor.Transformation.Building.Converted"
                        .Translate(pawn.LabelShortCap),
                    building,
                    MessageTypeDefOf.PositiveEvent,
                    historical: false);
                return true;
            }
            catch (Exception ex)
            {
                if (transitionStarted)
                {
                    RollBackConversion(
                        pawn,
                        building,
                        map,
                        originalPosition,
                        originalRotation,
                        pawnStored,
                        buildingSpawned);
                }

                Log.Error(
                    "[MAP-机械族机械师] 建筑转换异常，已尝试恢复原始 Pawn：" +
                    $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{ex}");
                Reject(
                    pawn,
                    "MAP_MechanoidMechanitor.Transformation.Building.UnexpectedFailure"
                        .Translate(),
                    sendFailureMessage);
                return false;
            }
        }

        internal static bool TryRestore(Thing carrier, bool sendFailureMessage)
        {
            if (!CanRestore(
                    carrier,
                    allowDestroyedCarrier: false,
                    out string? failureReason))
            {
                Reject(carrier, failureReason, sendFailureMessage);
                return false;
            }

            CompMechFormCarrier carrierComp =
                carrier.TryGetComp<CompMechFormCarrier>()!;
            Pawn sourcePawn = carrierComp.SourcePawn!;
            Map map = carrier.Map;
            IntVec3 position = carrier.Position;
            Rot4 rotation = carrier.Rotation;

            if (!TryRestorePawn(
                    sourcePawn,
                    carrier,
                    map,
                    position,
                    rotation,
                    emergencyRecovery: false,
                    out Thing? restoredThing,
                    out failureReason))
            {
                Reject(carrier, failureReason, sendFailureMessage);
                return false;
            }

            carrier.Destroy(DestroyMode.Vanish);
            Messages.Message(
                "MAP_MechanoidMechanitor.Transformation.Building.Restored"
                    .Translate(sourcePawn.LabelShortCap),
                restoredThing ?? sourcePawn,
                MessageTypeDefOf.PositiveEvent,
                historical: false);
            return true;
        }

        internal static void TryEmergencyRestore(
            Thing carrier,
            Pawn sourcePawn,
            Map map,
            IntVec3 position,
            Rot4 rotation)
        {
            if (carrier == null
                || sourcePawn == null
                || map == null
                || sourcePawn.Destroyed
                || sourcePawn.Discarded)
            {
                return;
            }

            if (!TryRestorePawn(
                    sourcePawn,
                    carrier,
                    map,
                    position,
                    rotation,
                    emergencyRecovery: true,
                    out Thing? restoredThing,
                    out string? failureReason))
            {
                Log.Error(
                    "[MAP-机械族机械师] 建筑载体销毁后无法恢复原始 Pawn，" +
                    "记录仍被保留以便后续人工恢复：" +
                    $"pawn={sourcePawn.LabelShort}（{sourcePawn.ThingID}），" +
                    $"carrier={carrier.ThingID}，reason={failureReason ?? "未知"}。");
                return;
            }

            Messages.Message(
                "MAP_MechanoidMechanitor.Transformation.Building.EmergencyRestored"
                    .Translate(sourcePawn.LabelShortCap),
                restoredThing ?? sourcePawn,
                MessageTypeDefOf.NegativeEvent,
                historical: false);
        }

        private static bool TryRestorePawn(
            Pawn sourcePawn,
            Thing carrier,
            Map map,
            IntVec3 position,
            Rot4 rotation,
            bool emergencyRecovery,
            out Thing? restoredThing,
            out string? failureReason)
        {
            restoredThing = null;
            failureReason = null;

            bool transitionStarted = emergencyRecovery
                ? GameComponent_MechTransformationRegistry.TryBeginRecoveryToPawn(
                    sourcePawn,
                    carrier,
                    out _,
                    out failureReason)
                : GameComponent_MechTransformationRegistry.TryBeginTransition(
                    sourcePawn,
                    MechTransformationForm.Pawn,
                    out _,
                    out failureReason);
            if (!transitionStarted)
            {
                return false;
            }

            ThingDef spawnDef = sourcePawn.Dead
                ? sourcePawn.RaceProps.corpseDef
                : sourcePawn.def;
            IntVec3 spawnCell = CellFinder.FindNoWipeSpawnLocNear(
                position,
                map,
                spawnDef,
                rotation,
                RestoreSearchRadius);
            if (!spawnCell.IsValid)
            {
                GameComponent_MechTransformationRegistry
                    .TryCancelTransition(sourcePawn);
                failureReason =
                    "MAP_MechanoidMechanitor.Transformation.Building.NoRestorePlacement"
                        .Translate();
                return false;
            }

            bool removedFromWorld = false;
            try
            {
                if (Find.WorldPawns.Contains(sourcePawn))
                {
                    Find.WorldPawns.RemovePawn(sourcePawn);
                    removedFromWorld = true;
                }

                if (sourcePawn.Dead)
                {
                    Corpse? corpse = sourcePawn.Corpse;
                    if (corpse == null)
                    {
                        corpse = (Corpse)ThingMaker.MakeThing(
                            sourcePawn.RaceProps.corpseDef);
                        corpse.InnerPawn = sourcePawn;
                    }

                    restoredThing = GenSpawn.Spawn(
                        corpse,
                        spawnCell,
                        map,
                        rotation,
                        WipeMode.VanishOrMoveAside);
                }
                else
                {
                    restoredThing = GenSpawn.Spawn(
                        sourcePawn,
                        spawnCell,
                        map,
                        rotation,
                        WipeMode.VanishOrMoveAside);
                }

                if (!GameComponent_MechTransformationRegistry.TryCommitTransition(
                        sourcePawn,
                        targetCarrier: null,
                        out failureReason))
                {
                    RollBackRestore(sourcePawn, restoredThing, removedFromWorld);
                    GameComponent_MechTransformationRegistry
                        .TryCancelTransition(sourcePawn);
                    restoredThing = null;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                RollBackRestore(sourcePawn, restoredThing, removedFromWorld);
                GameComponent_MechTransformationRegistry.TryCancelTransition(sourcePawn);
                restoredThing = null;
                failureReason =
                    "MAP_MechanoidMechanitor.Transformation.Building.UnexpectedFailure"
                        .Translate();
                Log.Error(
                    "[MAP-机械族机械师] 恢复建筑形态中的原始 Pawn 时发生异常：" +
                    $"pawn={sourcePawn.LabelShort}（{sourcePawn.ThingID}）：{ex}");
                return false;
            }
        }

        private static bool CanPlaceWithoutWiping(
            Pawn sourcePawn,
            ThingDef buildingDef,
            Map map,
            IntVec3 root,
            Rot4 rotation)
        {
            foreach (IntVec3 cell in GenAdj.CellsOccupiedBy(
                         root,
                         rotation,
                         buildingDef.Size))
            {
                if (!cell.InBounds(map))
                {
                    return false;
                }

                System.Collections.Generic.List<Thing> things =
                    cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing existing = things[i];
                    if (ReferenceEquals(existing, sourcePawn))
                    {
                        continue;
                    }

                    if (existing is Pawn
                        || GenSpawn.SpawningWipes(buildingDef, existing.def))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static void RollBackConversion(
            Pawn pawn,
            Thing? building,
            Map map,
            IntVec3 originalPosition,
            Rot4 originalRotation,
            bool pawnStored,
            bool buildingSpawned)
        {
            if (buildingSpawned && building != null && !building.Destroyed)
            {
                building.Destroy(DestroyMode.Vanish);
            }

            if (pawnStored && !pawn.Spawned && !pawn.Destroyed)
            {
                if (Find.WorldPawns.Contains(pawn))
                {
                    Find.WorldPawns.RemovePawn(pawn);
                }

                IntVec3 restoreCell = CellFinder.FindNoWipeSpawnLocNear(
                    originalPosition,
                    map,
                    pawn.def,
                    originalRotation,
                    RestoreSearchRadius);
                if (restoreCell.IsValid)
                {
                    GenSpawn.Spawn(
                        pawn,
                        restoreCell,
                        map,
                        originalRotation,
                        WipeMode.VanishOrMoveAside);
                }
                else
                {
                    Find.WorldPawns.PassToWorld(
                        pawn,
                        PawnDiscardDecideMode.KeepForever);
                    Log.Error(
                        "[MAP-机械族机械师] 建筑转换回滚时地图上没有安全位置，" +
                        "原始 Pawn 已保留在 WorldPawns：" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}）。");
                }
            }

            GameComponent_MechTransformationRegistry.TryCancelTransition(pawn);
        }

        private static void RollBackRestore(
            Pawn sourcePawn,
            Thing? restoredThing,
            bool removedFromWorld)
        {
            if (restoredThing != null && restoredThing.Spawned)
            {
                restoredThing.DeSpawn(DestroyMode.Vanish);
            }

            if (!sourcePawn.Destroyed
                && !sourcePawn.Discarded
                && (removedFromWorld || !Find.WorldPawns.Contains(sourcePawn)))
            {
                Find.WorldPawns.PassToWorld(
                    sourcePawn,
                    PawnDiscardDecideMode.KeepForever);
            }
        }

        private static void Reject(
            Thing? target,
            string? failureReason,
            bool sendFailureMessage)
        {
            if (!sendFailureMessage)
            {
                return;
            }

            Messages.Message(
                failureReason
                    ?? "MAP_MechanoidMechanitor.Transformation.Building.Unavailable".Translate(),
                target,
                MessageTypeDefOf.RejectInput,
                historical: false);
        }
    }
}
