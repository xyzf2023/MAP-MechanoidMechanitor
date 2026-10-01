using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor
{
    /// <summary>先恢复由本 MOD 暂存的真实实体，成功后再解除持久行为和移除组件。</summary>
    internal static class ModUninstallPreparation
    {
        private const string Key = "MAP_MechanoidMechanitor.Uninstall.";
        private const string PackageId = "xyzf.mechanoidmechanitor";
        private static readonly AccessTools.FieldRef<Caravan_PathFollower, CaravanArrivalAction>
            CaravanArrival = AccessTools.FieldRefAccess<Caravan_PathFollower, CaravanArrivalAction>("arrivalAction");
        private static readonly AccessTools.FieldRef<LetterStack, List<Letter>>
            QueuedLetters = AccessTools.FieldRefAccess<LetterStack, List<Letter>>("letterQueue");

        private static bool OwnType(Type? type) => GameComponent_ModUninstallPreparation.OwnType(type);
        private static bool OwnDef(Def? def) => def != null
            && string.Equals(def.modContentPack?.PackageIdPlayerFacing, PackageId, StringComparison.OrdinalIgnoreCase);

        internal static void Execute()
        {
            Game? game = Current.Game;
            GameComponent_ModUninstallPreparation? state =
                game?.GetComponent<GameComponent_ModUninstallPreparation>();
            if (game == null || state == null || GameComponent_ModUninstallPreparation.IsExecuting)
                return;
            Find.TickManager.Pause();
            if (GameComponent_ModUninstallPreparation.IsPrepared)
            {
                ShowCompletion();
                return;
            }

            var failures = new List<string>();
            GameComponent_ModUninstallPreparation.IsExecuting = true;
            try
            {
                // 每阶段都以集合快照遍历。恢复失败时停止后续清理，凭据仍可保存和重试。
                foreach (WorldObject_MechanicalFlyingCaravan flight in
                    Find.WorldObjects.AllWorldObjects.OfType<WorldObject_MechanicalFlyingCaravan>().ToArray())
                    Attempt(failures, "WorldFlight", flight.Label, flight.TryLandForUninstall);
                if (failures.Count > 0) return;

                foreach (MechanicalFlightAuthorizationRecord record in
                    GameComponent_MechanicalFlightRegistry.GetAuthorizationRecordSnapshot())
                    Attempt(failures, "MapFlight", record.Pawn?.LabelShort ?? string.Empty, () => GroundFlight(record));
                if (failures.Count > 0) return;

                foreach (MechFusionSession session in GameComponent_MechFusionSessionRegistry.GetSessionsForReading().ToArray())
                    Attempt(failures, "Fusion", session.SourcePawn?.LabelShort ?? string.Empty, () =>
                        session.TeardownCompleted || MechFusionTeardownService.TryTeardown(session, MechFusionExitReason.LoadRepair, force: true));
                if (GameComponent_MechFusionSessionRegistry.HasAnySession && failures.Count == 0)
                    failures.Add((Key + "FusionRemaining").Translate());
                if (failures.Count > 0) return;

                GameComponent_MechBuildingConversionQueue? queue = game.GetComponent<GameComponent_MechBuildingConversionQueue>();
                if (queue != null)
                    Attempt(failures, "Building", string.Empty, queue.PrepareForUninstall);
                foreach (MechTransformationRecord record in GameComponent_MechTransformationRegistry.GetRecordSnapshot())
                {
                    if (record.CurrentForm == MechTransformationForm.Building && record.ExternalCarrier != null
                        && !record.ExternalCarrier.Destroyed)
                        Attempt(failures, "Building", record.SourcePawn?.LabelShort ?? string.Empty,
                            () => MechBuildingConversionService.TryRestoreForUninstall(record.ExternalCarrier));
                }
                foreach (MechTransformationRecord record in GameComponent_MechTransformationRegistry.GetRecordSnapshot())
                    if (record.CurrentForm != MechTransformationForm.Pawn || record.TransitionInProgress || record.ExternalCarrier != null)
                        failures.Add((Key + "FormRemaining").Translate(record.SourcePawn?.LabelShort ?? string.Empty));
                if (failures.Count > 0) return;

                List<Thing> things = CollectThings();
                List<Pawn> pawns = CollectPawns(things);
                CaptureAffectedMechs(state, pawns);
                // 包括本 MOD 容器和玩家 MOD Pawn 所携带的原版物品/人员。
                RescueContents(things, failures);
                if (failures.Count > 0) return;

                CleanupQuests(failures);
                ReplaceLords(failures);
                foreach (Pawn pawn in pawns)
                    Attempt(failures, "Pawn", pawn.LabelShort, () => CleanupPawn(pawn));
                if (failures.Count > 0) return;

                PrepareWorldObjects(state, failures);
                CleanupGlobalState(failures);
                if (failures.Count > 0) return;

                // 已解除机械师资格的原版机械体若仍有控制下属，必须断开原版不支持的网络。
                foreach (Pawn pawn in state.AffectedControllers.ToArray())
                    Attempt(failures, "Control", pawn?.LabelShort ?? string.Empty, () => DetachInvalidMechanitor(pawn));
                if (failures.Count > 0) return;

                // 暂时关闭缓存资格查询；所有最终操作成功之后才移除真实组件。
                state.BeginFinalization();
                foreach (Pawn pawn in pawns.Where(p => !p.Dead && !p.Destroyed && !OwnDef(p.def)))
                    PawnComponentsUtility.AddAndRemoveDynamicComponents(pawn);
                ReassignOriginalMechs(state, pawns);
                ValidateRemainingDependencies(pawns, failures);
                if (failures.Count > 0)
                {
                    state.CancelPrepared();
                    return;
                }
                state.MarkPrepared();
            }
            catch (Exception exception)
            {
                state.CancelPrepared();
                failures.Add((Key + "UnexpectedFailure").Translate());
                Log.Error("[MAP-机械族机械师] 卸载准备异常：" + exception);
            }
            finally
            {
                GameComponent_ModUninstallPreparation.IsExecuting = false;
                Find.TickManager.Pause();
                if (failures.Count > 0)
                {
                    Find.WindowStack.Add(new Dialog_MessageBox((Key + "Failed").Translate(
                        string.Join("\n", failures.Distinct()))));
                }
                else if (GameComponent_ModUninstallPreparation.IsPrepared)
                {
                    state.AffectedMechs.Clear();
                    ShowCompletion();
                }
            }
        }

        internal static void ShowCompletion()
        {
            int unassigned = Current.Game?.GetComponent<GameComponent_ModUninstallPreparation>()?.UnassignedMechs ?? 0;
            string details = unassigned > 0 ? (Key + "UnassignedMechs").Translate(unassigned).ToString() : string.Empty;
            Find.WindowStack.Add(new Dialog_MessageBox((Key + "Completed").Translate(details)));
        }

        private static void Attempt(List<string> failures, string step, string subject, Func<bool> action)
        {
            try
            {
                if (!action())
                    failures.Add((Key + "StepFailed").Translate((Key + "Step." + step).Translate(), subject));
            }
            catch (Exception exception)
            {
                failures.Add((Key + "StepFailed").Translate((Key + "Step." + step).Translate(), subject));
                Log.Error("[MAP-机械族机械师] 卸载准备阶段失败，保留未完成对象：" + step + " / " + subject + "\n" + exception);
            }
        }

        private static bool GroundFlight(MechanicalFlightAuthorizationRecord record)
        {
            Pawn? pawn = record.Pawn;
            if (pawn?.flight != null && MechanicalFlightUtility.GetVanillaFlightState(pawn) != MechanicalFlightUtility.VanillaFlightState.Grounded)
            {
                if (pawn.Spawned && pawn.Map != null && !pawn.Dead)
                {
                    IntVec3 cell = IntVec3.Invalid;
                    foreach (IntVec3 candidate in pawn.Map.AllCells.OrderBy(c => c.DistanceToSquared(pawn.Position)))
                        if (MechanicalFlightUtility.IsBaseLandingCellValid(candidate, pawn, pawn.Map))
                        {
                            cell = candidate;
                            break;
                        }
                    if (!cell.IsValid) return false;
                    pawn.pather?.StopDead();
                    MechanicalFlightStraightPathPatch.ClearMotion(pawn);
                    record.Phase = MechanicalFlightPhase.Landing;
                    record.PendingExitMap = null;
                    record.PendingShutdownAfterLanding = false;
                    pawn.Position = cell;
                    if (pawn.CurJob != null) pawn.CurJob.flying = false;
                    pawn.flight.ForceLand();
                    for (int i = 0; i < 60 && MechanicalFlightUtility.GetVanillaFlightState(pawn) != MechanicalFlightUtility.VanillaFlightState.Grounded; i++)
                        pawn.flight.FlightTick();
                }
                else
                {
                    // 离图 Pawn 没有可调用 FlightTick 的地图。只恢复原版 Tracker 的着地数据。
                    FieldInfo field = AccessTools.Field(typeof(Pawn_FlightTracker), "flightState");
                    field.SetValue(pawn.flight, Enum.Parse(field.FieldType, "Grounded"));
                    AccessTools.Field(typeof(Pawn_FlightTracker), "lerpTick").SetValue(pawn.flight, 0);
                    AccessTools.Field(typeof(Pawn_FlightTracker), "flyingTicks").SetValue(pawn.flight, -1);
                }
                if (MechanicalFlightUtility.GetVanillaFlightState(pawn) != MechanicalFlightUtility.VanillaFlightState.Grounded)
                    return false;
            }
            record.PendingExitMap = null;
            record.PendingShutdownAfterLanding = false;
            MechanicalFlightUtility.ClearRuntimeState(record, forceLand: false);
            return true;
        }

        private static List<Thing> CollectThings()
        {
            var result = new HashSet<Thing>();
            foreach (Map map in Find.Maps)
            {
                var things = new List<Thing>();
                ThingOwnerUtility.GetAllThingsRecursively(map, ThingRequest.ForGroup(ThingRequestGroup.Everything), things);
                result.UnionWith(things);
            }
            foreach (IThingHolder holder in Find.WorldObjects.AllWorldObjects.OfType<IThingHolder>())
            {
                var things = new List<Thing>();
                ThingOwnerUtility.GetAllThingsRecursively(holder, things);
                result.UnionWith(things);
            }
            foreach (TransportShip ship in Find.TransportShipManager.AllTransportShips)
                if (ship.shipThing is IThingHolder holder)
                {
                    result.Add(ship.shipThing);
                    var contents = new List<Thing>();
                    ThingOwnerUtility.GetAllThingsRecursively(holder, contents);
                    result.UnionWith(contents);
                }
            foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead.ToArray())
            {
                result.Add(pawn);
                var things = new List<Thing>();
                ThingOwnerUtility.GetAllThingsRecursively(pawn, things);
                result.UnionWith(things);
            }
            return result.Where(t => t != null && !t.Destroyed).ToList();
        }

        private static List<Pawn> CollectPawns(List<Thing> things)
        {
            var result = new HashSet<Pawn>(PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead);
            result.UnionWith(things.OfType<Pawn>());
            result.UnionWith(things.OfType<Corpse>().Select(c => c.InnerPawn));
            result.UnionWith(GameComponent_MechanoidMechanitorRegistry.GetPersistentRecordSnapshot().Select(r => r.Pawn));
            result.UnionWith(GameComponent_AutonomousMechRegistry.GetAuthorizationRecordSnapshot().Select(r => r.Pawn).OfType<Pawn>());
            result.UnionWith(GameComponent_SyntheticCompanionRegistry.GetAuthorizationRecordSnapshot().Select(r => r.Pawn).OfType<Pawn>());
            return result.Where(p => p != null && !p.Discarded).ToList();
        }

        private static void RescueContents(List<Thing> things, List<string> failures)
        {
            foreach (Thing thing in things)
            {
                Thing? parent = ThingOwnerUtility.GetFirstParentThing(thing);
                if (OwnDef(thing.def) || parent == null || !OwnDef(parent.def)
                    || (parent is Pawn && parent.Faction != Faction.OfPlayer && !(thing is Pawn)))
                    continue;
                Attempt(failures, "Contents", thing.Label, () => RescueThing(thing));
            }
        }

        private static bool RescueThing(Thing thing)
        {
            Caravan? caravan = thing.GetCaravan();
            if (caravan != null)
            {
                if (thing is Pawn pawn)
                    return MechanicalFlyingCaravanArrivalAction.TryAddPawnToCaravan(caravan, pawn);
                if (caravan.PawnsListForReading.Any(p => !OwnDef(p.def) && p.inventory != null))
                    return MechanicalFlyingCaravanArrivalAction.TryGiveThingToCaravanPawn(caravan, thing, out _,
                        receiverFilter: p => !OwnDef(p.def));
                // 全员都是即将消失的 MOD Pawn 时，原版物品改放回已加载的玩家基地。
            }
            Map? map = thing.MapHeld ?? Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
            if (map == null) return false;
            IntVec3 near = thing.MapHeld == map ? thing.PositionHeld : map.Center;
            IntVec3 cell = CellFinder.FindNoWipeSpawnLocNear(near, map, thing.def, thing.Rotation, 12);
            if (!cell.IsValid) return false;
            ThingOwner? owner = thing.holdingOwner;
            owner?.Remove(thing);
            try
            {
                if (thing is Pawn pawn && Find.WorldPawns.Contains(pawn))
                    Find.WorldPawns.RemovePawn(pawn);
                GenSpawn.Spawn(thing, cell, map, thing.Rotation, WipeMode.VanishOrMoveAside);
                return thing.Spawned;
            }
            catch
            {
                if (!thing.Spawned && !thing.Destroyed && thing.holdingOwner == null)
                    owner?.TryAdd(thing);
                throw;
            }
        }

        private static void CaptureAffectedMechs(GameComponent_ModUninstallPreparation state, List<Pawn> pawns)
        {
            var affected = new HashSet<Pawn>(state.AffectedMechs.Where(p => p != null));
            var controllers = new HashSet<Pawn>(state.AffectedControllers.Where(p => p != null));
            foreach (var entry in GameComponent_MechanoidMechanitorRegistry.GetPersistentRecordSnapshot())
            {
                Pawn? pawn = entry.Pawn;
                if (pawn == null) continue;
                affected.Add(pawn);
                controllers.Add(pawn);
                if (pawn.mechanitor != null)
                    affected.UnionWith(pawn.mechanitor.ControlledPawns);
            }
            foreach (var record in GameComponent_AutonomousMechRegistry.GetAuthorizationRecordSnapshot())
                if (record.Pawn != null && record.Sources != AutonomousMechAuthorizationSource.None)
                    affected.Add(record.Pawn);
            foreach (Pawn pawn in pawns)
                if (pawn.RaceProps.IsMechanoid && pawn.GetComp<CompNativeMechanoidMechanitor>() != null)
                {
                    affected.Add(pawn);
                    controllers.Add(pawn);
                    if (pawn.mechanitor != null) affected.UnionWith(pawn.mechanitor.ControlledPawns);
                }
            state.AffectedMechs = affected.ToList();
            state.AffectedControllers = controllers.ToList();
        }

        private static bool OwnJob(Job? job) => job != null
            && (OwnDef(job.def) || OwnType(job.def?.driverClass) || OwnType(job.GetType())
                || OwnDef(job.ability?.def) || OwnDef(job.workGiverDef));

        private static bool CleanupPawn(Pawn pawn)
        {
            if (OwnJob(pawn.CurJob) || OwnType(pawn.jobs?.curDriver?.GetType()))
                pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            pawn.jobs?.jobQueue.RemoveAll(pawn, OwnJob);
            GameComponent_MechFusionRegistry.TryUnregisterFromDebug(pawn);
            GameComponent_SyntheticCompanionRegistry.TryRevokeAuthorization(pawn);
            GameComponent_MechanoidMechanitorRegistry.TryUnregisterFromDebug(pawn);
            GameComponent_AutonomousMechRegistry.TryRevokeAuthorization(pawn);
            GameComponent_MechanicalFlightRegistry.TryRevokeAuthorization(pawn);
            if (pawn.health != null)
                foreach (Hediff hediff in pawn.health.hediffSet.hediffs.ToArray())
                    if (OwnDef(hediff.def) || OwnType(hediff.GetType()))
                        pawn.health.RemoveHediff(hediff);
            if (pawn.abilities != null)
                foreach (Ability ability in pawn.abilities.abilities.ToArray())
                    if (OwnDef(ability.def) || OwnType(ability.GetType()))
                        if (!ManagedResearchAbilitySyncUtility.SafeRemoveAbility(pawn, ability.def))
                            return false;
            if (pawn.mindState?.duty != null && OwnDef(pawn.mindState.duty.def))
                pawn.mindState.duty = null;
            if (pawn.mechanitor != null)
                foreach (MechanitorControlGroup group in pawn.mechanitor.controlGroups)
                    if (OwnDef(group.WorkMode)) group.SetWorkMode(MechWorkModeDefOf.Work);
            return !OwnJob(pawn.CurJob)
                && (pawn.health == null || !pawn.health.hediffSet.hediffs.Any(h => OwnDef(h.def) || OwnType(h.GetType())))
                && (pawn.abilities == null || !pawn.abilities.abilities.Any(a => OwnDef(a.def) || OwnType(a.GetType())));
        }

        private static void CleanupQuests(List<string> failures)
        {
            foreach (Quest quest in Find.QuestManager.QuestsListForReading.ToArray())
                Attempt(failures, "Quest", quest.name, () =>
                {
                    if (OwnDef(quest.root))
                    {
                        if (!quest.Historical) quest.End(QuestEndOutcome.Unknown, sendLetter: false, playSound: false);
                        Find.QuestManager.Remove(quest);
                    }
                    else
                    {
                        foreach (QuestPart part in quest.PartsListForReading.Where(p => OwnType(p.GetType())).ToArray())
                        {
                            part.Notify_PreCleanup();
                            part.Cleanup();
                            quest.RemovePart(part);
                        }
                    }
                    return true;
                });
        }

        private static void ReplaceLords(List<string> failures)
        {
            foreach (Map map in Find.Maps)
                foreach (Lord lord in map.lordManager.lords.ToArray())
                {
                    if (!OwnType(lord.LordJob?.GetType()) && lord.CurLordToil != null) continue;
                    Attempt(failures, "Lord", map.Parent.Label, () =>
                    {
                        // 上次 SetJob 已成功但后续初始化抛异常时，重试原版状态图初始化。
                        if (lord.LordJob == null) return false;
                        if (!OwnType(lord.LordJob.GetType()))
                        {
                            lord.SetJob(lord.LordJob);
                            lord.GotoToil(lord.Graph.StartingToil);
                            return lord.CurLordToil != null;
                        }
                        LordJob old = lord.LordJob;
                        LordJob replacement;
                        Type? nativeBase = old.GetType().BaseType;
                        // 对仅扩展原版贸易/守卫/袭击的类型，保留其原版字段。
                        if (nativeBase == typeof(LordJob_TradeWithColony) || nativeBase == typeof(LordJob_DefendBase)
                            || nativeBase == typeof(LordJob_AssaultColony))
                        {
                            replacement = (LordJob)Activator.CreateInstance(nativeBase);
                            foreach (FieldInfo field in nativeBase.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                                field.SetValue(replacement, field.GetValue(old));
                        }
                        else if (lord.faction?.HostileTo(Faction.OfPlayer) == true)
                            replacement = new LordJob_AssaultColony(lord.faction);
                        else if (map.CanEverExit && lord.faction != Faction.OfPlayer)
                            replacement = new LordJob_ExitMapBest(LocomotionUrgency.Jog, canDefendSelf: true);
                        else
                            replacement = new LordJob_DefendPoint(lord.ownedPawns.FirstOrDefault(p => p.Spawned)?.Position ?? map.Center,
                                isCaravanSendable: true, addFleeToil: false);
                        lord.CurLordToil?.Cleanup();
                        lord.SetJob(replacement);
                        lord.GotoToil(lord.Graph.StartingToil);
                        foreach (Pawn pawn in lord.ownedPawns)
                        {
                            pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                            pawn.jobs?.ClearQueuedJobs();
                        }
                        return !OwnType(lord.LordJob.GetType()) && lord.CurLordToil != null;
                    });
                }
        }

        private static void PrepareWorldObjects(GameComponent_ModUninstallPreparation state, List<string> failures)
        {
            var replacements = new Dictionary<MapParent, MapParent>();
            for (int i = 0; i < Math.Min(state.OriginalParents.Count, state.ReplacementParents.Count); i++)
                if (state.OriginalParents[i] != null && state.ReplacementParents[i]?.Destroyed == false)
                    replacements[state.OriginalParents[i]] = state.ReplacementParents[i];
            foreach (WorldObject obj in Find.WorldObjects.AllWorldObjects.ToArray())
            {
                if (!(obj is MapParent parent) || (!OwnType(obj.GetType()) && !OwnDef(obj.def))) continue;
                Attempt(failures, "WorldObject", obj.Label, () =>
                {
                    if (parent.HasMap)
                    {
                        Map map = parent.Map;
                        var replacement = (MapParent)WorldObjectMaker.MakeWorldObject(
                            map.IsPlayerHome ? WorldObjectDefOf.Settlement : WorldObjectDefOf.Camp);
                        replacement.Tile = parent.Tile;
                        replacement.SetFaction(parent.Faction);
                        replacement.questTags = parent.questTags?.ToList();
                        if (replacement is Settlement settlement) settlement.Name = parent.Label;
                        Find.WorldObjects.Add(replacement);
                        // Destroy/PostRemove 会删除旧父对象的地图，必须先更换地图父对象。
                        map.info.parent = replacement;
                        replacements[parent] = replacement;
                        state.OriginalParents.Add(parent);
                        state.ReplacementParents.Add(replacement);
                    }
                    return true;
                });
            }
            if (failures.Count > 0) return;
            foreach (WorldObject obj in Find.WorldObjects.AllWorldObjects.ToArray())
                Attempt(failures, "Travel", obj.Label, () =>
                {
                    if (obj is Caravan caravan && OwnType(CaravanArrival(caravan.pather)?.GetType()))
                        caravan.pather.StopDead();
                    if (obj is TravellingTransporters travelling)
                    {
                        if (travelling.arrivalAction is MAPTransportersArrivalAction_AttackMechHiveNodeShuttle shuttle)
                        {
                            if (!shuttle.TryGetVanillaAction(replacements, out TransportersArrivalAction? action)) return false;
                            travelling.arrivalAction = action;
                        }
                        else if (OwnType(travelling.arrivalAction?.GetType()))
                        {
                            MapParent? target = replacements.Values.FirstOrDefault(p => p.Tile == travelling.destinationTile);
                            if (target?.HasMap == true)
                            {
                                if (!DropCellFinder.TryFindDropSpotNear(target.Map.Center, target.Map, out IntVec3 cell,
                                        allowFogged: true, canRoofPunch: false, maxRadius: Math.Max(target.Map.Size.x, target.Map.Size.z),
                                        allowIndoors: false, mustBeReachableFromCenter: false)) return false;
                                travelling.arrivalAction = new TransportersArrivalAction_LandInSpecificCell(target, cell);
                            }
                            else if (travelling.destinationTile.LayerDef?.canFormCaravans == true
                                && travelling.Pawns.Any(p => !OwnDef(p.def) && !p.Dead && p.RaceProps.Humanlike))
                                travelling.arrivalAction = new TransportersArrivalAction_FormCaravan();
                            else
                            {
                                Map? home = Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
                                if (home == null || !DropCellFinder.TryFindDropSpotNear(home.Center, home, out IntVec3 cell,
                                        allowFogged: true, canRoofPunch: false, maxRadius: Math.Max(home.Size.x, home.Size.z),
                                        allowIndoors: false, mustBeReachableFromCenter: false)) return false;
                                travelling.destinationTile = home.Tile;
                                travelling.arrivalAction = new TransportersArrivalAction_LandInSpecificCell(home.Parent, cell);
                            }
                        }
                        if (!RemapParentFields(travelling.arrivalAction, replacements)) return false;
                    }
                    return true;
                });
            if (failures.Count > 0) return;
            // 原版任务/运输船也可能保存指向这些地图父对象的引用。
            foreach (Quest quest in Find.QuestManager.QuestsListForReading.ToArray())
                Attempt(failures, "Quest", quest.name, () =>
                {
                    // 没有已生成地图可接管的 MOD 目标将被移除，依赖它的任务按未知结果结束。
                    if (quest.PartsListForReading.Any(part => part.QuestLookTargets.Any(target =>
                            target.WorldObject is MapParent parent && (OwnType(parent.GetType()) || OwnDef(parent.def))
                            && !replacements.ContainsKey(parent))))
                    {
                        if (!quest.Historical) quest.End(QuestEndOutcome.Unknown, sendLetter: false, playSound: false);
                        Find.QuestManager.Remove(quest);
                        return true;
                    }
                    return quest.PartsListForReading.All(part => RemapParentFields(part, replacements));
                });
            foreach (TransportShip ship in Find.TransportShipManager.AllTransportShips)
                Attempt(failures, "Travel", ship.shipThing?.Label ?? string.Empty, () =>
                {
                    if (!RemapParentFields(ship.curJob, replacements)) return false;
                    if (AccessTools.Field(typeof(TransportShip), "shipJobs").GetValue(ship) is List<ShipJob> jobs)
                        return jobs.All(job => RemapParentFields(job, replacements));
                    return true;
                });
            if (failures.Count > 0) return;
            foreach (WorldObject obj in Find.WorldObjects.AllWorldObjects.ToArray())
            {
                if (!OwnType(obj.GetType()) && !OwnDef(obj.def)) continue;
                if (obj is TravellingTransporters) continue; // 未恢复的载荷绝不能销毁。
                Attempt(failures, "WorldObject", obj.Label, () =>
                {
                    // 标签已迁移到替代父对象，不再发送旧目标的 Destroyed 信号。
                    if (obj is MapParent parent && replacements.ContainsKey(parent)) obj.questTags = null;
                    obj.Destroy();
                    return true;
                });
            }
        }

        private static bool RemapParentFields(object? value, Dictionary<MapParent, MapParent> replacements)
        {
            if (value == null) return true;
            for (Type? type = value.GetType(); type != null; type = type.BaseType)
                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    object? current = field.GetValue(value);
                    if (current is MapParent old && replacements.TryGetValue(old, out MapParent replacement))
                    {
                        if (!field.FieldType.IsInstanceOfType(replacement)) return false;
                        field.SetValue(value, replacement);
                    }
                    else if (current is GlobalTargetInfo target && target.WorldObject is MapParent targetParent
                        && replacements.TryGetValue(targetParent, out MapParent targetReplacement))
                        field.SetValue(value, new GlobalTargetInfo(targetReplacement));
                    else if (current is List<WorldObject> objects)
                    {
                        for (int i = 0; i < objects.Count; i++)
                            if (objects[i] is MapParent parent && replacements.TryGetValue(parent, out MapParent newParent))
                                objects[i] = newParent;
                    }
                    else if (current is List<MapParent> parents)
                    {
                        for (int i = 0; i < parents.Count; i++)
                            if (parents[i] != null && replacements.TryGetValue(parents[i], out MapParent newParent))
                                parents[i] = newParent;
                    }
                }
            return true;
        }

        private static void CleanupGlobalState(List<string> failures)
        {
            Attempt(failures, "Global", string.Empty, () =>
            {
                foreach (ScenPart part in Current.Game.Scenario.AllParts.Where(p => OwnType(p.GetType()) || OwnDef(p.def)).ToArray())
                    Current.Game.Scenario.RemovePart(part);
                foreach (Letter letter in Find.LetterStack.LettersListForReading.ToArray())
                    if (OwnType(letter.GetType()) || OwnDef(letter.def)) Find.LetterStack.RemoveLetter(letter);
                QueuedLetters(Find.LetterStack).RemoveAll(l => OwnType(l.GetType()) || OwnDef(l.def));
                foreach (IArchivable item in Find.Archive.ArchivablesListForReading.ToArray())
                    if (OwnType(item.GetType()) || (item is Letter letter && OwnDef(letter.def))) Find.Archive.Remove(item);
                var queued = new List<QueuedIncident>();
                foreach (QueuedIncident incident in Find.Storyteller.incidentQueue)
                    if (!OwnDef(incident.FiringIncident.def) && !OwnType(incident.FiringIncident.def?.workerClass)) queued.Add(incident);
                Find.Storyteller.incidentQueue.Clear();
                foreach (QueuedIncident incident in queued) Find.Storyteller.incidentQueue.Add(incident);
                if (OwnDef(Find.Storyteller.def))
                {
                    Find.Storyteller.def = DefDatabase<StorytellerDef>.GetNamed("Cassandra");
                    Find.Storyteller.Notify_DefChanged();
                }
                foreach (Faction faction in Find.FactionManager.AllFactionsListForReading)
                    if (OwnDef(faction.def))
                        // 保留派系对象与关系，避免原版人员的派系引用丢失；不制造重复的唯一机械族派系。
                        faction.def = faction.IsPlayer ? FactionDefOf.PlayerColony
                            : faction.HostileTo(Faction.OfPlayer) ? FactionDefOf.OutlanderRough : FactionDefOf.OutlanderCivil;
                var managers = Find.Maps.Select(m => m.gameConditionManager).ToList();
                managers.Add(Find.World.gameConditionManager);
                foreach (GameConditionManager manager in managers)
                    foreach (GameCondition condition in manager.ActiveConditions.ToArray())
                        if (OwnDef(condition.def) || OwnType(condition.GetType())) condition.End();
                return true;
            });
        }

        private static void ValidateRemainingDependencies(List<Pawn> pawns, List<string> failures)
        {
            foreach (Map map in Find.Maps)
                foreach (Lord lord in map.lordManager.lords)
                    if (OwnType(lord.LordJob?.GetType()) || lord.LordJob == null || lord.CurLordToil == null)
                        failures.Add((Key + "StepFailed").Translate((Key + "Step.Lord").Translate(), map.Parent.Label));
            foreach (WorldObject obj in Find.WorldObjects.AllWorldObjects)
            {
                if (OwnType(obj.GetType()) || OwnDef(obj.def))
                    failures.Add((Key + "StepFailed").Translate((Key + "Step.WorldObject").Translate(), obj.Label));
                if (obj is TravellingTransporters travelling && OwnType(travelling.arrivalAction?.GetType())
                    || obj is Caravan caravan && OwnType(CaravanArrival(caravan.pather)?.GetType()))
                    failures.Add((Key + "StepFailed").Translate((Key + "Step.Travel").Translate(), obj.Label));
            }
            foreach (Quest quest in Find.QuestManager.QuestsListForReading)
                if (OwnDef(quest.root) || quest.PartsListForReading.Any(p => OwnType(p.GetType())))
                    failures.Add((Key + "StepFailed").Translate((Key + "Step.Quest").Translate(), quest.name));
            foreach (Pawn pawn in pawns.Where(p => !OwnDef(p.def)))
                if (OwnJob(pawn.CurJob)
                    || pawn.health?.hediffSet.hediffs.Any(h => OwnDef(h.def) || OwnType(h.GetType())) == true
                    || pawn.abilities?.abilities.Any(a => OwnDef(a.def) || OwnType(a.GetType())) == true
                    || pawn.mechanitor?.controlGroups.Any(g => OwnDef(g.WorkMode)) == true)
                    failures.Add((Key + "StepFailed").Translate((Key + "Step.Pawn").Translate(), pawn.LabelShort));
        }

        private static bool DetachInvalidMechanitor(Pawn? pawn)
        {
            if (pawn == null || pawn.Discarded || pawn.mechanitor == null || !pawn.RaceProps.IsMechanoid)
                return true;
            MAPOverseerAssignmentUtility.DisconnectAllOverseerRelations(pawn);
            pawn.mechanitor = null;
            return true;
        }

        private static void ReassignOriginalMechs(GameComponent_ModUninstallPreparation state, List<Pawn> pawns)
        {
            var controllers = pawns.Where(p => !p.Dead && !p.Destroyed && p.RaceProps.Humanlike
                && p.Faction == Faction.OfPlayer && p.health?.hediffSet.HasHediff(HediffDefOf.MechlinkImplant) == true).ToArray();
            state.UnassignedMechs = 0;
            foreach (Pawn mech in state.AffectedMechs)
            {
                if (mech == null || mech.Dead || mech.Destroyed || OwnDef(mech.def) || !mech.RaceProps.IsMechanoid
                    || mech.Faction != Faction.OfPlayer || mech.OverseerSubject == null) continue;
                if (mech.GetOverseer() is Pawn current && controllers.Contains(current)) continue;
                bool assigned = false;
                foreach (Pawn controller in controllers.OrderByDescending(p => p.MapHeld == mech.MapHeld && p.MapHeld != null
                    || p.GetCaravan() != null && p.GetCaravan() == mech.GetCaravan()))
                {
                    PawnComponentsUtility.AddAndRemoveDynamicComponents(controller);
                    if (controller.mechanitor == null || controller.mechanitor.UsedBandwidth
                        + mech.GetStatValue(StatDefOf.BandwidthCost) > controller.mechanitor.TotalBandwidth) continue;
                    if (MAPOverseerAssignmentUtility.TryAssignActualOverseer(controller, mech)) { assigned = true; break; }
                }
                if (!assigned) state.UnassignedMechs++;
            }
        }
    }
}