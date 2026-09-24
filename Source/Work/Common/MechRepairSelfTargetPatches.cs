using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    internal static class MechRepairTargetSafetyUtility
    {
        // 原版修理结束回调会结束目标当前任务，不能让修理目标链形成环。
        // 这里只读取当前任务，不终止任务，也不清理已有互修状态。
        internal static bool WouldCreateRepairCycle(Pawn? actor, Thing? target)
        {
            if (actor == null || target is not Pawn current)
            {
                return false;
            }

            HashSet<Pawn>? visited = null;
            while (true)
            {
                if (current == actor)
                {
                    return true;
                }

                // 包含远程修理及使用原版修理驱动的派生任务。
                // 不排除 ended：结束回调执行期间 curJob 尚未清空，仍可能重入。
                if (current.jobs?.curDriver is not JobDriver_RepairMech
                    || current.CurJob?.targetA.Pawn is not Pawn next)
                {
                    return false;
                }

                visited ??= new HashSet<Pawn>();
                if (!visited.Add(current))
                {
                    // 不允许新任务接入已有环，同时保证检测本身不会无限循环。
                    return true;
                }

                current = next;
            }
        }
    }

    [HarmonyPatch(typeof(WorkGiver_RepairMech), nameof(WorkGiver_RepairMech.HasJobOnThing))]
    public static class MechRepairSelfTargetPatches_HasJobOnThing
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn pawn, Thing t, ref bool __result)
        {
            if (MechRepairTargetSafetyUtility.WouldCreateRepairCycle(pawn, t))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch]
    public static class MechRepairSelfTargetPatches_TryMakePreToilReservations
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(JobDriver_RepairMech),
                nameof(JobDriver_RepairMech.TryMakePreToilReservations));
            // 远程修理重写预约方法且不调用基类，必须单独覆盖。
            yield return AccessTools.Method(typeof(JobDriver_RepairMechRemote),
                nameof(JobDriver_RepairMechRemote.TryMakePreToilReservations));
        }

        [HarmonyPrefix]
        public static bool Prefix(JobDriver_RepairMech __instance, ref bool __result)
        {
            Pawn? pawn = __instance.pawn;
            Thing? target = __instance.job?.targetA.Thing;
            // StartJob 已设置当前任务，但尚未 SetupToils；拒绝时不会运行修理结束回调。
            if (MechRepairTargetSafetyUtility.WouldCreateRepairCycle(pawn, target))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }
}
