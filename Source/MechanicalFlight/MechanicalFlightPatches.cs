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
            foreach (Gizmo gizmo in __result)
            {
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
            if (MechanicalFlightUtility.IsActivelyFlying(PawnField(__instance)))
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
            if (!MechanicalFlightUtility.IsActivelyFlying(pawn))
            {
                job.flying = false;
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
                job.flying = true;
                Messages.Message("MAP_MechanicalFlight_GroundJobBlocked".Translate(), pawn,
                    MessageTypeDefOf.RejectInput, false);
            }
            return false;
        }

        internal static bool IsHoverCompatible(Job? job)
        {
            if (job?.def == null)
            {
                return false;
            }

            bool meleeOrTouch = job.def.defName == "AttackMelee"
                || job.def.defName == "CastAbilityTouch"
                || job.verbToUse?.IsMeleeAttack == true;
            return !meleeOrTouch
                && (job.def.ifFlyingKeepFlying || HoverCompatibleJobs.Contains(job.def.defName));
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
            if (!MechanicalFlightUtility.IsActivelyFlying(pawn)
                || MechanicalFlightJobPatch.IsHoverCompatible(job)
                || job == null || !job.targetA.IsValid)
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
                return true;
            }

            pawn.pather?.StopDead();
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), "SetNewPathRequest")]
    internal static class MechanicalFlightStraightPathPatch
    {
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
            IntVec3 destination = DestinationField(__instance).Cell;
            if (!destination.IsValid || !destination.InBounds(pawn.Map))
            {
                return false;
            }

            AssignDirectPath(__instance, pawn, destination);
            return false;
        }

        internal static bool IsActive(Pawn? pawn)
        {
            return pawn?.Map != null && MechanicalFlightUtility.IsActivelyFlying(pawn);
        }

        internal static bool TryStartDirectPath(
            Pawn_PathFollower pather,
            Pawn pawn,
            LocalTargetInfo destination,
            PathEndMode pathEndMode)
        {
            if (!destination.IsValid || pawn.Map == null
                || !destination.Cell.InBounds(pawn.Map)
                || (destination.HasThing && destination.Thing.MapHeld != pawn.MapHeld))
            {
                NotifyFailed(pather, pawn);
                return false;
            }

            pather.StopDead();
            DestinationField(pather) = destination;
            PathEndModeField(pather) = pathEndMode;
            pather.lastPathedTargetPosition = destination.Cell;
            if (pawn.Position == destination.Cell)
            {
                NotifyArrived(pather, pawn);
                return true;
            }

            MovingField(pather) = true;
            pawn.jobs.posture = PawnPosture.Standing;
            CachedMovePercentageField(pather) = 0f;
            CachedCollisionField(pather) = false;
            pather.curPathJobIsStale = false;
            AssignDirectPath(pather, pawn, destination.Cell);
            return true;
        }

        internal static void TickDirectPath(Pawn_PathFollower pather, Pawn pawn)
        {
            if (!MovingField(pather) || pawn.Map == null || pawn.Downed
                || pawn.stances.FullBodyBusy)
            {
                return;
            }

            LocalTargetInfo target = DestinationField(pather);
            IntVec3 destination = target.Cell;
            if (!destination.IsValid || !destination.InBounds(pawn.Map))
            {
                NotifyFailed(pather, pawn);
                return;
            }
            if (target.HasThing && target.Thing.MapHeld != pawn.MapHeld)
            {
                NotifyFailed(pather, pawn);
                return;
            }
            if (pawn.Position == destination)
            {
                NotifyArrived(pather, pawn);
                return;
            }

            if (pather.curPath == null
                || pather.lastPathedTargetPosition != destination)
            {
                pather.nextCell = pawn.Position;
                pather.nextCellCostLeft = 0f;
                AssignDirectPath(pather, pawn, destination);
            }
            if (!pather.nextCell.IsValid || pather.nextCell == pawn.Position)
            {
                if (!SetupNextCell(pather, pawn, destination))
                {
                    return;
                }
            }

            pather.nextCellCostLeft -= 1f;
            CachedMovePercentageField(pather) = Mathf.Clamp01(
                1f - pather.nextCellCostLeft / pather.nextCellCostTotal);
            CachedCollisionField(pather) = false;
            if (pather.nextCellCostLeft > 0f)
            {
                LastMovedTickField(pather) = Find.TickManager.TicksGame;
                return;
            }

            IntVec3 previous = pawn.Position;
            LastCellField(pather) = previous;
            pawn.Position = pather.nextCell;
            pather.lastMoveDirection = (pather.nextCell - previous).AngleFlat;
            LastEnteredCellTickField(pather) = GenTicks.TicksGame;
            LastMovedTickField(pather) = GenTicks.TicksGame;
            if (pawn.Position == destination)
            {
                NotifyArrived(pather, pawn);
                return;
            }
            SetupNextCell(pather, pawn, destination);
        }

        private static void AssignDirectPath(
            Pawn_PathFollower pather,
            Pawn pawn,
            IntVec3 destination)
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
        }

        private static bool SetupNextCell(
            Pawn_PathFollower pather,
            Pawn pawn,
            IntVec3 destination)
        {
            if (pather.curPath == null || pather.curPath.NodesLeftCount <= 1)
            {
                if (pawn.Position == destination)
                {
                    NotifyArrived(pather, pawn);
                    return false;
                }
                AssignDirectPath(pather, pawn, destination);
            }

            IntVec3 previousNextCell = pather.nextCell;
            pather.nextCell = pather.curPath!.ConsumeNextNode();
            if (previousNextCell == pather.nextCell)
            {
                if (pather.curPath.NodesLeftCount <= 1)
                {
                    NotifyFailed(pather, pawn);
                    return false;
                }
                pather.nextCell = pather.curPath.ConsumeNextNode();
            }

            float cellsPerSecond = 30f;
            if (GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                && record?.Profile != null)
            {
                cellsPerSecond = Mathf.Max(0.01f, record.Profile.flightCellsPerSecond);
            }
            bool diagonal = pather.nextCell.x != pawn.Position.x
                && pather.nextCell.z != pawn.Position.z;
            float ticksForCell = 60f / cellsPerSecond
                * (diagonal ? Mathf.Sqrt(2f) : 1f);
            pather.nextCellCostTotal = Mathf.Max(0.01f, ticksForCell);
            pather.nextCellCostLeft = Mathf.Max(
                pather.nextCellCostTotal + Mathf.Min(pather.nextCellCostLeft, 0f),
                0.01f);
            CachedMovePercentageField(pather) = Mathf.Clamp01(
                1f - pather.nextCellCostLeft / pather.nextCellCostTotal);
            CachedCollisionField(pather) = false;
            return true;
        }

        private static void NotifyArrived(Pawn_PathFollower pather, Pawn pawn)
        {
            bool notifyDriver = pawn.jobs.curJob != null && !pather.curPathJobIsStale;
            pather.StopDead();
            if (notifyDriver)
            {
                pawn.jobs.curDriver.Notify_PatherArrived();
            }
        }

        private static void NotifyFailed(Pawn_PathFollower pather, Pawn pawn)
        {
            bool notifyDriver = pawn.jobs.curJob != null && !pather.curPathJobIsStale;
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
            if (!___pawn.Drafted && ___pawn.CurJob?.playerForced != true)
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
            __result ??= new List<FloatMenuOption>();
            __result.Insert(0, new FloatMenuOption(
                "MAP_MechanicalFlight_AerialMove".Translate(),
                () => flyers.ForEach(flyer =>
                    MechanicalFlightUtility.TryStartAerialMove(flyer, cell)),
                MenuOptionPriority.High)
            {
                autoTakeable = flyers.Count == selectedPawns.Count && !occupied,
                autoTakeablePriority = 10000f
            });
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
            if (MechanicalFlightUtility.IsActivelyFlying(p))
            {
                __result = false;
            }
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

    [HarmonyPatch(typeof(Pawn_PathFollower), "CostToPayThisTick")]
    internal static class MechanicalFlightSpeedPatch
    {
        public static void Postfix(Pawn_PathFollower __instance, Pawn ___pawn,
            ref float __result)
        {
            if (GameComponent_MechanicalFlightRegistry.TryGetRecord(___pawn, out var record)
                && record?.ConsumesFlightEnergy == true && record.Profile != null)
            {
                float cellsPerSecond = Mathf.Max(0.01f,
                    record.Profile.flightCellsPerSecond);
                bool diagonal = __instance.nextCell.x != ___pawn.Position.x
                    && __instance.nextCell.z != ___pawn.Position.z;
                float ticksForCell = 60f / cellsPerSecond
                    * (diagonal ? Mathf.Sqrt(2f) : 1f);
                __result = __instance.nextCellCostTotal / Mathf.Max(1f, ticksForCell);
            }
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
                if (GameComponent_MechanicalFlightRegistry.TryGetRecord(
                        __instance, out var record) && record != null)
                {
                    position += MechanicalFlightPresentationUtility.HoverVisualOffset(
                        __instance, record);
                }
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
