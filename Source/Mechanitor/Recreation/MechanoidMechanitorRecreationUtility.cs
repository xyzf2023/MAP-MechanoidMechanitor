using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械族机械师空闲娱乐中央工具。
    /// 负责维护白名单、系统开关、合法 Pawn 判定、ReadingTracker 初始化、
    /// “本系统产生的娱乐 Job”识别，以及 DEV 强制下一次空闲尝试娱乐的临时标记。
    /// 本功能不创建 Need_Joy，也不对普通机械族或普通殖民者产生任何影响。
    /// </summary>
    public static class MechanoidMechanitorRecreationUtility
    {
        /// <summary>
        /// 唯一权威的 JoyGiverDef 白名单。只允许这些原版娱乐活动被机械族机械师选择。
        /// Play_MusicalInstrument 来自 Royalty，找不到时直接跳过，不影响其余 12 项。
        /// </summary>
        private static readonly string[] AllowedJoyGiverDefNames =
        {
            "Skygaze",
            "GoForWalk",
            "ViewArt",
            "BuildSnowman",
            "Play_Horseshoes",
            "Play_Hoopstone",
            "Play_GameOfUr",
            "Play_Chess",
            "Play_Poker",
            "WatchTelevision",
            "UseTelescope",
            "Reading",
            "Play_MusicalInstrument"
        };

        private static List<JoyGiverDef>? cachedAllowedJoyGivers;
        private static HashSet<JobDef>? cachedAllowedJobDefs;

        /// <summary>
        /// DEV 临时标记：请求目标 Pawn 下一次空闲时强制尝试娱乐。
        /// 不需要保存；以 Pawn 弱引用为键，避免换局后 ThingID 复用误消费旧标记。
        /// </summary>
        private static readonly ConditionalWeakTable<Pawn, object> debugForceNextRecreationPawns =
            new ConditionalWeakTable<Pawn, object>();

        private static void EnsureCaches()
        {
            if (cachedAllowedJoyGivers != null)
            {
                return;
            }

            cachedAllowedJoyGivers = new List<JoyGiverDef>();
            cachedAllowedJobDefs = new HashSet<JobDef>();

            foreach (string defName in AllowedJoyGiverDefNames)
            {
                JoyGiverDef? def =
                    DefDatabase<JoyGiverDef>.GetNamedSilentFail(defName);
                if (def == null)
                {
                    continue;
                }

                cachedAllowedJoyGivers.Add(def);
                if (def.jobDef != null)
                {
                    cachedAllowedJobDefs.Add(def.jobDef);
                }
            }
        }

        /// <summary>
        /// 当前生效的白名单 JoyGiverDef 列表（按 DefDatabase 解析，找不到的已跳过）。
        /// </summary>
        public static IReadOnlyList<JoyGiverDef> AllowedJoyGivers
        {
            get
            {
                EnsureCaches();
                return cachedAllowedJoyGivers!;
            }
        }

        /// <summary>
        /// 该 JobDef 是否属于本系统白名单中的一项娱乐活动。
        /// 用于“本系统产生的娱乐 Job”识别时的第二道校验，不单独作为判定依据。
        /// </summary>
        public static bool IsWhitelistedJob(JobDef? jobDef)
        {
            if (jobDef == null)
            {
                return false;
            }

            EnsureCaches();
            return cachedAllowedJobDefs!.Contains(jobDef);
        }

        /// <summary>
        /// 系统总开关：由 MOD 设置控制。
        /// </summary>
        public static bool Enabled
        {
            get
            {
                return MAPMechanitorMod.Settings?.enableMechanoidMechanitorRecreation
                    == true;
            }
        }

        /// <summary>
        /// 空闲时尝试娱乐的概率（0~1）。
        /// </summary>
        public static float IdleRecreationChance
        {
            get
            {
                int percent =
                    MAPMechanitorMod.Settings
                        ?.mechanoidMechanitorIdleRecreationChancePercent
                    ?? 0;

                return Mathf.Clamp01(percent / 100f);
            }
        }

        /// <summary>
        /// 完整完成娱乐后获得灵感的概率（0~1）。
        /// </summary>
        public static float InspirationChance
        {
            get
            {
                int percent =
                    MAPMechanitorMod.Settings
                        ?.mechanoidMechanitorInspirationChancePercent
                    ?? 0;

                return Mathf.Clamp01(percent / 100f);
            }
        }

        /// <summary>
        /// 判定 Pawn 是否可以使用本娱乐系统。
        /// 要求：开启、是机械族机械师、机械族、玩家派系、存活、未倒地、未征召、
        /// 未精神崩溃、已生成、具有 jobs/mindState。
        /// 不要求 Pawn.IsColonist、Humanlike 或 Need_Joy。
        /// </summary>
        public static bool CanUseRecreationSystem(Pawn? pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            if (!Enabled)
            {
                return false;
            }

            if (!MechanoidMechanitorCapabilityUtility.HasCapability(pawn, MechanoidMechanitorCapability.Recreation))
            {
                return false;
            }

            if (pawn.RaceProps?.IsMechanoid != true)
            {
                return false;
            }

            if (pawn.Faction?.IsPlayer != true)
            {
                return false;
            }

            if (pawn.Dead)
            {
                return false;
            }

            if (pawn.Downed)
            {
                return false;
            }

            if (pawn.Drafted)
            {
                return false;
            }

            if (pawn.InMentalState)
            {
                return false;
            }

            if (!pawn.Spawned)
            {
                return false;
            }

            if (pawn.jobs == null)
            {
                return false;
            }

            if (pawn.mindState == null)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 确保机械族机械师拥有 ReadingTracker，以便 Reading 娱乐可以正常选书/取书。
        /// 仅在开启且确为本系统时初始化；不会删除已有 ReadingTracker。
        /// 即使玩家日后关闭设置，也不设置 pawn.reading = null，避免破坏已保存数据。
        /// </summary>
        public static void EnsureReadingTracker(Pawn? pawn)
        {
            if (pawn == null)
            {
                return;
            }

            if (!MechanoidMechanitorCapabilityUtility.HasCapability(pawn, MechanoidMechanitorCapability.Recreation))
            {
                return;
            }

            if (!Enabled)
            {
                return;
            }

            pawn.reading ??= new Pawn_ReadingTracker(pawn);
        }

        /// <summary>
        /// 判断给定 Job 是否为“本系统产生的娱乐 Job”。
        /// 必须同时满足：
        ///  1. Pawn 是机械族机械师；
        ///  2. JobDef 属于白名单；
        ///  3. job.jobGiver 是我们的 JobGiver。
        /// 这样其他系统或 MOD 偶然使用同一个原版 Chess/TV Job 时不会被误认。
        /// </summary>
        public static bool IsManagedRecreationJob(Pawn? pawn, Job? job)
        {
            if (pawn == null || job == null)
            {
                return false;
            }

            if (!IsWhitelistedJob(job.def)
                || !IsManagedRecreationJobGiver(
                    job.jobGiver as ThinkNode_JobGiver))
            {
                return false;
            }

            return MechanoidMechanitorCapabilityUtility.HasCapability(
                pawn, MechanoidMechanitorCapability.Recreation);
        }

        /// <summary>
        /// 判断一个 JobGiver 是否属于本 MOD 的娱乐入口。
        /// 目前包括：
        /// - Idle 随机娱乐 JobGiver_MechanoidMechanitorRecreation
        /// - Joy 作息调度 JobGiver_MechanoidMechanitorScheduledRecreation
        /// 两者都被认为是本 MOD 管理的娱乐 Job，
        /// 现有 JoyUtility.JoyTickCheckEnd / EndCurrentJob 灵感 Roll / TryOpportunisticJob
        /// 三套兼容仍能正确作用。
        /// </summary>
        public static bool IsManagedRecreationJobGiver(ThinkNode_JobGiver? jobGiver)
        {
            return jobGiver is JobGiver_MechanoidMechanitorRecreation
                || jobGiver is JobGiver_MechanoidMechanitorScheduledRecreation;
        }

        /// <summary>
        /// 参考原版 JobGiver_GetJoy，从白名单中选择一个当前合法的娱乐 Job。
        /// 完全删除 Need_Joy、JoyTolerance、BoredOf 等逻辑；仅保留
        /// CanBeGivenTo / pctPawnsEverDo / GetChance / TryGiveJob，
        /// 并在抽中的 giver 给不出 Job 时继续尝试其他候选。
        /// </summary>
        public static Job? TryMakeRecreationJob(Pawn pawn)
        {
            if (pawn == null)
            {
                return null;
            }

            EnsureCaches();

            List<JoyGiverDef> remaining = new List<JoyGiverDef>();
            Dictionary<JoyGiverDef, float> weights =
                new Dictionary<JoyGiverDef, float>();

            foreach (JoyGiverDef def in cachedAllowedJoyGivers!)
            {
                if (def == null)
                {
                    continue;
                }

                if (!def.Worker.CanBeGivenTo(pawn))
                {
                    continue;
                }

                if (def.pctPawnsEverDo < 1f)
                {
                    bool allowedByPct;
                    Rand.PushState(pawn.thingIDNumber ^ 0x3C49C49);
                    try
                    {
                        allowedByPct = Rand.Value < def.pctPawnsEverDo;
                    }
                    finally
                    {
                        Rand.PopState();
                    }

                    if (!allowedByPct)
                    {
                        continue;
                    }
                }

                float chance = def.Worker.GetChance(pawn);
                if (chance > 0f)
                {
                    remaining.Add(def);
                    weights[def] = chance;
                }
            }

            while (remaining.Count > 0)
            {
                if (!remaining.TryRandomElementByWeight(
                        d => Mathf.Max(0f, weights[d]),
                        out JoyGiverDef chosen))
                {
                    break;
                }

                Job? job = chosen.Worker.TryGiveJob(pawn);
                if (job != null)
                {
                    return job;
                }

                remaining.Remove(chosen);
                weights.Remove(chosen);
            }

            return null;
        }

        /// <summary>
        /// DEV：请求目标 Pawn 下一次空闲强制尝试娱乐。
        /// </summary>
        public static void RequestDebugForceNextRecreation(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            debugForceNextRecreationPawns.GetOrCreateValue(pawn);
        }

        /// <summary>
        /// DEV：尝试消费“下一次强制娱乐”标记，消费后返回 true。
        /// 只有 JobGiver 真正运行到合法机械族机械师时才消费。
        /// </summary>
        public static bool TryConsumeDebugForceNextRecreation(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            return debugForceNextRecreationPawns.Remove(pawn);
        }
    }
}
