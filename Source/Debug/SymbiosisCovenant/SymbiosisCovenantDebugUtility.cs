using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 共生盟约 Debug / 测试工具的统一可复用 API。
    /// 不注册任何 DebugAction；只包装现有 Dev* / Utility API，
    /// 以便 DEV 窗口、Debug Actions 与未来 AutoTest / Harness 都能直接调用，
    /// 而不必模拟点击控制台 UI。
    /// 除“包装已有业务逻辑”的极小副作用外，本类不实现任何新的共生盟约玩法。
    /// </summary>
    public static class SymbiosisCovenantDebugUtility
    {
        private static readonly float[] TestRaidPoints =
        {
            500f,
            1000f,
            3000f,
            10000f,
            30000f,
            50000f
        };

        /// <summary>
        /// 标准测试袭击可选的 Raid 点数档位（不自行 Clamp，原样传入 IncidentParms.points）。
        /// </summary>
        public static IReadOnlyList<float> RaidPointOptions
            => TestRaidPoints;

        private const string DevTrustReason = "DEV 信任测试";

        // ===== 统一获取共生盟约 State =====

        public static bool TryGetActiveState(
            out GameComponent_SymbiosisCovenantState? state,
            out string message)
        {
            state = GameComponent_SymbiosisCovenantState.CurrentComponent;

            if (state == null)
            {
                message = "当前存档没有共生盟约 GameComponent。";
                return false;
            }

            if (!GameComponent_SymbiosisCovenantState.IsActive)
            {
                message =
                    "当前剧本状态未启用共生盟约，"
                    + "请先通过“切换剧本状态 → 共生盟约”启用。";
                return false;
            }

            // 仅首次未完成初始化时建立必要记录；已初始化后，
            // Debug 动作不得为“获取状态”而主动推进提案、团结度日更、
            // 成员退出等正式业务。
            if (!state.Initialized)
            {
                state.SynchronizeNow();
            }

            message = string.Empty;
            return true;
        }

        // ===== 统一 DevMode 检查（写状态 API 必须先过此关） =====

        private static bool TryRequireDevMode(out string message)
        {
            if (!Prefs.DevMode)
            {
                message = "当前未启用开发者模式。";
                return false;
            }

            message = string.Empty;
            return true;
        }

        // ===== 窗口相关 =====

        public static bool TryOpenCovenantWindow(out string message)
        {
            if (!TryGetActiveState(out _, out message))
            {
                return false;
            }

            Find.WindowStack.Add(new Dialog_SymbiosisCovenant());

            message = "已打开共生盟约窗口。";
            return true;
        }

        public static bool TryOpenCovenantDevWindow(out string message)
        {
            if (!Prefs.DevMode)
            {
                message = "当前未启用开发者模式。";
                return false;
            }

            if (!TryGetActiveState(out _, out message))
            {
                return false;
            }

            Find.WindowStack.Add(new Dialog_SymbiosisCovenantDev(null));

            message = "已打开共生盟约 DEV 面板。";
            return true;
        }

        // ===== 团结度与等级 =====

        public static bool TrySetUnity(float value, out string message)
        {
            if (!TryRequireDevMode(out message))
            {
                return false;
            }

            if (!TryGetActiveState(
                    out GameComponent_SymbiosisCovenantState? state,
                    out message)
                || state == null)
            {
                return false;
            }

            state.DevSetUnity(value);

            message =
                "团结度已设置为 "
                + state.Unity.ToString("F0")
                + "；当前盟约等级 L"
                + state.CovenantLevel
                + "。";

            return true;
        }

        public static bool TryChangeUnity(float delta, out string message)
        {
            if (!TryRequireDevMode(out message))
            {
                return false;
            }

            if (!TryGetActiveState(
                    out GameComponent_SymbiosisCovenantState? state,
                    out message)
                || state == null)
            {
                return false;
            }

            state.DevChangeUnity(delta);

            message =
                "团结度已变更为 "
                + (delta >= 0f ? "+" : string.Empty)
                + delta.ToString("F0")
                + "；当前团结度 "
                + state.Unity.ToString("F0")
                + "；盟约等级 L"
                + state.CovenantLevel
                + "。";

            return true;
        }

        public static bool TryUpdateUnityDaily(out string message)
        {
            if (!TryRequireDevMode(out message))
            {
                return false;
            }

            if (!TryGetActiveState(
                    out GameComponent_SymbiosisCovenantState? state,
                    out message)
                || state == null)
            {
                return false;
            }

            state.DevUpdateUnityDaily();

            message =
                "已立即执行一次每日团结度更新；当前团结度 "
                + state.Unity.ToString("F0")
                + "；盟约等级 L"
                + state.CovenantLevel
                + "。";

            return true;
        }

        public static bool TryRecalculateLevel(out string message)
        {
            if (!TryRequireDevMode(out message))
            {
                return false;
            }

            if (!TryGetActiveState(
                    out GameComponent_SymbiosisCovenantState? state,
                    out message)
                || state == null)
            {
                return false;
            }

            state.DevRecalculateCovenantLevel();

            message =
                "已重新计算盟约等级；当前等级 L"
                + state.CovenantLevel
                + "（成员数 "
                + state.CovenantMemberCount
                + "，团结度 "
                + state.Unity.ToString("F0")
                + "）。";

            return true;
        }

        /// <summary>
        /// DEV 专用一键测试：准备盟约/派系状态并生成一个精确的「联合军事行动」邀请。
        /// 点击后只执行与本次测试准备有关的修改，不触发正式调度结算（如每日 Unity、提案）。
        /// </summary>
        public static bool TryPrepareAndSpawnJointOperationTest(out string message)
        {
            message = string.Empty;

            if (!TryRequireDevMode(out message))
            {
                return false;
            }

            if (Current.Game == null || Find.World == null)
            {
                message = "没有活动存档。";
                return false;
            }

            if (!GameComponent_SymbiosisCovenantState.IsActive
                || (GameComponent_SymbiosisCovenantState.CurrentComponent?.Initialized != true))
            {
                message = "共生盟约尚未初始化。";
                return false;
            }

            // 已有进行中行动：不擅自结束，提示先清除。
            if (SymbiosisCovenantJointOperationUtility.IsJointOperationOngoing())
            {
                message = "已有一个活动联合军事行动，请先清除现有联合行动。";
                return false;
            }

            // 暴力军事行动需世界设定允许。
            if (!SymbiosisCovenantJointOperationUtility.ViolentQuestsAllowed)
            {
                message = "暴力军事行动被当前世界设定禁用。";
                return false;
            }

            GameComponent_SymbiosisCovenantState state =
                GameComponent_SymbiosisCovenantState.CurrentComponent!;
            Faction? playerFaction = Faction.OfPlayer;
            if (playerFaction == null)
            {
                message = "找不到玩家派系。";
                return false;
            }

            // 基础候选：非玩家、非永久敌对、非隐藏/临时、未战败/停用、能生成 Combat 编组。
            List<Faction> baseCandidates = new List<Faction>();
            foreach (Faction faction in Find.FactionManager.AllFactionsListForReading)
            {
                if (faction == null
                    || faction.IsPlayer
                    || faction.def.permanentEnemy
                    || faction.Hidden
                    || faction.temporary
                    || faction.defeated
                    || faction.deactivated)
                {
                    continue;
                }

                if (!SymbiosisCovenantJointOperationUtility.CanGenerateCombatGroup(faction))
                {
                    continue;
                }

                baseCandidates.Add(faction);
            }

            if (baseCandidates.Count == 0)
            {
                message = "找不到能生成战斗编组的合法派系。";
                return false;
            }

            // A. 目标派系候选：还需符合前哨生成资格（普通派系），且不能是当前盟约成员。
            // 没有盟约记录（record == null）的派系仍可作为目标；只排除已是成员者。
            List<Faction> targetCandidates = baseCandidates
                .Where(f => IsValidJointOperationTestTarget(state, f))
                .ToList();
            if (targetCandidates.Count == 0)
            {
                message =
                    "找不到可作为敌方目标的非盟约普通派系。"
                    + "需要至少一个未加入盟约、能够生成战斗编组且符合前哨生成资格的普通派系。";
                return false;
            }

            // 优先已敌对玩家的；否则取第一个并在 DEV 中转为敌对。
            Faction? targetFaction = targetCandidates
                .FirstOrDefault(f => f.HostileTo(playerFaction));
            if (targetFaction == null)
            {
                targetFaction = targetCandidates[0];
            }

            // 选出再次验证：目标不能是盟约成员（不为自动退出成员，避免破坏用户成员状态）。
            if (state.GetRecord(targetFaction)?.CovenantMember == true)
            {
                message = "选中的目标派系仍是盟约成员，已停止一键测试：" + targetFaction.Name;
                return false;
            }

            // 将目标派系与玩家设为敌对（-100），并验证。
            if (!TrySetDevGoodwill(targetFaction, playerFaction, -100, out string targetRelReason))
            {
                message = "无法将目标派系与玩家设为敌对：" + targetRelReason;
                return false;
            }

            if (!targetFaction.HostileTo(playerFaction))
            {
                message = "目标派系与玩家未成功变为敌对（关系可能被锁定）。";
                return false;
            }

            // B. 参与派系候选：非玩家、非目标、普通可记录派系；
            //    不在此要求已与目标敌对（关系将在下面准备）。
            List<Faction> participantCandidates = baseCandidates
                .Where(f => f != targetFaction
                    && MechanoidMechanitorOrdinaryFactionUtility.IsOrdinaryFaction(f))
                .ToList();
            if (participantCandidates.Count == 0)
            {
                message = "找不到可作为参与派系的合法普通派系。";
                return false;
            }

            // 取前至多 3 个作为参与者（含发起者，participants[0] 即发起者）。
            List<Faction> participants = participantCandidates
                .Take(3)
                .ToList();

            // 为每个参与派系准备：记录、关系、信任、盟约成员。
            foreach (Faction participant in participants)
            {
                if (!PrepareParticipantForJointOperation(
                        state,
                        participant,
                        targetFaction,
                        playerFaction,
                        out string prepMessage))
                {
                    message = prepMessage;
                    return false;
                }
            }

            // 确保盟约等级至少达到联合军事行动解锁等级：团结度设为 L2 阈值并重新计算。
            state.DevSetUnity(
                GameComponent_SymbiosisCovenantState.CovenantLevel2UnityThreshold);
            state.DevRecalculateCovenantLevel();

            int unlockLevel = SymbiosisCovenantJointOperationDef.MinimumCovenantLevel;
            if (state.CovenantLevel < unlockLevel)
            {
                message = "盟约等级不足 L" + unlockLevel
                    + "（当前 L" + state.CovenantLevel + "）。";
                return false;
            }

            if (state.CovenantMemberCount < 1)
            {
                message = "盟约成员数量不足（当前 " + state.CovenantMemberCount + "）。";
                return false;
            }

            // 生成前哨前再验证：目标派系在此期间不应成为盟约成员
            // （不自动让其退出盟约，避免破坏用户已有成员状态）。
            if (state.GetRecord(targetFaction)?.CovenantMember == true)
            {
                message = "目标派系在测试准备期间加入了盟约，已停止生成前哨：" + targetFaction.Name;
                return false;
            }

            // 为目标派系生成一个「建成」的前哨并获得精确引用。
            if (!FactionOutpostGenerationUtility.TryDevGenerateCompletedOutpost(
                    targetFaction,
                    out MAPFactionOutpost outpost,
                    out string outpostMessage))
            {
                message =
                    "前哨生成失败：" + outpostMessage
                    + "（已为以下派系准备关系：" + targetFaction.Name + "；"
                    + "已加入成员：" + string.Join("、", participants.Select(f => f.Name)) + "）";
                return false;
            }

            // 立即生成前哨后需验证引用与状态。
            if (outpost == null
                || !outpost.Spawned
                || !outpost.IsCompleted
                || outpost.Faction != targetFaction
                || !targetFaction.HostileTo(playerFaction))
            {
                message =
                    "前哨生成后状态校验失败（Spawned="
                    + (outpost?.Spawned ?? false)
                    + "；Completed="
                    + (outpost?.IsCompleted ?? false)
                    + "；前哨派系匹配="
                    + (outpost?.Faction == targetFaction)
                    + "）。";
                return false;
            }

            // 清除调度冷却（仅保证下次自然调度可用，不影响下面的立即生成）。
            SymbiosisCovenantJointOperationScheduler.DevClearCooldown();

            // 针对精确前哨与准备好的参与派系立即生成邀请。
            // 防御性去重并限制数量，避免重复援军。
            participants = participants
                .Where(f => f != null)
                .Distinct()
                .Take(SymbiosisCovenantJointOperationDefOf
                    .MAP_SymbiosisCovenant_JointOperationConfig.maxParticipants)
                .ToList();

            if (!SymbiosisCovenantJointOperationScheduler.DevSpawnNowForTarget(
                    outpost,
                    participants,
                    out string spawnReason))
            {
                message =
                    "邀请创建失败：" + spawnReason
                    + "（前哨已生成：" + outpost.Label + "；"
                    + "已准备参与派系：" + string.Join("、", participants.Select(f => f.Name)) + "）";
                return false;
            }

            message =
                "已生成联合军事行动测试邀请。\n"
                + "目标派系：" + targetFaction.Name + "\n"
                + "目标前哨：" + outpost.Label + "\n"
                + "发起派系：" + participants[0].Name + "\n"
                + "参与派系：" + string.Join("、", participants.Select(f => f.Name)) + "\n"
                + "盟约等级：L" + state.CovenantLevel + "\n"
                + "团结度：" + state.Unity.ToString("F0");

            return true;
        }

        /// <summary>
        /// 判断一个派系是否可作为一键联合军事行动的“敌方目标”候选：
        /// 必须符合前哨生成资格（普通派系），且当前不是盟约成员。
        /// 没有盟约记录（record == null）是允许的，只排除已是成员者。
        /// </summary>
        private static bool IsValidJointOperationTestTarget(
            GameComponent_SymbiosisCovenantState state,
            Faction faction)
        {
            if (faction == null)
            {
                return false;
            }

            if (!FactionOutpostFactionUtility.IsEligibleFaction(faction))
            {
                return false;
            }

            SymbiosisCovenantFactionRecord? record = state.GetRecord(faction);
            return record?.CovenantMember != true;
        }

        /// <summary>
        /// 为一个参与派系可靠地准备联合军事行动所需状态：
        /// 确保记录存在、与玩家非敌对、与目标敌对、信任≥25、并加入盟约。
        /// 不依赖可能为 null 的 GetRecord 结果，缺失记录会创建并重新读取。
        /// </summary>
        private static bool PrepareParticipantForJointOperation(
            GameComponent_SymbiosisCovenantState state,
            Faction participant,
            Faction targetFaction,
            Faction playerFaction,
            out string message)
        {
            message = string.Empty;

            // 1. 确保记录存在（缺失则创建并重新读取）。
            SymbiosisCovenantFactionRecord? record = state.GetRecord(participant);
            if (record == null)
            {
                if (!state.DevRecreateRecord(participant))
                {
                    message = "无法为参与派系创建盟约记录：" + participant.Name;
                    return false;
                }

                record = state.GetRecord(participant);
            }

            if (record == null)
            {
                message = "参与派系的盟约记录创建后仍为空：" + participant.Name;
                return false;
            }

            // 2. 参与派系与玩家设为非敌对（至少 0 好感）。
            if (participant.HostileTo(playerFaction))
            {
                if (!TrySetDevGoodwill(participant, playerFaction, 0, out string ppReason))
                {
                    message = "无法将参与派系与玩家设为非敌对："
                        + participant.Name + "（" + ppReason + "）";
                    return false;
                }

                if (participant.HostileTo(playerFaction))
                {
                    message = "参与派系与玩家未成功解除敌对：" + participant.Name;
                    return false;
                }
            }

            // 3. 参与派系与目标设为敌对（-100 好感）。
            if (!participant.HostileTo(targetFaction))
            {
                if (!TrySetDevGoodwill(participant, targetFaction, -100, out string ptReason))
                {
                    message = "无法将参与派系与目标设为敌对："
                        + participant.Name + "（" + ptReason + "）";
                    return false;
                }

                if (!participant.HostileTo(targetFaction))
                {
                    message = "参与派系与目标未成功变为敌对：" + participant.Name;
                    return false;
                }
            }

            // 4. 信任至少 25。
            record = state.GetRecord(participant);
            if (record == null)
            {
                message = "参与派系记录在读回时为空：" + participant.Name;
                return false;
            }

            if (record.Trust < 25)
            {
                if (!state.DevSetTrust(participant, 25, DevTrustReason))
                {
                    message = "无法设置参与派系信任度：" + participant.Name;
                    return false;
                }
            }

            // 5. 若尚未加入，加入盟约。
            record = state.GetRecord(participant);
            if (record == null)
            {
                message = "参与派系记录在读回时为空：" + participant.Name;
                return false;
            }

            if (!record.CovenantMember)
            {
                if (!state.DevForceJoinCovenant(participant))
                {
                    message = "无法让派系加入盟约：" + participant.Name;
                    return false;
                }
            }

            // 6. 重新读取并验证 CovenantMember 已生效。
            record = state.GetRecord(participant);
            if (record?.CovenantMember != true)
            {
                message = "派系加入盟约后状态仍未生效：" + participant.Name;
                return false;
            }

            return true;
        }

        /// <summary>
        /// DEV 内部：读取当前实际好感，计算差值并以正式关系 API 应用，
        /// 之后重新读取验证变更是否生效。不使用反射、不修改 FactionRelation 私有字段。
        /// </summary>
        internal static bool TrySetDevGoodwill(
            Faction a,
            Faction b,
            int desiredGoodwill,
            out string reason)
        {
            reason = string.Empty;
            int current = a.GoodwillWith(b);
            int delta = desiredGoodwill - current;
            if (delta == 0)
            {
                return true;
            }

            if (!a.TryAffectGoodwillWith(
                    b,
                    delta,
                    canSendMessage: false,
                    canSendHostilityLetter: false))
            {
                reason = "TryAffectGoodwillWith 未能应用好感变化（delta=" + delta + "）";
                return false;
            }

            // 重新读取实际关系，确认变更生效。
            int actual = a.GoodwillWith(b);
            if (Math.Abs(actual - desiredGoodwill) > 1)
            {
                reason = "实际好感 " + actual + " 与目标 " + desiredGoodwill
                    + " 不一致（可能受关系锁限制）";
                return false;
            }

            return true;
        }

        // ===== 公开宣言 =====

        public static bool TryBroadcastDeclaration(out string message)
        {
            if (!TryRequireDevMode(out message))
            {
                return false;
            }

            if (!TryGetActiveState(
                    out GameComponent_SymbiosisCovenantState? state,
                    out message)
                || state == null)
            {
                return false;
            }

            if (state.PublicDeclarationBroadcast)
            {
                message = "公开脱离声明已经广播过了。";
                return false;
            }

            if (!state.Initialized)
            {
                message = "共生盟约尚未完成初始化，无法广播宣言。";
                return false;
            }

            if (state.DevBroadcastDeclaration())
            {
                message = "公开脱离声明已广播。";
                return true;
            }

            message = "无法广播公开脱离声明（条件不满足）。";
            return false;
        }

        // ===== 派系列表 =====

        public static List<Faction> GetRecordedFactions()
        {
            GameComponent_SymbiosisCovenantState? state =
                GameComponent_SymbiosisCovenantState.CurrentComponent;

            if (state == null
                || !GameComponent_SymbiosisCovenantState.IsActive)
            {
                return new List<Faction>();
            }

            // 仅首次未完成初始化时建立必要记录；已初始化后，
            // 展开 Debug 菜单不应推动正式系统。
            if (!state.Initialized)
            {
                state.SynchronizeNow();
            }

            return state.GetRecordsSorted()
                .Where(record => record.Faction != null)
                .Select(record => record.Faction!)
                .ToList();
        }

        public static string FactionMenuLabel(Faction faction)
        {
            return faction.Name
                + " ["
                + faction.def.defName
                + "] ("
                + faction.loadID
                + ")";
        }

        // ===== 派系 Dev API 包装 =====

        public static bool TrySetTrust(
            Faction faction,
            int value,
            out string message)
        {
            if (faction == null)
            {
                message = "指定派系为空。";
                return false;
            }

            if (!TryRequireDevMode(out message))
            {
                return false;
            }

            if (!TryGetActiveState(
                    out GameComponent_SymbiosisCovenantState? state,
                    out message)
                || state == null)
            {
                return false;
            }

            if (state.GetRecord(faction) == null)
            {
                message = "该派系没有共生盟约记录。";
                return false;
            }

            state.DevSetTrust(
                faction,
                value,
                DevTrustReason);

            message =
                "已将 "
                + faction.Name
                + " 的信任度设置为 "
                + state.GetRecord(faction)!.Trust
                + "。";

            return true;
        }

        public static bool TryAdjustTrust(
            Faction faction,
            int amount,
            out string message)
        {
            if (faction == null)
            {
                message = "指定派系为空。";
                return false;
            }

            if (!TryRequireDevMode(out message))
            {
                return false;
            }

            if (!TryGetActiveState(
                    out GameComponent_SymbiosisCovenantState? state,
                    out message)
                || state == null)
            {
                return false;
            }

            if (state.GetRecord(faction) == null)
            {
                message = "该派系没有共生盟约记录。";
                return false;
            }

            int before = state.GetRecord(faction)!.Trust;
            bool changed = state.DevAdjustTrust(
                faction,
                amount,
                DevTrustReason);

            int after = state.GetRecord(faction)!.Trust;

            message =
                changed
                    ? "已调整 "
                      + faction.Name
                      + " 的信任度："
                      + before
                      + " → "
                      + after
                      + "。"
                    : "未能调整 "
                      + faction.Name
                      + " 的信任度（增量无效或来源限额已耗尽）。";

            return changed;
        }

        public static bool TryForceJoin(
            Faction faction,
            out string message)
        {
            if (faction == null)
            {
                message = "指定派系为空。";
                return false;
            }

            if (!TryRequireDevMode(out message))
            {
                return false;
            }

            if (!TryGetActiveState(
                    out GameComponent_SymbiosisCovenantState? state,
                    out message)
                || state == null)
            {
                return false;
            }

            if (state.GetRecord(faction) == null)
            {
                message = "该派系没有共生盟约记录。";
                return false;
            }

            if (state.DevForceJoinCovenant(faction))
            {
                message = faction.Name + " 已被强制加入盟约。";
                return true;
            }

            message = faction.Name + " 已经是盟约成员，无法再次加入。";
            return false;
        }

        public static bool TryForceLeave(
            Faction faction,
            out string message)
        {
            if (faction == null)
            {
                message = "指定派系为空。";
                return false;
            }

            if (!TryRequireDevMode(out message))
            {
                return false;
            }

            if (!TryGetActiveState(
                    out GameComponent_SymbiosisCovenantState? state,
                    out message)
                || state == null)
            {
                return false;
            }

            if (state.GetRecord(faction) == null)
            {
                message = "该派系没有共生盟约记录。";
                return false;
            }

            if (state.DevForceLeaveCovenant(faction))
            {
                message = faction.Name + " 已被强制退出盟约。";
                return true;
            }

            message = faction.Name + " 当前不是盟约成员，无法强制退出。";
            return false;
        }

        public static bool TryBeginProposal(
            Faction faction,
            out string message)
        {
            if (faction == null)
            {
                message = "指定派系为空。";
                return false;
            }

            if (!TryRequireDevMode(out message))
            {
                return false;
            }

            if (!TryGetActiveState(
                    out GameComponent_SymbiosisCovenantState? state,
                    out message)
                || state == null)
            {
                return false;
            }

            if (state.GetRecord(faction) == null)
            {
                message = "该派系没有共生盟约记录。";
                return false;
            }

            if (state.DevBeginProposal(faction))
            {
                message = "已开始向 " + faction.Name + " 的邀请提案。";
                return true;
            }

            message =
                "无法开始提案（可能未广播宣言、信任不足、已是成员、"
                + "提案进行中、永久拒绝或仍在邀请冷却中）。";
            return false;
        }

        public static bool TryResolveProposalRandom(
            Faction faction,
            out string message)
        {
            return TryResolveProposal(faction, null, "随机结算", out message);
        }

        public static bool TryForceProposalSuccess(
            Faction faction,
            out string message)
        {
            return TryResolveProposal(faction, true, "强制成功", out message);
        }

        public static bool TryForceProposalFailure(
            Faction faction,
            out string message)
        {
            return TryResolveProposal(faction, false, "强制失败", out message);
        }

        private static bool TryResolveProposal(
            Faction faction,
            bool? forceOutcome,
            string kindLabel,
            out string message)
        {
            if (faction == null)
            {
                message = "指定派系为空。";
                return false;
            }

            if (!TryRequireDevMode(out message))
            {
                return false;
            }

            if (!TryGetActiveState(
                    out GameComponent_SymbiosisCovenantState? state,
                    out message)
                || state == null)
            {
                return false;
            }

            if (state.GetRecord(faction) == null)
            {
                message = "该派系没有共生盟约记录。";
                return false;
            }

            if (state.DevResolveProposal(faction, forceOutcome))
            {
                message = "已" + kindLabel + " " + faction.Name + " 的提案。";
                return true;
            }

            message = "无法结算提案（记录不存在或提案尚未开始）。";
            return false;
        }

        public static bool TryClearInvitationCooldown(
            Faction faction,
            out string message)
        {
            if (faction == null)
            {
                message = "指定派系为空。";
                return false;
            }

            if (!TryRequireDevMode(out message))
            {
                return false;
            }

            if (!TryGetActiveState(
                    out GameComponent_SymbiosisCovenantState? state,
                    out message)
                || state == null)
            {
                return false;
            }

            if (state.GetRecord(faction) == null)
            {
                message = "该派系没有共生盟约记录。";
                return false;
            }

            state.DevClearInvitationCooldown(faction);
            message = "已清除 " + faction.Name + " 的邀请冷却。";
            return true;
        }

        public static bool TryResetTrustSourceLimits(
            Faction faction,
            out string message)
        {
            if (faction == null)
            {
                message = "指定派系为空。";
                return false;
            }

            if (!TryRequireDevMode(out message))
            {
                return false;
            }

            if (!TryGetActiveState(
                    out GameComponent_SymbiosisCovenantState? state,
                    out message)
                || state == null)
            {
                return false;
            }

            if (state.GetRecord(faction) == null)
            {
                message = "该派系没有共生盟约记录。";
                return false;
            }

            state.DevResetSourceLimits(faction);
            message = "已重置 " + faction.Name + " 的信任来源限额。";
            return true;
        }

        public static bool TryRecreateRecord(
            Faction faction,
            out string message)
        {
            if (faction == null)
            {
                message = "指定派系为空。";
                return false;
            }

            if (!TryRequireDevMode(out message))
            {
                return false;
            }

            if (!TryGetActiveState(
                    out GameComponent_SymbiosisCovenantState? state,
                    out message)
                || state == null)
            {
                return false;
            }

            if (state.DevRecreateRecord(faction))
            {
                message = "已重建 " + faction.Name + " 的共生盟约记录。";
                return true;
            }

            message = "无法重建该派系记录（派系不符合盟约资格）。";
            return false;
        }

        public static bool TrySetInvitationFailureCount(
            Faction faction,
            int value,
            out string message)
        {
            if (faction == null)
            {
                message = "指定派系为空。";
                return false;
            }

            if (!TryRequireDevMode(out message))
            {
                return false;
            }

            if (!TryGetActiveState(
                    out GameComponent_SymbiosisCovenantState? state,
                    out message)
                || state == null)
            {
                return false;
            }

            if (state.GetRecord(faction) == null)
            {
                message = "该派系没有共生盟约记录。";
                return false;
            }

            state.DevSetInvitationFailureCount(faction, value);
            message =
                "已将 "
                + faction.Name
                + " 的邀请失败次数设置为 "
                + Math.Max(0, value)
                + "。";
            return true;
        }

        public static bool TrySetCovenantExitCount(
            Faction faction,
            int value,
            out string message)
        {
            if (faction == null)
            {
                message = "指定派系为空。";
                return false;
            }

            if (!TryRequireDevMode(out message))
            {
                return false;
            }

            if (!TryGetActiveState(
                    out GameComponent_SymbiosisCovenantState? state,
                    out message)
                || state == null)
            {
                return false;
            }

            if (state.GetRecord(faction) == null)
            {
                message = "该派系没有共生盟约记录。";
                return false;
            }

            state.DevSetCovenantExitCount(faction, value);
            message =
                "已将 "
                + faction.Name
                + " 的退出盟约次数设置为 "
                + Math.Max(0, value)
                + "。";
            return true;
        }

        // ===== 联合贸易代表团 =====

        public static bool TrySpawnTradeDelegationNow(out string message)
        {
            if (!TryRequireDevMode(out message))
            {
                return false;
            }

            if (!TryGetActiveState(out _, out message))
            {
                return false;
            }

            if (Find.CurrentMap == null)
            {
                message = "当前没有可用于生成联合贸易代表团的地图。";
                return false;
            }

            if (SymbiosisCovenantTradeDelegationScheduler.DevSpawnNow())
            {
                message = "已立即生成联合贸易代表团。";
                return true;
            }

            message =
                "当前盟约等级、成员状态或地图条件不足，无法执行联合贸易代表团操作。";
            return false;
        }

        public static bool TryRescheduleTradeDelegation(out string message)
        {
            if (!TryRequireDevMode(out message))
            {
                return false;
            }

            if (!TryGetActiveState(out _, out message))
            {
                return false;
            }

            if (SymbiosisCovenantTradeDelegationScheduler.DevReschedule())
            {
                message = "已重新安排联合贸易代表团的调度时钟。";
                return true;
            }

            message =
                "当前盟约等级、成员状态或地图条件不足，无法执行联合贸易代表团操作。";
            return false;
        }

        public static bool TryMakeTradeDelegationDueNow(out string message)
        {
            if (!TryRequireDevMode(out message))
            {
                return false;
            }

            if (!TryGetActiveState(out _, out message))
            {
                return false;
            }

            if (SymbiosisCovenantTradeDelegationScheduler.DevMakeDueNow())
            {
                message = "已使联合贸易代表团立即到期（下一次 Tick 即尝试生成）。";
                return true;
            }

            message =
                "当前盟约等级、成员状态或地图条件不足，无法执行联合贸易代表团操作。";
            return false;
        }

        // ===== 共同防卫 =====

        public static bool TryForceCurrentThreatAidOffer(out string message)
        {
            if (!TryRequireDevMode(out message))
            {
                return false;
            }

            if (!TryGetActiveState(out _, out message))
            {
                return false;
            }

            if (SymbiosisCovenantMilitaryAidUtility.DevForceOfferForCurrentThreat())
            {
                message =
                    "已根据当前威胁 CombatPower 估值强制发送共同防卫援助询问；"
                    + "随机概率已跳过。";
                return true;
            }

            message =
                "未能发送当前威胁援助询问（无当前威胁、响应Faction、"
                + "信件/冷却/活动援军等条件不满足，或盟约等级不足 L3）。";
            return false;
        }

        public static bool TryForcePendingRaidAidOffer(out string message)
        {
            if (!TryRequireDevMode(out message))
            {
                return false;
            }

            if (!TryGetActiveState(
                    out GameComponent_SymbiosisCovenantState? state,
                    out message)
                || state == null)
            {
                return false;
            }

            Map? map = Find.CurrentMap;
            if (map == null)
            {
                message = "当前没有地图。";
                return false;
            }

            // 先读取 Snapshot，给出更精确的失败原因。
            SymbiosisCovenantMilitaryAidUtility
                .SymbiosisCovenantMilitaryAidDevSnapshot snap =
                    SymbiosisCovenantMilitaryAidUtility.GetDevSnapshot(
                        state,
                        map);

            if (!snap.PendingEvaluation)
            {
                message = "当前地图没有待判定的共生盟约 Pending Raid。";
                return false;
            }

            if (snap.PendingRaidTicksRemaining > 0)
            {
                message =
                    "当前 Pending Raid 尚未到正式判定时间；剩余 "
                    + snap.PendingRaidTicksRemaining
                    + " ticks。"
                    + "该 DEV 操作只跳过援助概率，不跳过 600 tick 判定延迟；"
                    + "Pending 已保留，请等待延迟结束后再试。";
                return false;
            }

            if (SymbiosisCovenantMilitaryAidUtility
                    .DevForcePendingOfferForCurrentMap())
            {
                message =
                    "已使用真实 Pending Raid 数据强制发送共同防卫援助询问；"
                    + "随机概率已跳过（仍保留 600 tick 判定延迟与其他资格检查）。";
                return true;
            }

            message =
                "Pending Raid 已到判定时间，但 ActiveThreat、合法响应派系、"
                + "冷却、已有援助信或活动援军等条件不满足；"
                + "Pending 已保留，可在条件变化后重试。";
            return false;
        }

        public static bool TryClearMilitaryAidState(out string message)
        {
            if (!TryRequireDevMode(out message))
            {
                return false;
            }

            if (!TryGetActiveState(out _, out message))
            {
                return false;
            }

            if (SymbiosisCovenantMilitaryAidUtility.DevClearCurrentMapState())
            {
                message =
                    "已清除当前地图的共同防卫 Pending、援助信、冷却"
                    + "以及活动援军追踪状态；"
                    + "已经生成在地图上的援军不会被移除。";
                return true;
            }

            message = "无法清除当前地图共同防卫状态。";
            return false;
        }

        public static bool TryClearMilitaryAidCooldown(out string message)
        {
            if (!TryRequireDevMode(out message))
            {
                return false;
            }

            if (!TryGetActiveState(out _, out message))
            {
                return false;
            }

            if (SymbiosisCovenantMilitaryAidUtility.DevClearCurrentMapCooldown())
            {
                message = "已仅清除当前地图的共同防卫冷却；"
                    + "活动援军、信件与 Pending Raid 不受影响。";
                return true;
            }

            message = "无法清除当前地图共同防卫冷却。";
            return false;
        }

        // ===== 联合军事行动（L2 解锁的独立机制） =====

        public static bool TrySpawnJointOperationNow(out string message)
        {
            if (!TryRequireDevMode(out message))
            {
                return false;
            }

            if (!TryGetActiveState(out _, out message))
            {
                return false;
            }

            if (SymbiosisCovenantJointOperationScheduler.DevSpawnNow())
            {
                message =
                    "已立即尝试生成联合军事行动邀请"
                    + "（需盟约等级达到 L" + SymbiosisCovenantJointOperationDef.MinimumCovenantLevel
                    + " / 无进行中行动 / 暴力任务许可 / 存在可用目标）。";
                return true;
            }

            message =
                "未能生成联合军事行动邀请（盟约等级不足 L"
                + SymbiosisCovenantJointOperationDef.MinimumCovenantLevel
                + "、已有进行中行动、无可用真实敌方目标或暴力任务被禁用）。";
            return false;
        }

        public static bool TryMakeJointOperationDueNow(out string message)
        {
            if (!TryRequireDevMode(out message))
            {
                return false;
            }

            if (!TryGetActiveState(out _, out message))
            {
                return false;
            }

            if (SymbiosisCovenantJointOperationScheduler.DevMakeDueNow())
            {
                message = "已使联合军事行动调度立即到期（下一次 Tick 即尝试生成）。";
                return true;
            }

            message = "无法使联合军事行动调度到期（盟约等级不足 L"
                + SymbiosisCovenantJointOperationDef.MinimumCovenantLevel + "）。";
            return false;
        }

        public static bool TryClearJointOperationCooldown(out string message)
        {
            if (!TryRequireDevMode(out message))
            {
                return false;
            }

            if (!TryGetActiveState(out _, out message))
            {
                return false;
            }

            if (SymbiosisCovenantJointOperationScheduler.DevClearCooldown())
            {
                message = "已清除联合军事行动冷却。";
                return true;
            }

            message = "无法清除联合军事行动冷却。";
            return false;
        }

        public static bool TryClearJointOperation(out string message)
        {
            if (!TryRequireDevMode(out message))
            {
                return false;
            }

            if (!TryGetActiveState(out _, out message))
            {
                return false;
            }

            SymbiosisCovenantJointOperationUtility.FindActiveOperationPart()
                ?.DevEndOperation();

            message = "已清除当前联合军事行动状态（按无效结束，不加不减）。";
            return true;
        }

        public static bool TryLogJointOperationStatus(out string message)
        {
            Log.Message(BuildJointOperationStatus());
            message = "联合军事行动状态已写入 RimWorld 日志。";
            return true;
        }

        public static string BuildJointOperationStatus()
        {
            GameComponent_SymbiosisCovenantState? state =
                GameComponent_SymbiosisCovenantState.CurrentComponent;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[MAP-SymbiosisDebug] ===== JointOperation =====");

            if (state == null)
            {
                sb.AppendLine("Available = False");
                sb.AppendLine();
                return sb.ToString();
            }

            SymbiosisCovenantJointOperationScheduler
                .SymbiosisCovenantJointOperationDevSnapshot? snap =
                SymbiosisCovenantJointOperationScheduler.GetDevSnapshot(state);

            if (snap == null)
            {
                sb.AppendLine("Available = False (level < L"
                    + SymbiosisCovenantJointOperationDef.MinimumCovenantLevel + ")");
                sb.AppendLine();
                return sb.ToString();
            }

            sb.AppendLine("Level = " + snap.CovenantLevel);
            sb.AppendLine(
                "CurrentSupportPointsFactor = "
                + snap.CurrentSupportPointsFactor.ToString("F2"));
            sb.AppendLine("CovenantLevelSnapshot = " + snap.CovenantLevelSnapshot);
            sb.AppendLine(
                "SupportPointsFactorSnapshot = "
                + snap.SupportPointsFactorSnapshot.ToString("F2"));
            sb.AppendLine("NextTick = " + snap.NextTick);
            sb.AppendLine("DaysUntilNext = " + snap.DaysUntilNext.ToString("F1"));
            sb.AppendLine("CooldownRemainingTicks = " + snap.CooldownRemainingTicks);
            sb.AppendLine("Ongoing = " + snap.Ongoing);
            sb.AppendLine("Target = " + (snap.TargetLabel ?? "-"));
            sb.AppendLine("TargetQuestTag = " + (snap.TargetQuestTag ?? "-"));
            sb.AppendLine("Stage = " + snap.Stage);
            sb.AppendLine("Participants = " + snap.ParticipantsCount);
            sb.AppendLine("TargetThreatPointsAtDeployment = " + snap.TargetThreatPointsAtDeployment);
            sb.AppendLine("TotalSupportPointsAtDeployment = " + snap.TotalSupportPointsAtDeployment.ToString("F1"));
            sb.AppendLine("TrackedLordCount = " + snap.TrackedLordCount);
            if (snap.PerFactionSupport != null)
            {
                for (int i = 0; i < snap.PerFactionSupport.Count; i++)
                {
                    sb.AppendLine("  Support[" + i + "] = " + snap.PerFactionSupport[i]);
                }
            }
            sb.AppendLine();

            return sb.ToString();
        }

        // ===== 标准测试 Raid =====

        public static bool TrySpawnTestRaid(
            float points,
            Faction? faction,
            out string message)
        {
            if (!TryRequireDevMode(out message))
            {
                return false;
            }

            Map? map = Find.CurrentMap;

            if (map == null)
            {
                message = "当前没有地图。";
                return false;
            }

            if (points <= 0f)
            {
                message = "测试袭击点数必须大于0。";
                return false;
            }

            if (faction != null)
            {
                Faction? player = Faction.OfPlayerSilentFail;

                if (player == null
                    || faction.IsPlayer
                    || faction.defeated
                    || faction.deactivated
                    || faction.Hidden
                    || faction.temporary
                    || !faction.HostileTo(player))
                {
                    message =
                        "指定派系当前不能作为标准敌对袭击派系。";
                    return false;
                }
            }

            // 必须真正调用原版 RaidEnemy，以验证：
            // RaidEnemy → Harmony Postfix → NotifyRaidSucceeded → pendingRaids 的完整监听链。
            // 禁止直接 NotifyRaidSucceeded 或手工塞 pendingRaids；
            // 也不自行选择 RaidStrategy / ArrivalMode，让原版 RaidEnemy 正常 Resolve。
            IncidentParms parms = new IncidentParms
            {
                target = map,
                points = points,
                forced = true,
                faction = faction
            };

            bool succeeded =
                IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);

            if (!succeeded)
            {
                message =
                    "RaidEnemy 执行失败；"
                    + "指定Faction、PawnGroup或地图条件可能不合法。";
                return false;
            }

            message =
                "标准 RaidEnemy 已执行；"
                + "请求点数="
                + points.ToString("F0")
                + (faction != null
                    ? "；指定派系=" + faction.Name
                    : "；派系由原版 RaidEnemy 自动选择")
                + "。请检查共同防卫 Pending 状态。";

            return true;
        }

        public static List<Faction> GetHostileRaidTestFactions()
        {
            Faction? player = Faction.OfPlayerSilentFail;

            if (player == null || Find.FactionManager == null)
            {
                return new List<Faction>();
            }

            return Find.FactionManager
                .AllFactions
                .Where(faction =>
                    faction != null
                    && !faction.IsPlayer
                    && !faction.defeated
                    && !faction.deactivated
                    && !faction.Hidden
                    && !faction.temporary
                    && faction.HostileTo(player))
                .OrderBy(faction => faction.Name)
                .ThenBy(faction => faction.loadID)
                .ToList()!;
        }

        // ===== 状态文本输出（仅读取，便于未来 Harness 调用） =====

        public static string BuildOverallStatus()
        {
            GameComponent_SymbiosisCovenantState? state =
                GameComponent_SymbiosisCovenantState.CurrentComponent;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[MAP-SymbiosisDebug] ===== Covenant =====");

            if (state == null)
            {
                sb.AppendLine("Active = False");
                return sb.ToString();
            }

            sb.AppendLine(
                "Active = " + GameComponent_SymbiosisCovenantState.IsActive);
            sb.AppendLine(
                "PublicDeclaration = " + state.PublicDeclarationBroadcast);
            sb.AppendLine("Unity = " + state.Unity.ToString("F0"));
            sb.AppendLine("CovenantLevel = " + state.CovenantLevel);
            sb.AppendLine(
                "HighestCovenantLevel = " + state.HighestCovenantLevel);
            sb.AppendLine("MemberCount = " + state.CovenantMemberCount);
            sb.AppendLine();

            sb.Append(BuildTradeDelegationStatus());
            sb.Append(BuildMilitaryAidStatus());
            sb.Append(BuildJointOperationStatus());

            return sb.ToString();
        }

        public static string BuildTradeDelegationStatus()
        {
            GameComponent_SymbiosisCovenantState? state =
                GameComponent_SymbiosisCovenantState.CurrentComponent;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[MAP-SymbiosisDebug] ===== TradeDelegation =====");

            if (state == null)
            {
                sb.AppendLine("Available = False");
                sb.AppendLine();
                return sb.ToString();
            }

            SymbiosisCovenantTradeDelegationDevSnapshot? snap =
                SymbiosisCovenantTradeDelegationScheduler.GetDevSnapshot(state);

            if (snap == null)
            {
                sb.AppendLine("Available = False");
                sb.AppendLine();
                return sb.ToString();
            }

            sb.AppendLine("Available = True");
            sb.AppendLine("Level = " + snap.CurrentLevel);
            sb.AppendLine("MemberCount = " + snap.MemberCount);
            sb.AppendLine(
                "BaseIntervalDays = "
                + FormatFloatRange(snap.BaseIntervalDays));
            sb.AppendLine(
                "MemberSpeedMultiplier = "
                + snap.MemberSpeedMultiplier.ToString("F2"));
            sb.AppendLine("NextTick = " + snap.NextTick);
            sb.AppendLine(
                "DaysUntilNext = " + snap.DaysUntilNext.ToString("F1"));
            sb.AppendLine("RetryCount = " + snap.RetryCount);
            sb.AppendLine(
                "LastLeadFaction = " + (snap.LastLeadFaction?.Name ?? "-"));
            sb.AppendLine();

            return sb.ToString();
        }

        public static string BuildMilitaryAidStatus()
        {
            GameComponent_SymbiosisCovenantState? state =
                GameComponent_SymbiosisCovenantState.CurrentComponent;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[MAP-SymbiosisDebug] ===== MilitaryAid =====");

            if (state == null || Find.CurrentMap == null)
            {
                sb.AppendLine("Available = False");
                sb.AppendLine();
                return sb.ToString();
            }

            SymbiosisCovenantMilitaryAidUtility.SymbiosisCovenantMilitaryAidDevSnapshot
                snap = SymbiosisCovenantMilitaryAidUtility.GetDevSnapshot(
                    state,
                    Find.CurrentMap);

            sb.AppendLine("Level = " + snap.CovenantLevel);
            sb.AppendLine("BaseOfferChance = " + Percent(snap.BaseOfferChance));
            sb.AppendLine("MaxOfferChance = " + Percent(snap.MaxOfferChance));
            sb.AppendLine(
                "SupportPointsFactor = " + Percent(snap.SupportPointsFactor));
            sb.AppendLine(
                "CurrentThreatFaction = " + (snap.CurrentThreatFaction?.Name ?? "-"));
            sb.AppendLine(
                "CurrentThreatCombatPower = "
                + snap.CurrentThreatCombatPower.ToString("F0"));
            sb.AppendLine(
                "EligibleResponderCount = " + snap.EligibleResponderCount);
            sb.AppendLine(
                "ResponderChanceBonus = " + Percent(snap.ResponderChanceBonus));
            sb.AppendLine(
                "EffectiveOfferChance = " + Percent(snap.EffectiveOfferChance));
            sb.AppendLine("PendingEvaluation = " + snap.PendingEvaluation);
            sb.AppendLine(
                "PendingRaidAttackerFaction = "
                + (snap.PendingRaidAttackerFaction?.Name ?? "-"));
            sb.AppendLine(
                "PendingRaidPoints = " + snap.PendingRaidPoints.ToString("F0"));
            sb.AppendLine(
                "PendingRaidEvaluateAtTick = " + snap.PendingRaidEvaluateAtTick);
            sb.AppendLine(
                "PendingRaidTicksRemaining = " + snap.PendingRaidTicksRemaining);
            sb.AppendLine("PendingOffer = " + snap.PendingOffer);
            sb.AppendLine(
                "LastResponderFaction = "
                + (snap.LastResponderFaction?.Name ?? "-"));
            sb.AppendLine(
                "ActiveAidFaction = " + (snap.ActiveAidFaction?.Name ?? "-"));
            sb.AppendLine("ActiveAidTag = " + (snap.ActiveAidTag ?? "-"));
            sb.AppendLine(
                "ActiveAidTriggerRaidPoints = "
                + snap.ActiveAidTriggerRaidPoints.ToString("F0"));
            sb.AppendLine(
                "ActiveAidSupportPoints = "
                + snap.ActiveAidSupportPoints.ToString("F0"));
            sb.AppendLine("ActiveAidStartTick = " + snap.ActiveAidStartTick);
            sb.AppendLine(
                "TaggedAssistLordCount = " + snap.TaggedAssistLordCount);
            sb.AppendLine(
                "CooldownRemainingTicks = " + snap.CooldownRemainingTicks);
            sb.AppendLine();

            return sb.ToString();
        }

        public static string BuildFactionRecordsStatus()
        {
            GameComponent_SymbiosisCovenantState? state =
                GameComponent_SymbiosisCovenantState.CurrentComponent;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[MAP-SymbiosisDebug] ===== FactionRecords =====");

            if (state == null)
            {
                sb.AppendLine("Available = False");
                sb.AppendLine();
                return sb.ToString();
            }

            IReadOnlyList<SymbiosisCovenantFactionRecord> records =
                state.GetRecordsSorted();

            if (records.Count == 0)
            {
                sb.AppendLine("Records = 0");
                sb.AppendLine();
                return sb.ToString();
            }

            Faction? player = Faction.OfPlayerSilentFail;

            foreach (SymbiosisCovenantFactionRecord record in records)
            {
                Faction? faction = record.Faction;
                if (faction == null)
                {
                    continue;
                }

                sb.AppendLine("Faction = " + faction.Name);
                sb.AppendLine("defName = " + faction.def.defName);
                sb.AppendLine("loadID = " + faction.loadID);
                sb.AppendLine("Trust = " + record.Trust);
                sb.AppendLine("CovenantMember = " + record.CovenantMember);
                sb.AppendLine("ProposalPending = " + record.ProposalPending);
                sb.AppendLine(
                    "InvitationFailureCount = " + record.InvitationFailureCount);
                sb.AppendLine(
                    "NextInvitationTick = " + record.NextInvitationTick);
                sb.AppendLine(
                    "CovenantExitCount = " + record.CovenantExitCount);
                if (player != null)
                {
                    sb.AppendLine("Goodwill = " + player.GoodwillWith(faction));
                }
            }

            sb.AppendLine();

            return sb.ToString();
        }

        // ===== 日志 =====

        public static bool TryLogOverallStatus(out string message)
        {
            Log.Message(BuildOverallStatus());
            message = "完整共生盟约状态已写入 RimWorld 日志。";
            return true;
        }

        public static bool TryLogTradeDelegationStatus(out string message)
        {
            Log.Message(BuildTradeDelegationStatus());
            message = "联合贸易代表团状态已写入 RimWorld 日志。";
            return true;
        }

        public static bool TryLogMilitaryAidStatus(out string message)
        {
            Log.Message(BuildMilitaryAidStatus());
            message = "共同防卫状态已写入 RimWorld 日志。";
            return true;
        }

        public static bool TryLogFactionRecords(out string message)
        {
            Log.Message(BuildFactionRecordsStatus());
            message = "全部派系记录已写入 RimWorld 日志。";
            return true;
        }

        // ===== 内部格式化辅助 =====

        private static string Percent(float value)
        {
            return (value * 100f).ToString("F0") + "%";
        }

        private static string FormatFloatRange(FloatRange range)
        {
            return Mathf.RoundToInt(range.min)
                + "~"
                + Mathf.RoundToInt(range.max);
        }
    }
}
