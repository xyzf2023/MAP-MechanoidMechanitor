using System;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 建筑转换的唯一写入入口。具体机械体只提供“能力标记”和“建筑 Def 配置”，
    /// 不再各自实现 Pawn 收存、恢复、耐久换算与回滚。
    /// </summary>
    public static class MechBuildingConversionService
    {
        private const int RestoreSearchRadius = 8;
        private const float FractionEpsilon = 0.0001f;

        internal static bool IsActiveBuildingSource(Pawn? pawn)
        {
            if (pawn == null
                || !GameComponent_MechTransformationRegistry.TryGetRecord(
                    pawn,
                    out MechTransformationRecord? record)
                || record == null
                || record.CurrentForm != MechTransformationForm.Building)
            {
                return false;
            }

            Thing? carrier = record.ExternalCarrier;
            CompMechFormCarrier? carrierComp =
                carrier?.TryGetComp<CompMechFormCarrier>();
            return carrierComp?.Committed == true
                && ReferenceEquals(carrierComp.SourcePawn, pawn);
        }

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

            if (MechTransformationUtility.IsTransitionInProgress(pawn))
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Transformation.InProgress".Translate();
                return false;
            }

            if (!MechTransformationUtility.IsInPawnForm(pawn))
            {
                failureReason =
                    "MAP_MechanoidMechanitor.Transformation.Building.NotPawnForm".Translate();
                return false;
            }

            if (!MechBuildingConversionProfileUtility.TryGetProfile(
                    pawn,
                    out CompProperties_MechBuildingConversion? profile)
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
            if (carrierComp?.Committed != true
                || carrierComp.CarrierForm != MechTransformationForm.Building
                || !MechTransformationUtility.TryResolveSourcePawn(
                    carrier,
                    out Pawn? sourcePawn)
                || sourcePawn == null
                || sourcePawn.Destroyed)
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
                    out CompProperties_MechBuildingConversion? profile)
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
            IntVec3 spawnCell = FindBuildingPlacementNear(
                pawn,
                originalPosition,
                map,
                buildingDef,
                buildingRotation,
                searchRadius);

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

                InitializeBuildingDurability(pawn, building, buildingComp);
                buildingComp.CaptureSourceState(pawn);

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
                MechFusionSourceUtility.ApplyDormantGuard(pawn);

                // DeSpawn / PassToWorld / Hediff 回调可能在原落点生成其他 Thing。
                // 必须在不可逆的 WipeMode.Vanish 前复检，而非只依赖转换前的搜索。
                if (!CanPlaceBuildingForm(pawn, buildingDef, map, spawnCell, buildingRotation))
                {
                    RollBackConversion(pawn, building, map, originalPosition,
                        originalRotation, pawnStored, buildingSpawned);
                    Reject(pawn,
                        "MAP_MechanoidMechanitor.Transformation.Building.NoPlacement".Translate(),
                        sendFailureMessage);
                    return false;
                }

                // 原版生成会清除占地内电线，并由建筑自身的传电组件接管该区域。
                // 电线不保存快照；收起建筑或恢复机械体时也不重建。
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

                FleckMaker.ThrowDustPuffThick(
                    building.DrawPos,
                    map,
                    2f,
                    Color.white);
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
            CompMechBuildingForm buildingComp =
                carrier.TryGetComp<CompMechBuildingForm>()!;
            Pawn sourcePawn =
                carrierComp.SourcePawn ?? buildingComp.StoredSourcePawn!;
            buildingComp.EnsureSourceStateForRecovery(sourcePawn);
            Map map = carrier.Map;
            IntVec3 position = carrier.Position;
            Rot4 rotation = carrier.Rotation;

            if (!TryRestorePawn(
                    sourcePawn,
                    carrier,
                    buildingComp.SourceState,
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

            SettleBuildingDurability(
                carrier,
                sourcePawn,
                useDestructionSnapshot: false);
            MechFusionSourceUtility.RemoveDormantGuard(sourcePawn);
            restoredThing = ResolveRestoredThing(sourcePawn, restoredThing);
            carrier.Destroy(DestroyMode.Vanish);
            if (restoredThing != null && restoredThing.Spawned)
            {
                FleckMaker.ThrowDustPuffThick(
                    restoredThing.DrawPos,
                    map,
                    2f,
                    Color.white);
            }

            Messages.Message(
                "MAP_MechanoidMechanitor.Transformation.Building.Restored"
                    .Translate(sourcePawn.LabelShortCap),
                restoredThing ?? sourcePawn,
                MessageTypeDefOf.PositiveEvent,
                historical: false);
            return true;
        }

        internal static bool TryEmergencyRestore(MechBuildingEmergencyRestoreRecord entry)
        {
            Pawn? sourcePawn = entry.SourcePawn;
            if (sourcePawn == null || sourcePawn.Discarded)
            {
                return true;
            }

            if (!GameComponent_MechTransformationRegistry.TryGetRecord(
                    sourcePawn, out MechTransformationRecord? record)
                || record == null
                || record.TransformationId != entry.TransformationId)
            {
                Log.ErrorOnce("[MAP-机械族机械师] 建筑恢复凭据与当前形态身份不一致，已保留快照。",
                    entry.CarrierId ^ 0x4D424549);
                return false;
            }

            // 上次已经提交恢复但通知抛异常时，只移除请求，不再次生成或结算伤势。
            if (record.CurrentForm == MechTransformationForm.Pawn
                && record.ExternalCarrier == null)
            {
                return true;
            }

            Thing? carrier = record.ExternalCarrier;
            if (record.CurrentForm != MechTransformationForm.Building
                || sourcePawn.Spawned || sourcePawn.Destroyed
                || (carrier != null
                    && (!carrier.Destroyed || carrier.thingIDNumber != entry.CarrierId)))
            {
                Log.ErrorOnce("[MAP-机械族机械师] 建筑恢复状态冲突，已保留快照等待处理。",
                    entry.CarrierId ^ 0x4D424553);
                return false;
            }

            // 地图被移除时保留凭据，不擅自选择其他地图或丢弃源 Pawn。
            Map? map = entry.Map;
            if (map == null || map.Disposed || !Find.Maps.Contains(map))
            {
                Log.ErrorOnce("[MAP-机械族机械师] 建筑原地图已移除，已保留源 Pawn 与恢复快照。",
                    entry.CarrierId ^ 0x4D42454D);
                return false;
            }

            if (!TryRestorePawn(
                    sourcePawn,
                    carrier,
                    entry.SourceState,
                    map,
                    entry.Position,
                    entry.Rotation,
                    emergencyRecovery: true,
                    out Thing? restoredThing,
                    out string? failureReason))
            {
                Log.ErrorOnce(
                    "[MAP-机械族机械师] 建筑载体销毁后无法恢复原始 Pawn，将保留快照重试：" +
                    $"pawn={sourcePawn.LabelShort}（{sourcePawn.ThingID}），reason={failureReason ?? "未知"}。",
                    entry.CarrierId ^ 0x4D424546);
                return false;
            }

            if (entry.HasDurabilitySnapshot)
            {
                SettleBuildingDurability(sourcePawn, entry.CarrierId,
                    entry.InitialHealthFraction, entry.CurrentHealthFraction,
                    entry.InitialRepairableDamage);
            }
            else
            {
                Log.Warning(
                    "[MAP-机械族机械师] 建筑形态缺少初始耐久基线，本次紧急恢复跳过损伤与维修结算：" +
                    $"pawn={sourcePawn.LabelShort}（{sourcePawn.ThingID}），carrier={entry.CarrierId}。");
            }

            MechFusionSourceUtility.RemoveDormantGuard(sourcePawn);
            restoredThing = ResolveRestoredThing(sourcePawn, restoredThing);
            if (restoredThing != null && restoredThing.Spawned)
            {
                FleckMaker.ThrowDustPuffThick(restoredThing.DrawPos, map, 2f, Color.white);
            }

            Messages.Message(
                "MAP_MechanoidMechanitor.Transformation.Building.EmergencyRestored"
                    .Translate(sourcePawn.LabelShortCap),
                restoredThing ?? sourcePawn,
                MessageTypeDefOf.NegativeEvent,
                historical: false);
            return true;
        }

        private static void InitializeBuildingDurability(
            Pawn pawn,
            Thing building,
            CompMechBuildingForm buildingComp)
        {
            float pawnIntegrity =
                MechPartDurabilityUtility.GetStructuralIntegrity(pawn);
            int maximum = Math.Max(1, building.MaxHitPoints);
            building.HitPoints = Mathf.Clamp(
                Mathf.RoundToInt(maximum * pawnIntegrity),
                1,
                maximum);
            buildingComp.CaptureInitialHealthFraction(
                (float)building.HitPoints / maximum,
                MechPartDurabilityUtility.GetRepairableStructuralDamage(pawn));
        }

        private static void SettleBuildingDurability(
            Thing carrier,
            Pawn sourcePawn,
            bool useDestructionSnapshot)
        {
            CompMechBuildingForm? buildingComp =
                carrier.TryGetComp<CompMechBuildingForm>();
            if (buildingComp == null
                || !buildingComp.TryGetDurabilityFractions(
                    useDestructionSnapshot,
                    out float initialFraction,
                    out float currentFraction))
            {
                // 旧存档中的建筑没有转换时基线；不能凭空把既有建筑损伤
                // 追溯成 Pawn 伤势，因此安全地跳过一次结算。
                Log.Warning(
                    "[MAP-机械族机械师] 建筑形态缺少初始耐久基线，" +
                    "本次恢复跳过损伤与维修结算：" +
                    $"pawn={sourcePawn.LabelShort}（{sourcePawn.ThingID}），" +
                    $"carrier={carrier.ThingID}。");
                return;
            }

            SettleBuildingDurability(sourcePawn, carrier.thingIDNumber,
                initialFraction, currentFraction, buildingComp.InitialRepairableDamage);
        }

        private static void SettleBuildingDurability(
            Pawn sourcePawn, int carrierId, float initialFraction, float currentFraction,
            float initialRepairableDamage)
        {
            // 两条调用路径都已提交 Pawn 形态并清除载体链接；恢复请求重试不会再次进入结算。
            try
            {
                if (currentFraction > initialFraction)
                {
                    // 以修复掉的初始缺损占比计算额度，而非把当前结构整体放大。
                    // 例如 60% -> 80% 修复初始损伤的 50%；修满则修复全部初始损伤。
                    float repairFraction = Mathf.Clamp01(
                        (currentFraction - initialFraction) / (1f - initialFraction));
                    float repairableDamage = initialRepairableDamage >= 0f
                        ? initialRepairableDamage
                        : MechPartDurabilityUtility.GetRepairableStructuralDamage(sourcePawn);
                    MechPartDurabilityUtility.RepairCurrentPartDurability(
                        sourcePawn, repairableDamage * repairFraction);
                    return;
                }

                if (currentFraction == initialFraction && currentFraction > 0f)
                {
                    return;
                }

                float settlementRatio = initialFraction > FractionEpsilon
                    ? Mathf.Clamp01(currentFraction / initialFraction)
                    : 0f;
                int settlementSeed =
                    MechPartDurabilityUtility.CreateSettlementSeed(
                        sourcePawn,
                        carrierId);
                MechPartDurabilityUtility.SettleCurrentPartDurability(
                    sourcePawn,
                    settlementRatio,
                    settlementSeed);
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 建筑耐久无法安全映射回机械族伤势：" +
                    $"pawn={sourcePawn.LabelShort}（{sourcePawn.ThingID}），" +
                    $"carrier={carrierId}：{ex}");
            }
        }

        private static Thing? ResolveRestoredThing(
            Pawn sourcePawn,
            Thing? restoredThing)
        {
            Corpse? corpse = sourcePawn.Corpse;
            if (corpse != null && !corpse.Destroyed)
            {
                return corpse;
            }

            return !sourcePawn.Destroyed ? sourcePawn : restoredThing;
        }

        private static bool TryRestorePawn(
            Pawn sourcePawn,
            Thing? carrier,
            MechBuildingSourceState buildingState,
            Map map,
            IntVec3 position,
            Rot4 rotation,
            bool emergencyRecovery,
            out Thing? restoredThing,
            out string? failureReason)
        {
            restoredThing = null;
            failureReason = null;

            // 紧急恢复在下一 Tick 执行，此时捕获的地图可能已经移除或释放。
            // 在锁定形态、搜索落点及取出 WorldPawn 之前拒绝失效地图。
            if (map == null || map.Disposed || map.Parent == null
                || Current.Game == null || !Find.Maps.Contains(map))
            {
                failureReason = "原地图已移除，无法在该地图恢复机械体。";
                return false;
            }

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
                if (!buildingState.RestoreSourceIdentity(sourcePawn))
                {
                    Log.Warning(
                        "[MAP-机械族机械师] 建筑形态恢复前已还原源机械族派系，" +
                        "但原监管控制组位置暂时无法精确恢复：" +
                        $"pawn={sourcePawn.LabelShort}（{sourcePawn.ThingID}）。");
                }

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

                // SpawnSetup 及第三方补丁可能再次处理派系、监管关系或 Needs；
                // 生成后做第二次身份复核，再提交转换前保存的权威电量。
                if (!buildingState.RestoreSourceIdentity(sourcePawn))
                {
                    Log.Warning(
                        "[MAP-机械族机械师] 建筑形态恢复后已还原源机械族派系，" +
                        "但原监管控制组位置暂时无法精确恢复：" +
                        $"pawn={sourcePawn.LabelShort}（{sourcePawn.ThingID}）。");
                }

                if (!buildingState.TryWriteBackEnergy(sourcePawn))
                {
                    failureReason =
                        "恢复机械族能源需求失败，已回滚本次建筑形态恢复。";
                    RollBackRestore(sourcePawn, restoredThing, removedFromWorld);
                    GameComponent_MechTransformationRegistry
                        .TryCancelTransition(sourcePawn);
                    restoredThing = null;
                    return false;
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

        private static IntVec3 FindBuildingPlacementNear(
            Pawn sourcePawn,
            IntVec3 origin,
            Map map,
            ThingDef buildingDef,
            Rot4 rotation,
            int searchRadius)
        {
            // 原版无覆盖搜索会把源 Pawn 自身也视为不可通行建筑的障碍。
            // 使用原版按距离排序的偏移（首项为原地），复用允许源 Pawn 和可替换电线的占地检查。
            int count = GenRadial.NumCellsInRadius(Math.Max(0, searchRadius));
            for (int i = 0; i < count; i++)
            {
                IntVec3 candidate = origin + GenRadial.RadialPattern[i];
                if (!candidate.InBounds(map)
                    || (candidate != origin
                        && !GenSight.LineOfSight(origin, candidate, map, skipFirstCell: true))
                    || !CanPlaceBuildingForm(sourcePawn, buildingDef, map, candidate, rotation))
                {
                    continue;
                }

                return candidate;
            }

            // 没有安全位置时明确失败，不回退到未经检查的原位置。
            return IntVec3.Invalid;
        }

        private static bool CanPlaceBuildingForm(
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

                    // 仅传电建筑可以按原版规则接管已建成电线（含隐藏电线）。
                    // 不扩大到其他电力设备、蓝图或施工框架。
                    if (buildingDef.EverTransmitsPower
                        && existing is Building
                        && existing.def.building?.isPowerConduit == true)
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

            return GenConstruct.CanBuildOnTerrain(buildingDef, root, map, rotation);
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

            MechFusionSourceUtility.RemoveDormantGuard(pawn);
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
