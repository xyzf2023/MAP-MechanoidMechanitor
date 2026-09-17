using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    internal static class MechanicalFlightPredatorUtility
    {
        internal static bool IsAirborneHumanlike(Pawn? prey)
        {
            // 复用运行阶段，覆盖起飞、悬浮和落地前的迫降；兼容原版飞行状态。
            return prey != null && !prey.Dead && prey.RaceProps.Humanlike
                && (MechanicalFlightUtility.IsAirborne(prey) || prey.flight?.Flying == true);
        }
    }

    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.IsAcceptablePreyFor))]
    internal static class MechanicalFlightPredatorPreyPatch
    {
        public static void Postfix(Pawn prey, ref bool __result)
        {
            if (__result && MechanicalFlightPredatorUtility.IsAirborneHumanlike(prey))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(JobDriver_PredatorHunt), "MakeNewToils")]
    internal static class MechanicalFlightPredatorHuntPatch
    {
        public static IEnumerable<Toil> Postfix(
            IEnumerable<Toil> __result, JobDriver_PredatorHunt __instance)
        {
            // 新建及读档重建任务时均注册；起飞后由原版任务结束流程清理追踪与攻击缓存。
            // 直接读取活体目标，不通过 Prey 将尸体重新转换为 Pawn。
            __instance.AddEndCondition(() =>
                MechanicalFlightPredatorUtility.IsAirborneHumanlike(
                    __instance.job?.GetTarget(TargetIndex.A).Thing as Pawn)
                    ? JobCondition.Incompletable
                    : JobCondition.Ongoing);

            foreach (Toil toil in __result)
            {
                yield return toil;
            }
        }
    }
}
