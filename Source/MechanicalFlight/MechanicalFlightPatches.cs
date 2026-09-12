using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    internal static class MechanicalFlightGizmoPatch
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            bool emergency = MechanicalFlightEmergencyUtility.IsEmergencySequence(__instance);
            foreach (Gizmo gizmo in __result)
            {
                if (emergency && gizmo is Command command)
                {
                    command.Disable("MAP_MechanicalFlight_EmergencyLandingBlocked".Translate());
                }
                yield return gizmo;
            }
            if (__instance != null && __instance.Faction == Faction.OfPlayer
                && __instance.Drafted
                && GameComponent_MechanicalFlightRegistry.IsAuthorized(__instance))
            {
                yield return MechanicalFlightUtility.MakeCommand(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_FlightTracker), nameof(Pawn_FlightTracker.CanEverFly),
        MethodType.Getter)]
    internal static class MechanicalFlightCanEverFlyPatch
    {
        private static readonly AccessTools.FieldRef<Pawn_FlightTracker, Pawn> PawnField =
            AccessTools.FieldRefAccess<Pawn_FlightTracker, Pawn>("pawn");
        public static void Postfix(Pawn_FlightTracker __instance, ref bool __result)
        {
            if (!__result)
            {
                __result = MechanicalFlightUtility.CanEverFlyWithAuthorization(
                    PawnField(__instance));
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_FlightTracker), nameof(Pawn_FlightTracker.MaxFlightTicks),
        MethodType.Getter)]
    internal static class MechanicalFlightMaxTicksPatch
    {
        private static readonly AccessTools.FieldRef<Pawn_FlightTracker, Pawn> PawnField =
            AccessTools.FieldRefAccess<Pawn_FlightTracker, Pawn>("pawn");
        public static void Postfix(Pawn_FlightTracker __instance, ref int __result)
        {
            if (MechanicalFlightUtility.UsesAerialMovement(PawnField(__instance)))
            {
                __result = int.MaxValue;
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_FlightTracker), nameof(Pawn_FlightTracker.Notify_JobStarted))]
    internal static class MechanicalFlightJobPatch
    {
        private static readonly AccessTools.FieldRef<Pawn_FlightTracker, Pawn> PawnField =
            AccessTools.FieldRefAccess<Pawn_FlightTracker, Pawn>("pawn");
        private static readonly HashSet<string> HoverCompatibleJobs = new()
        {
            "Goto", "GotoWander", "Follow", "FollowClose", "Wait", "Wait_Combat",
            "Wait_MaintainPosture", "StandAndStare", "AttackStatic",
            "UseVerbOnThing", "UseVerbOnThingStatic", "UseVerbOnThingStaticReserve",
            "HaulToCell", "HaulToContainer", "PickupToHold", "TakeInventory"
        };

        public static bool Prefix(Pawn_FlightTracker __instance, Job job)
        {
            Pawn pawn = PawnField(__instance);
            if (!GameComponent_MechanicalFlightRegistry.IsAuthorized(pawn))
            {
                return true;
            }
            if (job == null)
            {
                return false;
            }
            if (MechanicalFlightEmergencyUtility.IsEmergencySequence(pawn))
            {
                job.flying = GameComponent_MechanicalFlightRegistry.TryGetRecord(
                    pawn, out var emergencyRecord)
                    && emergencyRecord?.Phase == MechanicalFlightPhase.EmergencyApproach;
                return false;
            }
            if (!MechanicalFlightUtility.IsActivelyFlying(pawn))
            {
                job.flying = false;
                return false;
            }

            if (IsMeleeOrTouch(job))
            {
                // 飞行状态不因近战或接触任务自动降落，也不允许其借用飞行移动。
                job.flying = true;
                return false;
            }

            if (IsHoverCompatible(job))
            {
                job.flying = true;
                return false;
            }

            if (MechanicalFlightUtility.TryBeginLanding(pawn))
            {
                job.flying = false;
            }
            else
            {
                // 无法降落：不能悬停在非法位置执行贴地任务，安全结束该任务。
                job.flying = false;
                MechanicalFlightUtility.NotifyGroundJobBlocked(pawn, job.playerForced);
                pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
            }
            return false;
        }

        internal static bool IsMeleeOrTouch(Job? job)
        {
            return job?.def != null
                && (job.def.defName == "AttackMelee"
                    || job.def.defName == "CastAbilityTouch"
                    || job.verbToUse?.IsMeleeAttack == true);
        }

        internal static bool IsHoverCompatible(Job? job)
        {
            return job?.def != null && !IsMeleeOrTouch(job)
                && (job.def.ifFlyingKeepFlying
                    || HoverCompatibleJobs.Contains(job.def.defName));
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob))]
    internal static class MechanicalFlightUnreachableOrderPatch
    {
        private static readonly AccessTools.FieldRef<Pawn_JobTracker, Pawn> PawnField =
            AccessTools.FieldRefAccess<Pawn_JobTracker, Pawn>("pawn");

        public static bool Prefix(Pawn_JobTracker __instance, Job job, ref bool __result)
        {
            Pawn pawn = PawnField(__instance);
            if (pawn == null || job == null)
            {
                return true;
            }
            if (MechanicalFlightEmergencyUtility.IsEmergencySequence(pawn))
            {
                __result = false;
                return false;
            }
            // 近战/接触任务在进入原版预留检查前拒绝，避免原版 Warning/Errored 链。
            if (MechanicalFlightJobPatch.IsMeleeOrTouch(job)
                && (MechanicalFlightUtility.IsAirborne(pawn)
                    || (job.targetA.Thing is Pawn targetPawn
                        && MechanicalFlightUtility.IsAirborne(targetPawn))))
            {
                __result = false;
                return false;
            }
            if (!MechanicalFlightUtility.IsActivelyFlying(pawn))
            {
                return true;
            }
            if (MechanicalFlightJobPatch.IsMeleeOrTouch(job))
            {
                pawn.pather?.StopDead();
                __result = false;
                return false;
            }
            if (MechanicalFlightJobPatch.IsHoverCompatible(job)
                || !job.targetA.IsValid)
            {
                return true;
            }

            LocalTargetInfo target = job.targetA;
            if (target.HasThing && target.Thing.MapHeld != pawn.MapHeld)
            {
                __result = false;
                return false;
            }
            if (pawn.CanReach(target, PathEndMode.Touch, Danger.Deadly))
            {
                if (MechanicalFlightUtility.TryBeginLanding(pawn))
                {
                    return true;
                }
                // 地面路径可达但当前格无法安全降落：拒绝命令并给出一次提示。
                MechanicalFlightUtility.NotifyGroundJobBlocked(pawn, true);
                pawn.pather?.StopDead();
                __result = false;
                return false;
            }

            pawn.pather?.StopDead();
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    internal static class MechanicalFlightJobStartPatch
    {
        private static readonly AccessTools.FieldRef<Pawn_JobTracker, Pawn> PawnField =
            AccessTools.FieldRefAccess<Pawn_JobTracker, Pawn>("pawn");

        public static bool Prefix(Pawn_JobTracker __instance, Job newJob)
        {
            Pawn pawn = PawnField(__instance);
            if (pawn == null || newJob == null)
            {
                return true;
            }

            // 紧急状态仅允许机械飞行自身的迫降 Job 启动。
            if (MechanicalFlightEmergencyUtility.IsEmergencySequence(pawn))
            {
                return newJob.def
                    == MAPMechanitor_JobDefOf.MAP_MechanicalFlightEmergencyLanding;
            }

            // 近战/接触任务分配层的最后防线：不允许进入原版预留失败告警链。
            if (MechanicalFlightJobPatch.IsMeleeOrTouch(newJob))
            {
                if (MechanicalFlightUtility.IsAirborne(pawn))
                {
                    return false;
                }
                if (newJob.targetA.Thing is Pawn targetPawn
                    && MechanicalFlightUtility.IsAirborne(targetPawn))
                {
                    return false;
                }
                return true;
            }

            if (!MechanicalFlightUtility.IsActivelyFlying(pawn))
            {
                return true;
            }
            if (MechanicalFlightJobPatch.IsHoverCompatible(newJob))
            {
                return true;
            }

            if (MechanicalFlightUtility.TryBeginLanding(pawn))
            {
                return true;
            }
            MechanicalFlightUtility.NotifyGroundJobBlocked(pawn, newJob.playerForced);
            return false;
        }
    }

    [HarmonyPatch(typeof(AttackTargetFinder),
        nameof(AttackTargetFinder.BestAttackTarget))]
    internal static class MechanicalFlightMeleeTargetPatch
    {
        public static void Prefix(IAttackTargetSearcher searcher,
            ref System.Predicate<Thing> validator)
        {
            if (searcher?.CurrentEffectiveVerb?.IsMeleeAttack != true)
            {
                return;
            }

            bool airborneSearcher = searcher.Thing is Pawn searcherPawn
                && MechanicalFlightUtility.IsAirborne(searcherPawn);
            System.Predicate<Thing>? originalValidator = validator;
            validator = target =>
                !airborneSearcher
                && !(target is Pawn targetPawn
                    && MechanicalFlightUtility.IsAirborne(targetPawn))
                && (originalValidator == null || originalValidator(target));
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), "SetNewPathRequest")]
    internal static class MechanicalFlightStraightPathPatch
    {
        private sealed class DirectFlightMotionState
        {
            public Vector3 ExactGroundPosition;
            public Vector3 DestinationGroundPosition;
            public float TotalDistance;
            public LocalTargetInfo SourceTarget;
            public PathEndMode SourcePathEndMode;
            public IntVec3 ResolvedDestination;
            public IntVec3 LastTargetPosition;
            public CellRect LastTargetOccupiedRect;
            public int NextDestinationValidationTick;
        }

        private const int DestinationValidationIntervalTicks = 30;

        private static readonly Dictionary<Pawn, DirectFlightMotionState>
            DirectMotionStates = new();

        private static readonly AccessTools.FieldRef<Pawn_PathFollower, Pawn> PawnField =
            AccessTools.FieldRefAccess<Pawn_PathFollower, Pawn>("pawn");
        private static readonly AccessTools.FieldRef<Pawn_PathFollower, LocalTargetInfo>
            DestinationField = AccessTools.FieldRefAccess<Pawn_PathFollower, LocalTargetInfo>(
                "destination");
        private static readonly AccessTools.FieldRef<PawnPath, int> CurNodeIndexField =
            AccessTools.FieldRefAccess<PawnPath, int>("curNodeIndex");
        private static readonly AccessTools.FieldRef<Pawn_PathFollower, bool> MovingField =
            AccessTools.FieldRefAccess<Pawn_PathFollower, bool>("moving");
        private static readonly AccessTools.FieldRef<Pawn_PathFollower, PathEndMode> PathEndModeField =
            AccessTools.FieldRefAccess<Pawn_PathFollower, PathEndMode>("peMode");
        private static readonly AccessTools.FieldRef<Pawn_PathFollower, float>
            CachedMovePercentageField =
                AccessTools.FieldRefAccess<Pawn_PathFollower, float>("cachedMovePercentage");
        private static readonly AccessTools.FieldRef<Pawn_PathFollower, bool>
            CachedCollisionField =
                AccessTools.FieldRefAccess<Pawn_PathFollower, bool>("cachedWillCollideNextCell");
        private static readonly AccessTools.FieldRef<Pawn_PathFollower, IntVec3> LastCellField =
            AccessTools.FieldRefAccess<Pawn_PathFollower, IntVec3>("lastCell");
        private static readonly AccessTools.FieldRef<Pawn_PathFollower, int>
            LastEnteredCellTickField =
                AccessTools.FieldRefAccess<Pawn_PathFollower, int>("lastEnteredCellTick");
        private static readonly AccessTools.FieldRef<Pawn_PathFollower, int> LastMovedTickField =
            AccessTools.FieldRefAccess<Pawn_PathFollower, int>("lastMovedTick");

        public static bool Prefix(Pawn_PathFollower __instance)
        {
            Pawn pawn = PawnField(__instance);
            if (!IsActive(pawn))
            {
                return true;
            }

            // 统一转发到完整的直线移动初始化入口，补齐 moving/posture/缓存/旧路径释放。
            TryStartDirectPath(
                __instance, pawn, DestinationField(__instance), PathEndModeField(__instance));
            return false;
        }

        internal static bool IsActive(Pawn? pawn)
        {
            return pawn?.Map != null && MechanicalFlightUtility.UsesAerialMovement(pawn);
        }

        internal static bool TryGetExactGroundDrawPos(Pawn pawn, out Vector3 drawPos)
        {
            if (IsActive(pawn) && pawn.pather?.MovingNow == true
                && DirectMotionStates.TryGetValue(
                    pawn, out DirectFlightMotionState? state))
            {
                drawPos = state.ExactGroundPosition;
                return true;
            }

            drawPos = default;
            return false;
        }

        internal static bool TryGetDirectPathDrawData(
            Pawn? pawn,
            out Vector3 currentGroundPosition,
            out Vector3 destinationGroundPosition)
        {
            if (pawn != null && IsActive(pawn)
                && pawn.pather?.MovingNow == true
                && DirectMotionStates.TryGetValue(
                    pawn, out DirectFlightMotionState? state))
            {
                currentGroundPosition = state.ExactGroundPosition;
                destinationGroundPosition = state.DestinationGroundPosition;
                return true;
            }

            currentGroundPosition = default;
            destinationGroundPosition = default;
            return false;
        }

        internal static void ClearMotion(Pawn? pawn)
        {
            if (pawn != null)
            {
                DirectMotionStates.Remove(pawn);
            }
        }

        internal static void ClearAllMotion()
        {
            DirectMotionStates.Clear();
        }

        private static Vector3 CurrentGroundPosition(Pawn pawn)
        {
            return TryGetExactGroundDrawPos(pawn, out Vector3 exact)
                ? exact
                : pawn.Position.ToVector3Shifted();
        }

        internal static bool TryStartDirectPath(
            Pawn_PathFollower pather,
            Pawn pawn,
            LocalTargetInfo destination,
            PathEndMode pathEndMode)
        {
            if (pawn.Map == null || !destination.IsValid
                || destination.ThingDestroyed
                || (destination.HasThing && destination.Thing.MapHeld != pawn.MapHeld)
                || !TryResolveDirectDestination(
                    pawn, destination, ref pathEndMode, out IntVec3 destinationCell))
            {
                NotifyFailed(pather, pawn);
                return false;
            }

            Vector3 exactStart = CurrentGroundPosition(pawn);
            pather.StopDead();
            DestinationField(pather) = destination;
            PathEndModeField(pather) = pathEndMode;
            pather.lastPathedTargetPosition = destinationCell;
            if (pawn.Position == destinationCell)
            {
                NotifyArrived(pather, pawn);
                return true;
            }

            MovingField(pather) = true;
            pawn.jobs.posture = PawnPosture.Standing;
            CachedMovePercentageField(pather) = 0f;
            CachedCollisionField(pather) = false;
            pather.curPathJobIsStale = false;
            AssignDirectPath(
                pather, pawn, destinationCell, exactStart, destination, pathEndMode);
            return true;
        }

        // 按原版 PathEndMode 语义解析直线飞行的实际终点格：
        // OnCell 直接使用目标格；Touch/ClosestTouch 在目标占用区扩展 1 格内选择
        // 最近的可通行接触格，避免停进工作台、容器、墙等目标 Thing 的占用格。
        private static bool TryResolveDirectDestination(
            Pawn pawn,
            LocalTargetInfo destination,
            ref PathEndMode pathEndMode,
            out IntVec3 result)
        {
            result = IntVec3.Invalid;
            Map? map = pawn.Map;
            if (map == null || !destination.IsValid)
            {
                return false;
            }

            TargetInfo targetInfo = destination.ToTargetInfo(map);
            targetInfo = GenPath.ResolvePathMode(pawn, targetInfo, ref pathEndMode);
            LocalTargetInfo resolved = (LocalTargetInfo)targetInfo;
            if (!resolved.IsValid)
            {
                return false;
            }

            if (pathEndMode == PathEndMode.OnCell || pathEndMode == PathEndMode.None)
            {
                result = resolved.Cell;
                return result.IsValid && result.InBounds(map);
            }

            CellRect occupied = resolved.HasThing
                ? resolved.Thing.OccupiedRect()
                : CellRect.SingleCell(resolved.Cell);
            CellRect candidates = occupied.ExpandedBy(1).ClipInsideMap(map);
            float bestDistance = float.MaxValue;
            IntVec3 best = IntVec3.Invalid;
            foreach (IntVec3 cell in candidates)
            {
                if (!cell.WalkableBy(map, pawn)
                    || !TouchPathEndModeUtility.IsAdjacentOrInsideAndAllowedToTouch(
                        cell, resolved, map.pathing.For(pawn)))
                {
                    continue;
                }
                float distance = cell.DistanceToSquared(pawn.Position);
                if (!best.IsValid || distance < bestDistance
                    || (Mathf.Approximately(distance, bestDistance)
                        && CellComesFirst(cell, best)))
                {
                    best = cell;
                    bestDistance = distance;
                }
            }

            if (!best.IsValid)
            {
                return false;
            }
            result = best;
            return true;
        }

        private static bool CellComesFirst(IntVec3 left, IntVec3 right)
        {
            return left.z < right.z || (left.z == right.z && left.x < right.x);
        }

        internal static void TickDirectPath(Pawn_PathFollower pather, Pawn pawn)
        {
            if (!MovingField(pather) || pawn.Map == null || pawn.Downed
                || pawn.stances.FullBodyBusy)
            {
                return;
            }

            LocalTargetInfo target = DestinationField(pather);
            if (!target.IsValid)
            {
                NotifyFailed(pather, pawn);
                return;
            }
            if (target.HasThing)
            {
                Thing thing = target.Thing;
                if (thing.Destroyed || !thing.Spawned
                    || thing.MapHeld != pawn.MapHeld)
                {
                    NotifyFailed(pather, pawn);
                    return;
                }
            }

            PathEndMode pathEndMode = PathEndModeField(pather);
            DirectMotionStates.TryGetValue(
                pawn, out DirectFlightMotionState? state);
            if (!TryGetCachedOrResolvedDestination(
                    pawn, target, ref pathEndMode, ref state, out IntVec3 destination))
            {
                NotifyFailed(pather, pawn);
                return;
            }
            if (pathEndMode != PathEndModeField(pather))
            {
                PathEndModeField(pather) = pathEndMode;
            }
            pather.lastPathedTargetPosition = destination;

            Vector3 destinationGroundPosition = destination.ToVector3Shifted();
            if (state == null
                || state.DestinationGroundPosition != destinationGroundPosition)
            {
                state = AssignDirectPath(
                    pather, pawn, destination, null, target, pathEndMode);
            }

            float cellsPerSecond = 30f;
            if (GameComponent_MechanicalFlightRegistry.TryGetRecord(
                    pawn, out MechanicalFlightAuthorizationRecord? record)
                && record?.Profile != null)
            {
                cellsPerSecond = Mathf.Max(0.01f,
                    record.Profile.flightCellsPerSecond);
            }

            Vector3 previousExact = state.ExactGroundPosition;
            state.ExactGroundPosition = Vector3.MoveTowards(
                previousExact, state.DestinationGroundPosition,
                cellsPerSecond / 60f);
            Vector3 movement = state.ExactGroundPosition - previousExact;
            if (movement.sqrMagnitude > 0.000001f)
            {
                pather.lastMoveDirection = movement.AngleFlat();
            }

            IntVec3 exactCell = IntVec3.FromVector3(state.ExactGroundPosition);
            if (exactCell.InBounds(pawn.Map) && exactCell != pawn.Position)
            {
                LastCellField(pather) = pawn.Position;
                pawn.Position = exactCell;
                LastEnteredCellTickField(pather) = GenTicks.TicksGame;
            }

            float remaining = Vector3.Distance(
                state.ExactGroundPosition, state.DestinationGroundPosition);
            float total = Mathf.Max(0.0001f, state.TotalDistance);
            CachedMovePercentageField(pather) =
                Mathf.Clamp01(1f - remaining / total);
            pather.nextCellCostTotal = total * 60f / cellsPerSecond;
            pather.nextCellCostLeft = remaining * 60f / cellsPerSecond;
            CachedCollisionField(pather) = false;
            LastMovedTickField(pather) = GenTicks.TicksGame;

            if (remaining <= 0.0001f)
            {
                // 正式到达前最终验证缓存终点仍然合法；失效时立即失败，
                // 不允许瞬移进已经不安全的格子。
                if (!IsCachedDirectDestinationValid(pawn, target, state))
                {
                    NotifyFailed(pather, pawn);
                    return;
                }
                if (pawn.Position != destination)
                {
                    LastCellField(pather) = pawn.Position;
                    pawn.Position = destination;
                    LastEnteredCellTickField(pather) = GenTicks.TicksGame;
                }
                pawn.Drawer.tweener.ResetTweenedPosToRoot();
                NotifyArrived(pather, pawn);
                return;
            }

            Vector3 lookAhead = Vector3.MoveTowards(
                state.ExactGroundPosition, state.DestinationGroundPosition, 1f);
            IntVec3 nextLogicalCell = IntVec3.FromVector3(lookAhead);
            pather.nextCell = nextLogicalCell.InBounds(pawn.Map)
                ? nextLogicalCell : pawn.Position;
        }

        private static DirectFlightMotionState AssignDirectPath(
            Pawn_PathFollower pather,
            Pawn pawn,
            IntVec3 destination,
            Vector3? exactStart,
            LocalTargetInfo sourceTarget,
            PathEndMode sourcePathEndMode)
        {
            pather.curPathRequest?.Dispose();
            pather.curPathRequest = null;
            pather.curPath?.ReleaseToPool();
            PawnPath path = pawn.Map.pawnPathPool.GetPath();
            List<IntVec3> cells = BuildLine(pawn.Position, destination);
            for (int i = cells.Count - 1; i >= 0; i--)
            {
                path.AddNode(cells[i]);
            }
            CurNodeIndexField(path) = cells.Count - 1;
            pather.curPath = path;
            pather.lastPathedTargetPosition = destination;
            pather.curPathJobIsStale = false;

            Vector3 destinationGroundPosition = destination.ToVector3Shifted();
            Vector3 startGroundPosition = exactStart
                ?? (DirectMotionStates.TryGetValue(
                        pawn, out DirectFlightMotionState? existing)
                    ? existing.ExactGroundPosition
                    : pawn.Position.ToVector3Shifted());
            DirectFlightMotionState state = new()
            {
                ExactGroundPosition = startGroundPosition,
                DestinationGroundPosition = destinationGroundPosition,
                TotalDistance = Vector3.Distance(
                    startGroundPosition, destinationGroundPosition)
            };
            RefreshDirectDestinationCache(
                state,
                sourceTarget,
                sourcePathEndMode,
                destination,
                GenTicks.TicksGame);
            DirectMotionStates[pawn] = state;
            return state;
        }

        // 复用缓存的解析终点：仅当目标、PathEndMode、Thing 位置/占用矩形、
        // 缓存格合法性或30 Tick 保险验证之一发生变化时才重新完整解析。
        private static bool TryGetCachedOrResolvedDestination(
            Pawn pawn,
            LocalTargetInfo target,
            ref PathEndMode pathEndMode,
            ref DirectFlightMotionState? state,
            out IntVec3 destination)
        {
            int tick = GenTicks.TicksGame;
            bool mustResolve = state == null
                || !state.SourceTarget.IsValid
                || state.SourceTarget != target
                || state.SourcePathEndMode != pathEndMode
                || tick >= state.NextDestinationValidationTick;

            if (!mustResolve && target.HasThing)
            {
                Thing thing = target.Thing;
                if (thing.Position != state!.LastTargetPosition
                    || !thing.OccupiedRect().Equals(state.LastTargetOccupiedRect))
                {
                    mustResolve = true;
                }
            }

            if (!mustResolve
                && !IsCachedDirectDestinationValid(pawn, target, state!))
            {
                mustResolve = true;
            }

            if (!mustResolve)
            {
                destination = state!.ResolvedDestination;
                return true;
            }

            PathEndMode resolvedMode = pathEndMode;
            if (!TryResolveDirectDestination(
                    pawn, target, ref resolvedMode, out IntVec3 resolved))
            {
                destination = IntVec3.Invalid;
                return false;
            }

            pathEndMode = resolvedMode;
            destination = resolved;
            if (state != null)
            {
                RefreshDirectDestinationCache(
                    state, target, resolvedMode, resolved, tick);
            }
            return true;
        }

        private static void RefreshDirectDestinationCache(
            DirectFlightMotionState state,
            LocalTargetInfo target,
            PathEndMode pathEndMode,
            IntVec3 destination,
            int tick)
        {
            state.SourceTarget = target;
            state.SourcePathEndMode = pathEndMode;
            state.ResolvedDestination = destination;
            state.LastTargetPosition = target.HasThing
                ? target.Thing.Position
                : IntVec3.Invalid;
            state.LastTargetOccupiedRect = target.HasThing
                ? target.Thing.OccupiedRect()
                : default;
            state.NextDestinationValidationTick =
                tick + DestinationValidationIntervalTicks;
        }

        private static bool IsCachedDirectDestinationValid(
            Pawn pawn,
            LocalTargetInfo target,
            DirectFlightMotionState state)
        {
            Map? map = pawn.Map;
            IntVec3 cell = state.ResolvedDestination;
            if (map == null || !cell.IsValid || !cell.InBounds(map))
            {
                return false;
            }

            PathEndMode resolvedMode = state.SourcePathEndMode;
            if (resolvedMode == PathEndMode.OnCell
                || resolvedMode == PathEndMode.None)
            {
                // OnCell / None 只要求目标格合法且位于地图内，不新增 WalkableBy 条件。
                return true;
            }

            if (!cell.WalkableBy(map, pawn))
            {
                return false;
            }

            LocalTargetInfo resolved = (LocalTargetInfo)target.ToTargetInfo(map);
            return TouchPathEndModeUtility.IsAdjacentOrInsideAndAllowedToTouch(
                cell, resolved, map.pathing.For(pawn));
        }

        private static void NotifyArrived(Pawn_PathFollower pather, Pawn pawn)
        {
            bool notifyDriver = pawn.jobs.curJob != null && !pather.curPathJobIsStale;
            ClearMotion(pawn);
            pather.StopDead();
            if (notifyDriver)
            {
                pawn.jobs.curDriver.Notify_PatherArrived();
            }
        }

        private static void NotifyFailed(Pawn_PathFollower pather, Pawn pawn)
        {
            bool notifyDriver = pawn.jobs.curJob != null && !pather.curPathJobIsStale;
            ClearMotion(pawn);
            pawn.Drawer.tweener.ResetTweenedPosToRoot();
            pather.StopDead();
            if (notifyDriver)
            {
                pawn.jobs.curDriver.Notify_PatherFailed();
            }
        }

        private static List<IntVec3> BuildLine(IntVec3 start, IntVec3 end)
        {
            List<IntVec3> cells = new();
            int x = start.x;
            int z = start.z;
            int dx = Mathf.Abs(end.x - x);
            int dz = Mathf.Abs(end.z - z);
            int sx = x < end.x ? 1 : -1;
            int sz = z < end.z ? 1 : -1;
            int error = dx - dz;
            while (true)
            {
                cells.Add(new IntVec3(x, 0, z));
                if (x == end.x && z == end.z)
                {
                    break;
                }
                int doubled = error * 2;
                if (doubled > -dz)
                {
                    error -= dz;
                    x += sx;
                }
                if (doubled < dx)
                {
                    error += dx;
                    z += sz;
                }
            }
            return cells;
        }
    }

    [HarmonyPatch(typeof(PawnPath), nameof(PawnPath.DrawPath))]
    internal static class MechanicalFlightPathDrawingPatch
    {
        public static bool Prefix(Pawn pathingPawn)
        {
            if (!MechanicalFlightStraightPathPatch.TryGetDirectPathDrawData(
                    pathingPawn, out Vector3 current, out Vector3 destination))
            {
                return true;
            }

            float altitude = AltitudeLayer.Item.AltitudeFor();
            current.y = altitude;
            destination.y = altitude;
            if ((destination - current).sqrMagnitude > 0.0001f)
            {
                GenDraw.DrawLineBetween(current, destination);
            }

            // 连续飞行只显示剩余直线，不再绘制兼容用网格PawnPath。
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StartPath))]
    internal static class MechanicalFlightStartPathPatch
    {
        public static bool Prefix(Pawn_PathFollower __instance, LocalTargetInfo dest,
            PathEndMode peMode, Pawn ___pawn)
        {
            if (!MechanicalFlightStraightPathPatch.IsActive(___pawn))
            {
                return true;
            }
            if (!___pawn.Drafted && ___pawn.CurJob?.playerForced != true
                && !MechanicalFlightEmergencyUtility.IsEmergencySequence(___pawn))
            {
                __instance.StopDead();
                return false;
            }
            MechanicalFlightStraightPathPatch.TryStartDirectPath(
                __instance, ___pawn, dest, peMode);
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.PatherTick))]
    internal static class MechanicalFlightPatherTickPatch
    {
        public static bool Prefix(Pawn_PathFollower __instance, Pawn ___pawn)
        {
            if (!MechanicalFlightStraightPathPatch.IsActive(___pawn))
            {
                return true;
            }
            MechanicalFlightStraightPathPatch.TickDirectPath(__instance, ___pawn);
            return false;
        }
    }

    [HarmonyPatch(typeof(FloatMenuMakerMap), "GetOptions")]
    internal static class MechanicalFlightMoveMenuPatch
    {
        public static void Postfix(List<Pawn> selectedPawns, Vector3 clickPos,
            ref List<FloatMenuOption> __result)
        {
            if (selectedPawns == null || selectedPawns.Count == 0)
            {
                return;
            }
            IntVec3 cell = IntVec3.FromVector3(clickPos);
            List<Pawn> flyers = selectedPawns.FindAll(pawn => pawn?.Map != null
                && cell.InBounds(pawn.Map) && MechanicalFlightUtility.IsActivelyFlying(pawn));
            if (flyers.Count == 0)
            {
                return;
            }

            bool occupied = cell.GetThingList(flyers[0].Map).Exists(thing =>
                thing is Pawn || thing.def.category == ThingCategory.Item
                || thing.HostileTo(flyers[0]));
            bool allSelectedPawnsFlying = flyers.Count == selectedPawns.Count;
            List<Pawn>? groundPawns = allSelectedPawnsFlying
                ? null
                : selectedPawns.FindAll(pawn => pawn != null && pawn.Drafted
                    && pawn.Spawned && !flyers.Contains(pawn));
            __result ??= new List<FloatMenuOption>();
            // 原版征召移动会先吸附到附近可站立格；无论是否混编，飞行单位都不能接受该目标。
            __result.RemoveAll(option => option.isGoto);
            __result.Insert(0, new FloatMenuOption(
                "MAP_MechanicalFlight_AerialMove".Translate(),
                () =>
                {
                    Map? feedbackMap = null;
                    for (int i = 0; i < flyers.Count; i++)
                    {
                        Pawn flyer = flyers[i];
                        if (MechanicalFlightUtility.TryStartAerialMove(flyer, cell))
                        {
                            feedbackMap ??= flyer.Map;
                        }
                    }
                    if (feedbackMap != null)
                    {
                        FleckMaker.Static(cell, feedbackMap, FleckDefOf.FeedbackGoto);
                    }
                },
                MenuOptionPriority.High)
            {
                // 不标记为原版 Goto，避免多选时 Selector 绕过此回调并改走群体移动。
                autoTakeable = allSelectedPawnsFlying && !occupied,
                autoTakeablePriority = 10000f
            });

            if (groundPawns != null && groundPawns.Count > 0)
            {
                // 地面单位保持原版多选 Goto 与吸附行为，单独走原版控制器。
                FloatMenuOption groundOption = new(
                    "GoHere".Translate(),
                    () => ExecuteGroundGoto(groundPawns, cell),
                    MenuOptionPriority.GoHere)
                {
                    isGoto = true
                };
                __result.Insert(1, groundOption);
            }
        }

        private static void ExecuteGroundGoto(List<Pawn> groundPawns, IntVec3 clickedCell)
        {
            Map? map = Find.CurrentMap;
            if (map == null || !clickedCell.InBounds(map))
            {
                return;
            }
            IntVec3 snapped = CellFinder.StandableCellNear(clickedCell, map, 2.9f);
            if (!snapped.IsValid)
            {
                return;
            }

            MultiPawnGotoController controller = Find.Selector.gotoController;
            controller.StartInteraction(snapped);
            bool addedAny = false;
            for (int i = 0; i < groundPawns.Count; i++)
            {
                Pawn pawn = groundPawns[i];
                if (pawn == null || !pawn.Drafted || !pawn.Spawned || pawn.Map != map)
                {
                    continue;
                }
                if (!FloatMenuOptionProvider_DraftedMove.PawnCanGoto(pawn, snapped).Accepted)
                {
                    continue;
                }
                controller.AddPawn(pawn);
                addedAny = true;
            }
            if (addedAny)
            {
                controller.FinalizeInteraction();
            }
            else
            {
                controller.Deactivate();
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), "PawnCanOccupy")]
    internal static class MechanicalFlightOccupancyPatch
    {
        public static void Postfix(Pawn ___pawn, IntVec3 __0, ref bool __result)
        {
            if (MechanicalFlightStraightPathPatch.IsActive(___pawn))
            {
                __result = __0.InBounds(___pawn.Map);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), "IsNextCellWalkable")]
    internal static class MechanicalFlightWalkabilityPatch
    {
        public static void Postfix(Pawn ___pawn, ref bool __result)
        {
            if (MechanicalFlightStraightPathPatch.IsActive(___pawn)) __result = true;
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), "BuildingBlockingNextPathCell")]
    internal static class MechanicalFlightBuildingBlockerPatch
    {
        public static void Postfix(Pawn ___pawn, ref Building __result)
        {
            if (MechanicalFlightStraightPathPatch.IsActive(___pawn)) __result = null!;
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), "NextCellDoorToWaitForOrManuallyOpen")]
    internal static class MechanicalFlightDoorBlockerPatch
    {
        public static void Postfix(Pawn ___pawn, ref Building_Door __result)
        {
            if (MechanicalFlightStraightPathPatch.IsActive(___pawn)) __result = null!;
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), "WillCollideWithPawnAt")]
    internal static class MechanicalFlightPawnCollisionPatch
    {
        public static void Postfix(Pawn ___pawn, ref bool __result)
        {
            if (MechanicalFlightStraightPathPatch.IsActive(___pawn)) __result = false;
        }
    }

    [HarmonyPatch(typeof(PawnUtility), nameof(PawnUtility.ShouldCollideWithPawns))]
    internal static class MechanicalFlightCollisionVolumePatch
    {
        public static void Postfix(Pawn p, ref bool __result)
        {
            if (MechanicalFlightUtility.UsesAerialMovement(p))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(JobGiver_MoveToStandable), "TryGiveJob")]
    internal static class MechanicalFlightMoveToStandablePatch
    {
        public static bool Prefix(Pawn pawn, ref Job __result)
        {
            if (!MechanicalFlightUtility.IsAirborne(pawn))
            {
                return true;
            }

            __result = null!;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), "TryRecoverFromUnwalkablePosition")]
    internal static class MechanicalFlightRecoveryPatch
    {
        public static bool Prefix(Pawn ___pawn, ref bool __result)
        {
            if (!MechanicalFlightStraightPathPatch.IsActive(___pawn)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(JobDriver_AttackMelee),
        nameof(JobDriver_AttackMelee.TryMakePreToilReservations))]
    internal static class MechanicalFlightMeleeJobPatch
    {
        public static bool Prefix(JobDriver_AttackMelee __instance, ref bool __result)
        {
            Thing? target = __instance.job?.targetA.Thing;
            if (!MechanicalFlightUtility.IsAirborne(__instance.pawn)
                && !(target is Pawn targetPawn
                    && MechanicalFlightUtility.IsAirborne(targetPawn)))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_MeleeVerbs), nameof(Pawn_MeleeVerbs.TryMeleeAttack))]
    internal static class MechanicalFlightMeleeExecutionPatch
    {
        public static bool Prefix(Pawn_MeleeVerbs __instance, Thing target,
            ref bool __result)
        {
            if (!MechanicalFlightUtility.IsAirborne(__instance.Pawn)
                && !(target is Pawn targetPawn
                    && MechanicalFlightUtility.IsAirborne(targetPawn)))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    internal static class MechanicalFlightMeleeImmunityPatch
    {
        public static bool Prefix(Pawn __instance, DamageInfo dinfo, ref bool absorbed)
        {
            if (!MechanicalFlightUtility.HasHoverVisual(__instance)
                || !IsMeleeDamage(dinfo))
            {
                return true;
            }
            absorbed = true;
            if (__instance.Map != null
                && __instance.DrawPos.ShouldSpawnMotesAt(__instance.Map, false))
            {
                Vector3 position = __instance.DrawPos;
                FleckMaker.ThrowAirPuffUp(position, __instance.Map);
            }
            return false;
        }

        private static bool IsMeleeDamage(DamageInfo dinfo)
        {
            if (dinfo.Tool != null || dinfo.WeaponBodyPartGroup != null
                || dinfo.Weapon?.IsMeleeWeapon == true)
            {
                return true;
            }
            return (dinfo.Instigator as Pawn)?.CurJob?.verbToUse?.IsMeleeAttack == true;
        }
    }

}
