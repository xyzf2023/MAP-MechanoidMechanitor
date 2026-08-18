using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械族机械师娱乐相关的 Harmony 补丁：
    ///  1. JoyUtility.JoyTickCheckEnd：本系统娱乐 Job 没有 Need_Joy，
    ///     原版会在该分支直接 InterruptForced，这里跳过原版并改为只结算 joySkill XP。
    ///  2. Pawn_JobTracker.EndCurrentJob：仅当娱乐 Job 以 Succeeded 结束时，
    ///     按设置概率 Roll 一个当前合法的原版灵感。
    /// </summary>
    public static class MechanoidMechanitorRecreationPatches
    {
        [HarmonyPatch(
            typeof(JoyUtility),
            nameof(JoyUtility.JoyTickCheckEnd))]
        public static class Patch_JoyUtility_JoyTickCheckEnd_MechanoidMechanitorRecreation
        {
            [HarmonyPrefix]
            public static bool Prefix(
                Pawn pawn,
                int delta,
                ref bool __result)
            {
                Job? job = pawn?.CurJob;

                if (!MechanoidMechanitorRecreationUtility
                        .IsManagedRecreationJob(pawn, job))
                {
                    return true;
                }

                // 本系统机械族娱乐：
                // 不 GainJoy、不检查 Joy 满值、不检查 timetable、
                // 不因缺少 Need_Joy 而中断。
                // 但保留 JobDef 自带的 joySkill XP（具体以原版 JobDef 为准）。
                JobDef? jobDef = job?.def;
                SkillDef? joySkill = jobDef?.joySkill;
                Pawn_SkillTracker? skills = pawn?.skills;
                if (joySkill != null && skills != null)
                {
                    skills
                        .GetSkill(joySkill)
                        .Learn(jobDef!.joyXpPerTick * delta);
                }

                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(Pawn_JobTracker))]
        [HarmonyPatch(nameof(Pawn_JobTracker.EndCurrentJob))]
        public static class Patch_Pawn_JobTracker_EndCurrentJob_MechanoidMechanitorRecreation
        {
            private readonly struct RecreationEndState
            {
                public readonly Pawn? Pawn;
                public readonly Job? Job;
                public readonly bool ShouldRoll;

                public RecreationEndState(Pawn? pawn, Job? job, bool shouldRoll)
                {
                    Pawn = pawn;
                    Job = job;
                    ShouldRoll = shouldRoll;
                }
            }

            // Prefix 只保存状态，不 Roll：
            // EndCurrentJob 内部会 CleanupCurrentJob，之后 curJob 会被置 null，
            // 因此必须在 Prefix 读取并保存 Job 与判定结果。
            [HarmonyPrefix]
            private static void Prefix(
                Pawn_JobTracker __instance,
                JobCondition condition,
                Pawn ___pawn,
                out RecreationEndState __state)
            {
                Job? job = __instance.curJob;

                bool shouldRoll =
                    condition == JobCondition.Succeeded
                    && MechanoidMechanitorRecreationUtility
                        .IsManagedRecreationJob(___pawn, job);

                __state = new RecreationEndState(___pawn, job, shouldRoll);
            }

            [HarmonyPostfix]
            private static void Postfix(RecreationEndState __state)
            {
                if (!__state.ShouldRoll || __state.Pawn == null)
                {
                    return;
                }

                if (!MechanoidMechanitorRecreationUtility.Enabled)
                {
                    return;
                }

                if (__state.Pawn.Dead)
                {
                    return;
                }

                if (__state.Pawn.Inspired)
                {
                    return;
                }

                if (!Rand.Chance(
                        MechanoidMechanitorRecreationUtility.InspirationChance))
                {
                    return;
                }

                MechanoidMechanitorInspirationUtility
                    .TryGrantRandomEligibleInspiration(
                        __state.Pawn,
                        sendLetter: true);
            }
        }
    }
}
