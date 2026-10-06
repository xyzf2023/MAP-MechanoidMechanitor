using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.PerspectiveShift
{
    internal sealed class PerspectiveShiftEnergySafetyCompatibility : PerspectiveShiftCompatibilityModule
    {
        public override string ModuleId => "PerspectiveShift.EnergyAndSafety";
        public override string DisplayName => "Perspective Shift：机械能源与控制安全";
        protected override string AppliedDetail => "等待兜底复用现有充电/关机节点，保留任务到期；保护失能、迫降与飞行运动。";
        protected override void SetEnabled(bool enabled) => PerspectiveShiftEnergySafetyPatches.Enabled = enabled;

        protected override void Resolve(ModContentPack mod, List<Binding> bindings)
        {
            Type avatar = PerspectiveShiftRuntime.AvatarType;
            MethodInfo ordersPatch = Method(ResolveType(mod, "JobGiver_Orders_TryGiveJob_Patch"), "Postfix", true,
                typeof(void), typeof(Pawn), typeof(Job).MakeByRefType());
            RequirePatch(Method(typeof(JobGiver_Orders), "TryGiveJob", false, typeof(Job), typeof(Pawn)),
                ordersPatch, HarmonyPatchType.Postfix);
            MethodInfo overridePatch = Method(ResolveType(mod, "Pawn_JobTracker_CheckForJobOverride_Patch"),
                "Prefix", true, typeof(bool), typeof(Pawn_JobTracker));
            RequirePatch(Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.CheckForJobOverride), false,
                typeof(void), typeof(float), typeof(bool)), overridePatch, HarmonyPatchType.Prefix);
            Type snapshot = ResolveType(mod, "JobStateSnapshot");
            FieldInfo snapshotDef = Field(snapshot, "def", typeof(JobDef));
            MethodInfo repeatPatch = Method(ResolveType(mod, "Pawn_JobTracker_EndCurrentJob_Patch"), "Postfix", true,
                typeof(void), typeof(Pawn_JobTracker), typeof(JobCondition), snapshot);
            RequirePatch(Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob), false,
                typeof(void), typeof(JobCondition), typeof(bool), typeof(bool)), repeatPatch, HarmonyPatchType.Postfix);
            MethodInfo orderPatch = Method(ResolveType(mod, "Pawn_CanTakeOrder_Patch"), "Postfix", true,
                typeof(void), typeof(Pawn), typeof(bool).MakeByRefType());
            MethodInfo canTakeOrder = Method(typeof(Pawn), "get_CanTakeOrder", false, typeof(bool));
            RequirePatch(canTakeOrder, orderPatch, HarmonyPatchType.Postfix);

            PerspectiveShiftEnergySafetyPatches.ConfigureMotion(
                Field(avatar, "moveInput", typeof(Vector3)), Field(avatar, "physicsPosition", typeof(Vector3?)),
                Field(avatar, "moveInputDuration", typeof(float)), Field(avatar, "wasMovingLastFrame", typeof(bool)),
                Field(avatar, "isSprinting", typeof(bool)), Field(avatar, "isWalking", typeof(bool)),
                Field(avatar, "LeanTarget", typeof(Vector3)), Field(avatar, "LeanSmoothed", typeof(Vector3)), snapshotDef);

            Type patches = typeof(PerspectiveShiftEnergySafetyPatches);
            bindings.Add(new Binding(Method(typeof(ThinkNode_JobGiver), nameof(ThinkNode_JobGiver.TryIssueJobPackage),
                false, typeof(ThinkResult), typeof(Pawn), typeof(JobIssueParams)), patches,
                nameof(PerspectiveShiftEnergySafetyPatches.OrdersResultPostfix), HarmonyPatchType.Postfix));
            bindings.Add(new Binding(Method(avatar, "PreventJobExpiry", false, typeof(void)), patches,
                nameof(PerspectiveShiftEnergySafetyPatches.PreventJobExpiryPrefix), HarmonyPatchType.Prefix));
            bindings.Add(new Binding(overridePatch, patches,
                nameof(PerspectiveShiftEnergySafetyPatches.CheckOverridePrefix), HarmonyPatchType.Prefix));
            bindings.Add(new Binding(repeatPatch, patches,
                nameof(PerspectiveShiftEnergySafetyPatches.RepeatInteractionPrefix), HarmonyPatchType.Prefix));
            bindings.Add(new Binding(Method(avatar, "UpdatePhysics", false, typeof(void)), patches,
                nameof(PerspectiveShiftEnergySafetyPatches.UpdatePhysicsPrefix), HarmonyPatchType.Prefix));
            bindings.Add(new Binding(Method(avatar, "RenderPawn", false, typeof(void)), patches,
                nameof(PerspectiveShiftEnergySafetyPatches.RenderPawnPrefix), HarmonyPatchType.Prefix));
            bindings.Add(new Binding(Method(avatar, "HandleFiring", false, typeof(void)), patches,
                nameof(PerspectiveShiftEnergySafetyPatches.ActionPrefix), HarmonyPatchType.Prefix));
            bindings.Add(new Binding(Method(avatar, "HandleSelectorClick", false, typeof(bool)), patches,
                nameof(PerspectiveShiftEnergySafetyPatches.ActionPrefix), HarmonyPatchType.Prefix));
            bindings.Add(new Binding(Method(avatar, "HandleLeftClick", false, typeof(bool)), patches,
                nameof(PerspectiveShiftEnergySafetyPatches.ActionPrefix), HarmonyPatchType.Prefix));
            bindings.Add(new Binding(canTakeOrder, patches,
                nameof(PerspectiveShiftEnergySafetyPatches.CanTakeOrderPostfix), HarmonyPatchType.Postfix));
        }
    }

    internal static class PerspectiveShiftEnergySafetyPatches
    {
        internal static bool Enabled;
        private static FieldInfo[] motionFields = Array.Empty<FieldInfo>();
        private static readonly object?[] MotionDefaults =
            { Vector3.zero, null, 0f, false, false, false, Vector3.zero, Vector3.zero };
        private static FieldInfo? snapshotDef;

        internal static void ConfigureMotion(FieldInfo move, FieldInfo position, FieldInfo duration,
            FieldInfo wasMoving, FieldInfo sprint, FieldInfo walk, FieldInfo lean, FieldInfo leanSmoothed, FieldInfo jobDef)
        {
            motionFields = new[] { move, position, duration, wasMoving, sprint, walk, lean, leanSmoothed };
            snapshotDef = jobDef;
        }

        // 只替换上游为未征召主角生成的 Wait，其他原版/MOD 结果与队列保持不变。
        // 返回实际 giver 的完整 ThinkResult，保留 SourceNode，作息续充身份不会变成 Orders。
        public static void OrdersResultPostfix(ThinkNode_JobGiver __instance, Pawn __0, JobIssueParams __1,
            ref ThinkResult __result)
        {
            if (!Enabled || !(__instance is JobGiver_Orders) || !__result.IsValid
                || __result.Job.def != JobDefOf.Wait || __result.Job.playerForced || __result.FromQueue
                || !ReferenceEquals(__result.SourceNode, __instance) || !PerspectiveShiftRuntime.CanQuery
                || !PerspectiveShiftRuntime.IsControlled(__0) || !PerspectiveShiftRuntime.CanOperate(__0)
                || __0.Drafted || !__0.Spawned || __0.Map == null
                || __0.thinker == null || __0.needs?.energy == null || __0.IsFormingCaravan()
                || PerspectiveShiftRuntime.IsUnderAIControl(__0)
                || MechanicalFlightUtility.IsAirborne(__0) || !MechTransformationUtility.IsInPawnForm(__0))
                return;

            ThinkResult energyJob;
            MechanoidMechanitorSelfWorkModeUtility.TryGetCurrentMode(__0, out MechWorkModeDef? mode);
            if (MechanoidMechanitorSelfWorkModeUtility.IsSelfShutdownMode(mode))
                energyJob = TryEnergyNode<JobGiver_SelfShutdown>(__0, __1);
            else if (MechanoidMechanitorSelfWorkModeUtility.IsRechargeMode(mode))
            {
                energyJob = TryEnergyNode<JobGiver_GetEnergy_Charger>(__0, __1);
                if (!energyJob.IsValid)
                    energyJob = TryEnergyNode<JobGiver_GetEnergy_SelfShutdown>(__0, __1);
            }
            else
            {
                energyJob = TryEnergyNode<JobGiver_MechanoidMechanitorScheduledRecharge>(__0, __1);
                if (!energyJob.IsValid && MechanoidMechanitorTimetableUtility.IsExecutingManagedScheduledRecharge(__0))
                    energyJob = TryEnergyNode<JobGiver_MechanoidMechanitorPostSleepRecharge>(__0, __1);
                if (!energyJob.IsValid)
                    energyJob = TryEnergyNode<JobGiver_GetEnergy_Charger>(__0, __1);
            }
            if (!energyJob.IsValid)
                return;
            Job wait = __result.Job;
            __result = energyJob;
            JobMaker.ReturnToPool(wait);
        }

        private static ThinkResult TryEnergyNode<T>(Pawn pawn, JobIssueParams jobParams) where T : ThinkNode =>
            pawn.thinker.TryGetMainTreeThinkNode<T>()?.TryIssueJobPackage(pawn, jobParams) ?? ThinkResult.NoJob;

        public static bool PreventJobExpiryPrefix(object __instance) => !Enabled
            || !PerspectiveShiftRuntime.CanQuery || !PerspectiveShiftRuntime.IsControlled(PerspectiveShiftRuntime.GetPawn(__instance))
            || !PerspectiveShiftRuntime.IsEnergyJob(PerspectiveShiftRuntime.GetPawn(__instance));

        // 允许原版对充电/关机任务重新评估，不让上游的 playerForced 屏障截断该流程。
        public static bool CheckOverridePrefix(Pawn_JobTracker __0, ref bool __result)
        {
            if (!Enabled || !PerspectiveShiftRuntime.CanQuery || __0 == null)
                return true;
            Pawn? pawn = PerspectiveShiftRuntime.CurrentPawn;
            // 原版 tracker.pawn 是 protected；通过 Pawn.jobs 核对当前接管者的 tracker。
            if (pawn == null || !ReferenceEquals(pawn.jobs, __0) || !PerspectiveShiftRuntime.IsControlled(pawn)
                || !PerspectiveShiftRuntime.IsPlayerMechanitor(pawn) || !PerspectiveShiftRuntime.IsEnergyJob(pawn))
                return true;
            __result = true;
            return false;
        }

        public static bool RepeatInteractionPrefix(Pawn_JobTracker __0, object? __2)
        {
            if (!Enabled || !PerspectiveShiftRuntime.CanQuery || __0 == null || __2 == null || snapshotDef == null)
                return true;
            Pawn? pawn = PerspectiveShiftRuntime.CurrentPawn;
            if (pawn == null || !ReferenceEquals(pawn.jobs, __0) || !PerspectiveShiftRuntime.IsControlled(pawn))
                return true;
            JobDef? def = snapshotDef.GetValue(__2) as JobDef;
            return def != JobDefOf.MechCharge && def != JobDefOf.SelfShutdown;
        }

        private static bool BlocksActions(Pawn pawn) => !pawn.Spawned || pawn.Map == null
            || !PerspectiveShiftRuntime.CanOperate(pawn)
            || (!pawn.Drafted && (PerspectiveShiftRuntime.IsEnergyJob(pawn)
                || MechanoidMechanitorSelfWorkModeUtility.IsSelfShutdown(pawn)));

        private static bool BlocksMotion(Pawn pawn) => BlocksActions(pawn) || MechanicalFlightUtility.IsAirborne(pawn)
            || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving);

        private static void ClearMotion(object avatar)
        {
            // 不停止 pather：充电路径、迫降与飞行由原有系统持有。
            for (int i = 0; i < motionFields.Length; i++)
                motionFields[i].SetValue(avatar, MotionDefaults[i]);
        }

        public static bool UpdatePhysicsPrefix(object __instance)
        {
            Pawn? pawn = PerspectiveShiftRuntime.GetPawn(__instance);
            if (!Enabled || !PerspectiveShiftRuntime.IsControlled(pawn) || !BlocksMotion(pawn!))
                return true;
            ClearMotion(__instance);
            return false;
        }

        public static bool RenderPawnPrefix(object __instance)
        {
            Pawn? pawn = PerspectiveShiftRuntime.GetPawn(__instance);
            if (!Enabled || !PerspectiveShiftRuntime.IsControlled(pawn) || !BlocksMotion(pawn!))
                return true;
            ClearMotion(__instance);
            return false;
        }

        // bool 入口被跳过时默认返回 false，void 入口直接跳过；无需吞掉上游异常。
        public static bool ActionPrefix(object __instance)
        {
            Pawn? pawn = PerspectiveShiftRuntime.GetPawn(__instance);
            return !Enabled || !PerspectiveShiftRuntime.IsControlled(pawn) || !BlocksActions(pawn!);
        }

        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("PerspectiveShiftMod")]
        public static void CanTakeOrderPostfix(Pawn __instance, ref bool __result)
        {
            if (Enabled && PerspectiveShiftRuntime.IsControlled(__instance) && !PerspectiveShiftRuntime.CanOperate(__instance))
                __result = false;
        }
    }
}
