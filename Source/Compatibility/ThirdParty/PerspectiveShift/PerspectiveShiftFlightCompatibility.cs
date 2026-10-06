using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.PerspectiveShift
{
    internal sealed class PerspectiveShiftFlightCompatibility : PerspectiveShiftCompatibilityModule
    {
        public override string ModuleId => "PerspectiveShift.AerialInput";
        public override string DisplayName => "Perspective Shift：机械飞行方向控制";
        protected override string AppliedDetail => "方向键复用机械飞行 Goto 与精确直线路径，松键仅结束本模块持有的移动任务。";
        protected override void SetEnabled(bool enabled) => PerspectiveShiftFlightPatches.Enabled = enabled;

        protected override void Resolve(ModContentPack mod, List<Binding> bindings)
        {
            Type avatar = PerspectiveShiftRuntime.AvatarType;
            Type state = PerspectiveShiftRuntime.StateType;
            MethodInfo updateInput = Method(avatar, "UpdateInput", false, typeof(void));
            MethodInfo rotate = Method(avatar, "RotateTowardsMouse", false, typeof(void));
            MethodInfo frozen = Method(state, "get_ControlsFrozen", true, typeof(bool));
            MethodInfo cleanup = Method(state, "CleanupPawnState", true, typeof(void), typeof(Pawn));
            Type snapshot = ResolveType(mod, "JobStateSnapshot");
            MethodInfo repeat = Method(ResolveType(mod, "Pawn_JobTracker_EndCurrentJob_Patch"), "Postfix", true,
                typeof(void), typeof(Pawn_JobTracker), typeof(JobCondition), snapshot);
            RequirePatch(Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob), false,
                typeof(void), typeof(JobCondition), typeof(bool), typeof(bool)), repeat, HarmonyPatchType.Postfix);
            PerspectiveShiftFlightPatches.Configure(updateInput, rotate,
                (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), frozen),
                Field(state, "CameraLockPosition", typeof(Vector3?), true),
                Field(avatar, "moveInput", typeof(Vector3)), Field(avatar, "physicsPosition", typeof(Vector3?)),
                Field(avatar, "moveInputDuration", typeof(float)), Field(avatar, "wasMovingLastFrame", typeof(bool)),
                Field(snapshot, "def", typeof(JobDef)));
            Type patches = typeof(PerspectiveShiftFlightPatches);
            bindings.Add(new Binding(Method(avatar, "UpdatePhysics", false, typeof(void)), patches,
                nameof(PerspectiveShiftFlightPatches.UpdatePhysicsPrefix), HarmonyPatchType.Prefix));
            bindings.Add(new Binding(Method(avatar, "RenderPawn", false, typeof(void)), patches,
                nameof(PerspectiveShiftFlightPatches.RenderPawnPrefix), HarmonyPatchType.Prefix));
            bindings.Add(new Binding(cleanup, patches, nameof(PerspectiveShiftFlightPatches.CleanupPawnPrefix),
                HarmonyPatchType.Prefix));
            bindings.Add(new Binding(repeat, patches, nameof(PerspectiveShiftFlightPatches.RepeatGotoPrefix),
                HarmonyPatchType.Prefix));
        }
    }

    internal static class PerspectiveShiftFlightPatches
    {
        internal static bool Enabled;
        private static MethodInfo updateInput = null!;
        private static MethodInfo rotateTowardsMouse = null!;
        private static Func<bool> controlsFrozen = null!;
        private static FieldInfo cameraLock = null!;
        private static FieldInfo inputField = null!;
        private static FieldInfo positionField = null!;
        private static FieldInfo durationField = null!;
        private static FieldInfo wasMovingField = null!;
        private static FieldInfo snapshotDef = null!;
        // 以 Avatar 为弱键，不持有旧游戏；Job 的 loadID 防止池化复用后误认任务归属。
        private static readonly ConditionalWeakTable<object, InputState> States = new ConditionalWeakTable<object, InputState>();

        private sealed class InputState
        {
            internal Job? OwnedJob;
            internal int OwnedJobId = -1;
            internal IntVec3 Direction = IntVec3.Zero;
            internal int LastIssueTick = -1;
        }

        internal static void Configure(MethodInfo input, MethodInfo rotate, Func<bool> frozen, FieldInfo camera,
            FieldInfo move, FieldInfo position, FieldInfo duration, FieldInfo wasMoving, FieldInfo jobDef)
        {
            updateInput = input;
            rotateTowardsMouse = rotate;
            controlsFrozen = frozen;
            cameraLock = camera;
            inputField = move;
            positionField = position;
            durationField = duration;
            wasMovingField = wasMoving;
            snapshotDef = jobDef;
        }

        private static void ClearGroundMotion(object avatar)
        {
            inputField.SetValue(avatar, Vector3.zero);
            positionField.SetValue(avatar, null);
            durationField.SetValue(avatar, 0f);
            wasMovingField.SetValue(avatar, false);
        }

        private static bool OwnsCurrentJob(InputState state, Pawn pawn) => state.OwnedJob != null
            && ReferenceEquals(pawn.CurJob, state.OwnedJob) && pawn.CurJob.loadID == state.OwnedJobId
            && pawn.CurJobDef == JobDefOf.Goto && pawn.CurJob.flying;

        private static void StopOwnedMove(InputState state, Pawn pawn)
        {
            bool stop = PerspectiveShiftRuntime.CanQuery && OwnsCurrentJob(state, pawn)
                && !MechanicalFlightEmergencyUtility.IsEmergencySequence(pawn);
            state.OwnedJob = null;
            state.OwnedJobId = -1;
            state.Direction = IntVec3.Zero;
            if (stop)
                MechanicalFlightUtility.StopAerialInputMove(pawn);
        }

        // 高于能源模块的飞行屏障；两者同时安装时，本模块负责正常飞行输入。
        [HarmonyPriority(Priority.First)]
        public static bool UpdatePhysicsPrefix(object __instance)
        {
            Pawn? pawn = PerspectiveShiftRuntime.GetPawn(__instance);
            if (!Enabled || !PerspectiveShiftRuntime.IsControlled(pawn))
                return true;
            if (!MechanicalFlightUtility.IsAirborne(pawn))
            {
                if (States.TryGetValue(__instance, out InputState previous))
                {
                    StopOwnedMove(previous, pawn!);
                    States.Remove(__instance);
                }
                return true;
            }

            InputState state = States.GetValue(__instance, _ => new InputState());
            ClearGroundMotion(__instance);
            if (!pawn!.Spawned || pawn.Map == null || pawn.Map != Find.CurrentMap
                || !PerspectiveShiftRuntime.CanOperate(pawn) || !pawn.Drafted
                || !MechanicalFlightUtility.IsActivelyFlying(pawn)
                || PerspectiveShiftRuntime.IsUnderAIControl(pawn)
                || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving)
                || !pawn.jobs.IsCurrentJobPlayerInterruptible() || pawn.CurJob?.def.forceCompleteBeforeNextJob == true
                || pawn.stances.curStance is Stance_Busy || controlsFrozen() || cameraLock.GetValue(null) != null)
            {
                StopOwnedMove(state, pawn);
                return false;
            }

            // 复用上游键位配置及世界视图判断；捕获后清空，避免 HandlePather 覆盖飞行速度。
            updateInput.Invoke(__instance, Array.Empty<object>());
            Vector3 input = (Vector3)inputField.GetValue(__instance);
            ClearGroundMotion(__instance);
            rotateTowardsMouse.Invoke(__instance, Array.Empty<object>());
            IntVec3 direction = new IntVec3(Math.Sign(input.x), 0, Math.Sign(input.z));
            if (direction == IntVec3.Zero)
            {
                StopOwnedMove(state, pawn);
                return false;
            }

            bool owned = OwnsCurrentJob(state, pawn);
            Vector3 exact = MechanicalFlightStraightPathPatch.TryGetExactGroundDrawPos(pawn, out Vector3 current)
                ? current : pawn.Position.ToVector3Shifted();
            if (owned && state.Direction == direction
                && (state.OwnedJob!.targetA.Cell.ToVector3Shifted() - exact).sqrMagnitude > 1f)
                return false;
            // 一游戏 Tick 最多发起或重定向一次；低速游戏的多次帧更新不重复启动任务。
            int tick = Find.TickManager.TicksGame;
            if (state.LastIssueTick == tick)
                return false;
            state.LastIssueTick = tick;
            for (int distance = 3; distance >= 1; distance--)
            {
                IntVec3 goal = (exact + input.normalized * distance).ToIntVec3();
                if (goal == pawn.Position || !MechanicalFlightUtility.CanIssueAerialMove(pawn, goal))
                    continue;
                if (owned)
                {
                    if (!MechanicalFlightUtility.TryRetargetAerialInputMove(pawn, state.OwnedJob!, goal))
                        break;
                }
                else
                {
                    if (!MechanicalFlightUtility.TryStartAerialInputMove(pawn, goal, out Job? job))
                        break;
                    state.OwnedJob = job;
                    state.OwnedJobId = job!.loadID;
                }
                state.Direction = direction;
                return false;
            }
            StopOwnedMove(state, pawn);
            return false;
        }

        [HarmonyPriority(Priority.First)]
        public static bool RenderPawnPrefix(object __instance)
        {
            Pawn? pawn = PerspectiveShiftRuntime.GetPawn(__instance);
            if (!Enabled || !PerspectiveShiftRuntime.IsControlled(pawn) || !MechanicalFlightUtility.IsAirborne(pawn))
                return true;
            // Tween、悬浮高度和转向仍由现有机械飞行绘制负责。
            ClearGroundMotion(__instance);
            return false;
        }

        public static void CleanupPawnPrefix(Pawn __0)
        {
            object? avatar = PerspectiveShiftRuntime.CurrentAvatar;
            if (Enabled && avatar != null && ReferenceEquals(PerspectiveShiftRuntime.GetPawn(avatar), __0)
                && States.TryGetValue(avatar, out InputState state))
            {
                StopOwnedMove(state, __0);
                States.Remove(avatar);
            }
        }

        public static bool RepeatGotoPrefix(Pawn_JobTracker __0, object? __2)
        {
            if (!Enabled || !PerspectiveShiftRuntime.CanQuery || __0 == null || __2 == null)
                return true;
            Pawn? pawn = PerspectiveShiftRuntime.CurrentPawn;
            return pawn == null || !ReferenceEquals(pawn.jobs, __0) || !PerspectiveShiftRuntime.IsControlled(pawn)
                || !MechanicalFlightUtility.IsAirborne(pawn) || (snapshotDef.GetValue(__2) as JobDef) != JobDefOf.Goto;
        }
    }
}
