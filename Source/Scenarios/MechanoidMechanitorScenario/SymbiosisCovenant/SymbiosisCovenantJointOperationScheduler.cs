using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using RimWorld.QuestGen;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 共生盟约「联合军事行动」的调度器（L4 专属，独立机制）。
    /// 复用共生盟约既有「以 GameComponent 为 key 的 ConditionalWeakTable + 三处 Harmony 钩子」模式：
    /// 不新增 GameComponent，也不修改共同防卫/联合贸易代表团的正式逻辑，
    /// 仅在盟约状态组件的 Tick / ExposeData / 等级重算 三个钩子上追加本机制行为。
    /// </summary>
    public static class SymbiosisCovenantJointOperationScheduler
    {
        private const int SchedulerIntervalTicks = 2500;
        private const int TicksPerDay = 60000;

        // 调度器自身的运行状态（冷却 / 下次每日检查时间），随盟约状态组件存档。
        private sealed class ScheduleState
        {
            public int nextCheckTick = -1;
            public int cooldownEndTick;
        }

        private static readonly ConditionalWeakTable<
            GameComponent_SymbiosisCovenantState,
            ScheduleState> States = new ConditionalWeakTable<
                GameComponent_SymbiosisCovenantState,
                ScheduleState>();

        private static ScheduleState GetState(GameComponent_SymbiosisCovenantState component)
        {
            return States.GetOrCreateValue(component);
        }

        /// <summary>
        /// 由 QuestPart 在行动结束（拒绝/过期/成功/失败/无效结束）时调用，
        /// 设置「下一次可生成邀请」的冷却到期时间。冷却期间调度器完全不生成邀请。
        /// </summary>
        public static void SetCooldownEndTick(int tick)
        {
            GameComponent_SymbiosisCovenantState? component =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (component == null || Find.TickManager == null)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            ScheduleState state = GetState(component);
            // 只在更晚时更新，避免并发结算把冷却提前。
            if (tick > state.cooldownEndTick)
            {
                state.cooldownEndTick = tick;
            }

            // 冷却生效期间不保留已排定的每日检查（由冷却统一门控）。
            if (state.cooldownEndTick > now)
            {
                state.nextCheckTick = -1;
            }
        }

        public static int GetCooldownRemainingTicks()
        {
            GameComponent_SymbiosisCovenantState? component =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (component == null || Find.TickManager == null)
            {
                return 0;
            }

            int cooldown = GetState(component).cooldownEndTick;
            return Math.Max(0, cooldown - Find.TickManager.TicksGame);
        }

        public static float GetDaysUntilNext()
        {
            GameComponent_SymbiosisCovenantState? component =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (component == null || Find.TickManager == null)
            {
                return -1f;
            }

            int next = GetState(component).nextCheckTick;
            if (next < 0)
            {
                return -1f;
            }

            return Math.Max(0f, (next - Find.TickManager.TicksGame) / (float)TicksPerDay);
        }

        /// <summary>
        /// 盟约等级重算后调用：等级达到 L4 时安排首次每日检查；低于 L4 时暂停调度。
        /// 不清除冷却（冷却由行动结束逻辑设置，等级恢复后仍应遵守）。
        /// </summary>
        public static void HandleLevelRecalculated(GameComponent_SymbiosisCovenantState component)
        {
            ScheduleState state = GetState(component);
            if (!GameComponent_SymbiosisCovenantState.IsActive || component.CovenantLevel < 4)
            {
                state.nextCheckTick = -1;
                return;
            }

            if (state.nextCheckTick < 0 && Find.TickManager != null)
            {
                state.nextCheckTick = Find.TickManager.TicksGame + TicksPerDay;
            }
        }

        /// <summary>
        /// 每 2500 ticks 由 Harmony Postfix 调用一次。实际目标筛选只在到达每日检查点时进行，
        /// 避免在每次 Tick 扫描世界对象（符合「每 60000 ticks 检查一次」的设计）。
        /// </summary>
        public static void Tick(GameComponent_SymbiosisCovenantState component)
        {
            if (Find.TickManager == null
                || Find.TickManager.TicksGame % SchedulerIntervalTicks != 0)
            {
                return;
            }

            ScheduleState state = GetState(component);

            // 盟约未激活或等级不足：暂停，待等级重算钩子重新安排。
            if (!GameComponent_SymbiosisCovenantState.IsActive || component.CovenantLevel < 4)
            {
                state.nextCheckTick = -1;
                return;
            }

            int now = Find.TickManager.TicksGame;

            // 冷却中：完全跳过生成，连每日检查都不需要。
            if (state.cooldownEndTick > now)
            {
                return;
            }

            // 已有进行中的行动：不要重复生成邀请（行动结束后才会设置冷却）。
            if (SymbiosisCovenantJointOperationUtility.IsJointOperationOngoing())
            {
                return;
            }

            if (state.nextCheckTick < 0)
            {
                // 首次调度：等一天后再真正筛选，避免加载即触发。
                state.nextCheckTick = now + TicksPerDay;
                return;
            }

            if (now < state.nextCheckTick)
            {
                return;
            }

            // 到达每日检查点：尝试生成邀请。无论成功失败都排定下一次每日检查
            // （成功时由 IsJointOperationOngoing 兜底拦截，不会重复生成）。
            TryGenerateOffer();
            state.nextCheckTick = now + TicksPerDay;
        }

        public static void ExposeData(GameComponent_SymbiosisCovenantState component)
        {
            ScheduleState state = GetState(component);
            Scribe_Values.Look(
                ref state.nextCheckTick,
                "symbiosisCovenantJointOpNextCheckTick",
                -1);
            Scribe_Values.Look(
                ref state.cooldownEndTick,
                "symbiosisCovenantJointOpCooldownEndTick",
                0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (state.nextCheckTick < -1)
                {
                    state.nextCheckTick = -1;
                }

                if (state.cooldownEndTick < 0)
                {
                    state.cooldownEndTick = 0;
                }
            }
        }

        /// <summary>
        /// 生成一次联合军事行动邀请。所有资格判定（L4、无进行中行动、暴力任务许可、
        /// 真实存在的敌方世界目标、发起者与参与派系）均在此处与 Utility 中完成。
        /// 绝不凭空生成敌方据点。
        ///
        /// 正式调度传 null/false；DEV 可传精确目标与参与派系以跳过自然随机与概率。
        /// </summary>
        public static bool TryGenerateOffer()
        {
            return TryGenerateOffer(null, null, false, out _);
        }

        /// <summary>
        /// 可指定精确目标与参与派系的邀请生成入口。
        /// devForced=true 时跳过自然等待时间、随机选择、自然概率、普通前哨生成频率/数量限制，
        /// 但仍保留 target 类型合法性、参与派系能生成援军、目标尚未被销毁、当前无另一项活动行动等硬性校验。
        /// </summary>
        public static bool TryGenerateOffer(
            WorldObject? forcedTarget,
            IReadOnlyList<Faction>? forcedParticipants,
            bool devForced,
            out string reason)
        {
            reason = string.Empty;

            GameComponent_SymbiosisCovenantState? component =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (component == null || !GameComponent_SymbiosisCovenantState.IsActive)
            {
                reason = "covenantInactive";
                return false;
            }

            if (component.CovenantLevel < 4)
            {
                reason = "covenantLevelBelowL4";
                return false;
            }

            // 已有进行中行动则不重复生成。
            if (SymbiosisCovenantJointOperationUtility.IsJointOperationOngoing())
            {
                reason = "operationOngoing";
                return false;
            }

            // 暴力军事行动需世界设定允许（DEV 强制也尊重此设定）。
            if (!SymbiosisCovenantJointOperationUtility.ViolentQuestsAllowed)
            {
                reason = "violentQuestsDisallowed";
                return false;
            }

            SymbiosisCovenantJointOperationDef? def =
                SymbiosisCovenantJointOperationDefOf.MAP_SymbiosisCovenant_JointOperationConfig;
            if (def == null)
            {
                reason = "missingDef";
                return false;
            }

            // 1）目标：DEV 可指定精确目标，否则从真实存在的敌方世界对象中按优先级选目标。
            WorldObject? target;
            Faction? targetFaction;
            if (forcedTarget != null)
            {
                if (!SymbiosisCovenantJointOperationUtility.IsEligibleTarget(
                        forcedTarget,
                        out Faction? eligibleFaction,
                        out string invalidReason))
                {
                    reason = "targetIneligible:" + invalidReason;
                    return false;
                }

                target = forcedTarget;
                targetFaction = eligibleFaction;
            }
            else
            {
                target = SymbiosisCovenantJointOperationUtility.SelectTargetWorldObject(
                    out targetFaction);
            }

            if (target == null || targetFaction == null)
            {
                reason = "noTarget";
                return false;
            }

            // 2）参与派系：DEV 可指定精确参与者（含发起者），否则按 Trust 加权选择。
            List<Faction> participants;
            Faction? proposer;
            if (forcedParticipants != null && forcedParticipants.Count > 0)
            {
                participants = forcedParticipants.ToList();
                // 校验每个参与者资格（与目标不同派系、非玩家、能生成 Combat 编组、
                // 与目标敌对、非战败/隐藏/临时）。
                foreach (Faction participant in participants)
                {
                    if (!SymbiosisCovenantJointOperationUtility.IsEligibleParticipant(
                            participant,
                            targetFaction))
                    {
                        reason = "participantIneligible:" + participant.Name;
                        return false;
                    }
                }

                proposer = participants[0];
            }
            else
            {
                if (!SymbiosisCovenantJointOperationUtility.SelectProposerAndParticipants(
                        def,
                        targetFaction,
                        out proposer,
                        out participants))
                {
                    reason = "noParticipants";
                    return false;
                }
            }

            if (proposer ==  null || participants.Count == 0)
            {
                reason = "noParticipants";
                return false;
            }

            // 参与派系最多 maxParticipants，包括发起者。
            if (participants.Count > def.maxParticipants)
            {
                participants = participants.Take(def.maxParticipants).ToList();
                proposer = participants[0];
            }

            // 3）奖励估值：仅用于（当前已禁用的）实物奖励价值估算，不影响援军规模。
            //    援军规模由 QuestPart 在部署时使用真实目标威胁点（TryGetTargetThreatPointsAtDeployment），
            //    绝不在此处用 StorytellerUtility.DefaultThreatPointsNow 估算。
            float threat = EstimateThreatPoints();
            int rewardValue = SymbiosisCovenantJointOperationUtility.ComputeRewardValue(threat, def);

            // 4）唯一行动标识，便于日志与未来排查。
            string actionId = "MAP_SymbiosisCovenantJointOp_"
                + (Find.TickManager?.TicksGame ?? 0)
                + "_"
                + Rand.Int;

            // 5）把运行期数据写入 Slate，交给真实 Quest 系统生成。
            Slate slate = new Slate();
            slate.Set("targetWorldObject", target);
            slate.Set("targetFaction", targetFaction);
            slate.Set("proposerFaction", proposer);
            slate.Set("participants", participants);
            slate.Set("jointOperationDef", def);
            slate.Set("actionId", actionId);
            slate.Set("rewardValue", rewardValue);

            QuestScriptDef? questScriptDef =
                SymbiosisCovenantJointOperationQuestScriptDefOf.MAP_SymbiosisCovenantJointOperation;
            if (questScriptDef == null)
            {
                reason = "missingQuestScriptDef";
                return false;
            }

            Quest? quest = QuestUtility.GenerateQuestAndMakeAvailable(questScriptDef, slate);
            if (quest == null)
            {
                reason = "questGenerateFailed";
                return false;
            }

            // 生成后主动发送「新任务可用」信件，让玩家在任务面板看到邀请。
            QuestUtility.SendLetterQuestAvailable(quest);
            return true;
        }

        private static float EstimateThreatPoints()
        {
            Map? map = Find.AnyPlayerHomeMap ?? Find.CurrentMap;
            if (map != null)
            {
                return StorytellerUtility.DefaultThreatPointsNow(map);
            }

            return 1000f;
        }

        // ===== DEV 工具（便于 QA 直接验证 L4 机制） =====

        public static bool DevSpawnNow()
        {
            if (!Prefs.DevMode)
            {
                return false;
            }

            // 无视冷却，但尊重「已有进行中行动」与 L4 条件。
            return TryGenerateOffer();
        }

        /// <summary>
        /// DEV 专用：针对精确目标与已准备好的参与派系立即生成邀请。
        /// 仅绕过自然等待时间、随机选择、自然概率、普通前哨生成频率/数量限制，
        /// 不绕过目标合法性、参与者资格、目标未被销毁、当前无活动行动等硬性校验。
        /// </summary>
        public static bool DevSpawnNowForTarget(
            WorldObject target,
            IReadOnlyList<Faction> preparedParticipants,
            out string reason)
        {
            reason = string.Empty;
            if (!Prefs.DevMode)
            {
                reason = "devModeRequired";
                return false;
            }

            if (target == null || preparedParticipants == null || preparedParticipants.Count == 0)
            {
                reason = "invalidArguments";
                return false;
            }

            return TryGenerateOffer(target, preparedParticipants, true, out reason);
        }

        public static bool DevMakeDueNow()
        {
            if (!Prefs.DevMode || Find.TickManager == null)
            {
                return false;
            }

            GameComponent_SymbiosisCovenantState? component =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (component == null || component.CovenantLevel < 4)
            {
                return false;
            }

            GetState(component).nextCheckTick = Find.TickManager.TicksGame;
            return true;
        }

        public static bool DevClearCooldown()
        {
            if (!Prefs.DevMode)
            {
                return false;
            }

            GameComponent_SymbiosisCovenantState? component =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (component == null)
            {
                return false;
            }

            GetState(component).cooldownEndTick = 0;
            return true;
        }

        public sealed class SymbiosisCovenantJointOperationDevSnapshot
        {
            public int CovenantLevel;
            public bool Available;
            public int NextTick;
            public float DaysUntilNext;
            public int CooldownRemainingTicks;
            public bool Ongoing;
            public string? TargetLabel;
            public string? TargetQuestTag;
            public string Stage = "-";
            public int ParticipantsCount;
            public int RewardValue;
            public int TargetThreatPointsAtDeployment;
            public float TotalSupportPointsAtDeployment;
            public int TrackedLordCount;
            public List<string>? PerFactionSupport;
        }

        public static SymbiosisCovenantJointOperationDevSnapshot? GetDevSnapshot(
            GameComponent_SymbiosisCovenantState component)
        {
            if (!GameComponent_SymbiosisCovenantState.IsActive || component.CovenantLevel < 4)
            {
                return null;
            }

            ScheduleState state = GetState(component);
            QuestPart_SymbiosisCovenantJointOperation? part =
                SymbiosisCovenantJointOperationUtility.FindActiveOperationPart();

            int lordCount = 0;
            List<string>? perFaction = null;
            if (part != null)
            {
                Map? targetMap = (part.targetWorldObject as MapParent)?.Map;
                if (targetMap != null && part.spawnedAidTags != null)
                {
                    foreach (string tag in part.spawnedAidTags)
                    {
                        lordCount += QuestPart_SymbiosisCovenantJointOperation
                            .CountTaggedJointOpLordsPublic(targetMap, tag);
                    }
                }

                if (part.supportRecords != null)
                {
                    perFaction = new List<string>();
                    foreach (SymbiosisCovenantJointOperationFactionSupportRecord record in part.supportRecords)
                    {
                        perFaction.Add(
                            (record.faction?.Name ?? "???")
                            + " | points="
                            + record.supportPoints.ToString("F0")
                            + " | pawns="
                            + record.pawnCount
                            + " | aidTag="
                            + (record.aidTag ?? "-"));
                    }
                }
            }

            return new SymbiosisCovenantJointOperationDevSnapshot
            {
                CovenantLevel = component.CovenantLevel,
                Available = true,
                NextTick = state.nextCheckTick,
                DaysUntilNext = GetDaysUntilNext(),
                CooldownRemainingTicks = GetCooldownRemainingTicks(),
                Ongoing = part != null,
                TargetLabel = part?.targetWorldObject?.Label ?? null,
                TargetQuestTag = part?.targetQuestTag ?? null,
                Stage = part?.stage.ToString() ?? "-",
                ParticipantsCount = part?.participantFactions?.Count ?? 0,
                RewardValue = part?.rewardValue ?? 0,
                TargetThreatPointsAtDeployment = part?.targetThreatPointsAtDeployment ?? 0,
                TotalSupportPointsAtDeployment = part?.totalSupportPointsAtDeployment ?? 0f,
                TrackedLordCount = lordCount,
                PerFactionSupport = perFaction
            };
        }

        // ===== 三处 Harmony 钩子：仅追加本机制行为，不改写既有方法体 =====

        [HarmonyPatch(typeof(GameComponent_SymbiosisCovenantState), "RecalculateCovenantLevel")]
        public static class SymbiosisCovenantJointOperationLevelRecalculatedPatch
        {
            public static void Postfix(GameComponent_SymbiosisCovenantState __instance)
            {
                HandleLevelRecalculated(__instance);
                QuestPart_SymbiosisCovenantJointOperation.NotifyCovenantLevelChanged(
                    __instance.CovenantLevel);
            }
        }

        [HarmonyPatch(typeof(GameComponent_SymbiosisCovenantState), nameof(GameComponent_SymbiosisCovenantState.GameComponentTick))]
        public static class SymbiosisCovenantJointOperationGameComponentTickPatch
        {
            public static void Postfix(GameComponent_SymbiosisCovenantState __instance)
            {
                Tick(__instance);
            }
        }

        [HarmonyPatch(typeof(GameComponent_SymbiosisCovenantState), nameof(GameComponent_SymbiosisCovenantState.ExposeData))]
        public static class SymbiosisCovenantJointOperationExposeDataPatch
        {
            public static void Postfix(GameComponent_SymbiosisCovenantState __instance)
            {
                ExposeData(__instance);
            }
        }
    }

    /// <summary>
    /// 联合军事行动 QuestScriptDef 的 DefOf 引用。Def 本体在
    /// 1.6/Defs/Misc/MAP_SymbiosisCovenant_JointOperation.xml 中定义。
    /// </summary>
    [DefOf]
    public static class SymbiosisCovenantJointOperationQuestScriptDefOf
    {
        public static QuestScriptDef MAP_SymbiosisCovenantJointOperation = null!;

        static SymbiosisCovenantJointOperationQuestScriptDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(
                typeof(SymbiosisCovenantJointOperationQuestScriptDefOf));
        }
    }
}
