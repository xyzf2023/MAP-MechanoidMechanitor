using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 肃清评级系统「统一只读查询 + 正式奖励/处罚入口」。
    /// 所有 UI、任务资格、机械族、物资、折扣与协议权限都必须走这里，
    /// 保证显示与运行时结算完全一致。
    /// </summary>
    public static class PurgeDirectiveRatingUtility
    {
        public static PurgeDirectiveRatingConfigDef Config => PurgeDirectiveRatingConfigDefOf.MAP_PurgeDirectiveRatingConfig;

        public static bool IsRatingSystemActive()
        {
            if (GameComponent_CerebrexTakeoverState.IsActive) return false;
            if (!GameComponent_MechanoidMechanitorStoryState.IsPurgeDirectiveActive) return false;
            return true;
        }

        public static bool IsFinalPenaltyTriggered()
        {
            MechanoidMechanitorPurgeDirectiveRuntimeState? rs = Runtime;
            if (rs == null) return false;
            return rs.FinalPenaltyTriggered;
        }

        public static MechanoidMechanitorPurgeDirectiveRuntimeState? Runtime =>
            Current.Game?.GetComponent<GameComponent_MechanoidMechanitorStoryState>()?.PurgeDirectiveRuntimeState;

        public static int CurrentRatingValue()
        {
            MechanoidMechanitorPurgeDirectiveRuntimeState? rs = Runtime;
            if (rs == null) return 0;
            return rs.RatingValue;
        }

        /// <summary>当前等级（1..5）。0 点也返回一级；接管主脑一律视为五级有效权限。</summary>
        public static int CurrentRatingLevel
        {
            get
            {
                if (GameComponent_CerebrexTakeoverState.IsActive) return 5;
                return Config.GetRatingLevel(CurrentRatingValue());
            }
        }

        /// <summary>下一等级起点（满级返回最大评级值）。</summary>
        public static int NextThresholdValue()
        {
            return Config.GetNextLevelStart(CurrentRatingLevel);
        }

        public static bool IsMaxRatingLevel(int level) => level >= 5;

        public static bool IsMaxRatingLevel() => IsMaxRatingLevel(CurrentRatingLevel);

        /// <summary>
        /// 当前折扣率（0..1），折扣只作用于机械族调拨与物资请求。
        /// 接管主脑后按 takeoverDiscount 运行。
        /// </summary>
        public static float GetDiscountRate()
        {
            if (GameComponent_CerebrexTakeoverState.IsActive) return Config.takeoverDiscount;
            return Config.GetDiscountRateForLevel(CurrentRatingLevel);
        }

        /// <summary>下一等级相对当前等级的新增权限摘要（用于升级信/顶部栏）。</summary>
        public static string NextLevelNewPermissions(int currentRating)
        {
            int level = Config.GetRatingLevel(currentRating);
            if (level >= 5) return string.Empty;
            return PurgeDirectiveRatingDisplay.PermissionSummaryForLevel(level + 1);
        }

        // ===== 机械族重量级权限 =====

        public static int RequiredLevelForMechWeight(MechWeightClassDef? weight)
        {
            return Config.RequiredLevelForMechWeight(weight);
        }

        public static bool IsMechUnlocked(MechWeightClassDef? weight)
        {
            // 配置约定 null 重量级按 Light 处理，第三方机械族不因缺省重量级被永久锁死。
            return CurrentRatingLevel >= RequiredLevelForMechWeight(weight);
        }

        // ===== 物资目录权限 =====

        /// <summary>
        /// 物资所需最低评级等级（1..3）。
        /// 黑名单优先级最高；其次 XML 覆盖（替代自动分类）；否则按目录条目三级安全规则自动分类。
        /// ThingDef 为 null、目录查询失败、价格异常时返回 3（从严）。
        /// </summary>
        public static int RequiredLevelForThing(ThingDef? def)
        {
            if (def == null) return 3;

            if (MechanoidOvermindCatalogService.GetMergedBlacklist().Contains(def))
            {
                return 3;
            }

            int over = PurgeDirectiveGoodsRatingOverrideUtility.GetOverrideLevel(def);
            if (over > 0) return over; // 覆盖替代自动分类，不是 max

            if (MechanoidOvermindCatalogService.TryFindThingCatalogEntry(def, out MechanoidOvermindThingCatalogEntry entry))
            {
                return Config.ClassifyGoodsLevel(entry.Category, entry.DefaultReferenceMarketValue);
            }

            return 3; // 无法安全分类时从严归三级
        }

        public static bool IsThingUnlocked(ThingDef? def)
        {
            if (def == null) return false;

            // 黑名单是最终否决，不得因为三级已满足 RequiredLevelForThing 的从严回退值而绕过。
            if (MechanoidOvermindCatalogService.GetMergedBlacklist().Contains(def)) return false;
            return CurrentRatingLevel >= RequiredLevelForThing(def);
        }

        /// <summary>物品未解锁时的锁定原因翻译串（黑名单 / XML覆盖 / 自动分类 / 可搬运建筑）。</summary>
        public static TaggedString GetThingLockReason(ThingDef def)
        {
            if (def == null) return "MAP_PurgeDirectiveRating.Reason.Invalid".Translate();
            if (MechanoidOvermindCatalogService.GetMergedBlacklist().Contains(def))
            {
                return "MAP_PurgeDirectiveRating.Reason.Blacklist".Translate();
            }

            if (MechanoidOvermindCatalogService.TryFindThingCatalogEntry(def, out MechanoidOvermindThingCatalogEntry entry)
                && entry.Category == MechanoidOvermindThingCategory.Building)
            {
                return "MAP_PurgeDirectiveRating.Reason.Building".Translate();
            }

            int overrideLevel = PurgeDirectiveGoodsRatingOverrideUtility.GetOverrideLevel(def);
            if (overrideLevel > 0)
            {
                return "MAP_PurgeDirectiveRating.Reason.XmlOverride".Translate(overrideLevel);
            }

            return "MAP_PurgeDirectiveRating.Reason.AutoClassify".Translate(RequiredLevelForThing(def));
        }

        // ===== 特殊协议权限 =====

        /// <summary>部队支援是否开放：二级及以上开放（一级锁定）。接管主脑开放。</summary>
        public static bool IsForceSupportAvailable()
        {
            if (GameComponent_CerebrexTakeoverState.IsActive) return true;
            int level = CurrentRatingLevel;
            return level >= 1 && Config.GetForceSupportMaxThreat(level) > 0;
        }

        /// <summary>机械集群是否开放：四级及以上开放（一~三级锁定）。接管主脑开放。</summary>
        public static bool IsClusterAvailable()
        {
            if (GameComponent_CerebrexTakeoverState.IsActive) return true;
            int level = CurrentRatingLevel;
            return level >= 1 && Config.GetClusterMaxThreat(level) > 0;
        }

        public static bool ClusterEnvironmentAllowed(int rating)
        {
            return rating >= Config.mechClusterEnvironmentMinLevel;
        }

        public static bool ClusterEnvironmentAllowed()
        {
            return ClusterEnvironmentAllowed(CurrentRatingLevel);
        }

        public static bool IsClusterEnvironmentAllowed()
        {
            if (GameComponent_CerebrexTakeoverState.IsActive) return true;
            return ClusterEnvironmentAllowed(CurrentRatingLevel);
        }

        /// <summary>部队支援有效威胁上限；接管主脑沿用原版无硬上限。</summary>
        public static int EffectiveForceSupportMaxThreat()
        {
            if (GameComponent_CerebrexTakeoverState.IsActive) return int.MaxValue;
            return Config.GetForceSupportMaxThreat(CurrentRatingLevel);
        }

        /// <summary>机械集群有效威胁上限；接管主脑按原版上限。</summary>
        public static int EffectiveClusterMaxThreat()
        {
            if (GameComponent_CerebrexTakeoverState.IsActive) return MechClusterDeploymentOrder.MaxThreatPoints;
            return Config.GetClusterMaxThreat(CurrentRatingLevel);
        }

        // ===== 正式奖励/处罚入口 =====

        /// <summary>
        /// 向评级值安全增加（可为负）。中间用 long 计算后钳制到 0~4000，避免整数溢出。
        /// 仅修改评级值，不影响肃清额度。
        /// </summary>
        public static bool TryAddRating(int amount)
        {
            if (!IsRatingSystemActive()) return false;
            MechanoidMechanitorPurgeDirectiveRuntimeState? rs = Runtime;
            if (rs == null) return false;

            int before = rs.RatingValue;
            long next = (long)before + amount;
            if (next < 0L) next = 0L;
            if (next > Config.maxRatingValue) next = Config.maxRatingValue;
            rs.RatingValue = (int)next;
            NotifyRatingChanged(before, rs.RatingValue);
            return true;
        }

        /// <summary>
        /// 直接设置评级值（钳制 0~4000）。不增加肃清额度。
        /// 用于 DEV 调试与最终红色惩罚归零。
        /// </summary>
        public static bool SetRatingDirect(int value)
        {
            if (!IsRatingSystemActive() && !IsFinalPenaltyTriggered()) return false;
            MechanoidMechanitorPurgeDirectiveRuntimeState? rs = Runtime;
            if (rs == null) return false;
            int before = rs.RatingValue;
            long v = value;
            if (v < 0L) v = 0L;
            if (v > Config.maxRatingValue) v = Config.maxRatingValue;
            rs.RatingValue = (int)v;
            NotifyRatingChanged(before, rs.RatingValue);
            return true;
        }

        /// <summary>
        /// 评级跨等级时只发送一封信（升级/降级），不逐级发送；
        /// 单个数值变化跨越多个等级也只发一封；接管主脑后不发送评级信件。
        /// </summary>
        private static void NotifyRatingChanged(int before, int after)
        {
            if (GameComponent_CerebrexTakeoverState.IsActive) return;
            int oldLevel = Config.GetRatingLevel(before);
            int newLevel = Config.GetRatingLevel(after);
            if (oldLevel == newLevel) return;
            if (newLevel > oldLevel)
            {
                PurgeDirectiveRatingLetterUtility.SendUpgradeLetter(oldLevel, newLevel, after);
            }
            else
            {
                PurgeDirectiveRatingLetterUtility.SendDowngradeLetter(newLevel, oldLevel, after);
            }
        }

        /// <summary>
        /// 同额度与评级的正式奖励入口：肃清额度与评级值同时增加等量数值，且先做溢出检查。
        /// 退款、订单取消返款、DEV 调额度不得走此入口（它们不应改评级）。
        /// 返回 false 表示未真正发放（评级系统未激活或额度加法会失败）。
        /// </summary>
        public static bool TryAddPurgeDirectiveRewardPoints(int points)
        {
            if (!IsRatingSystemActive()) return false;
            if (points <= 0) return false;

            // 额度加法溢出保护
            MechanoidMechanitorPurgeDirectiveRuntimeState? rs = Runtime;
            if (rs == null) return false;
            long nextCredits = (long)rs.RewardPoints + points;
            if (nextCredits > int.MaxValue) return false;

            // 评级值与额度同步增加等量数值
            if (!TryAddRating(points)) return false;
            rs.AddRewardPoints((int)nextCredits - rs.RewardPoints);
            return true;
        }

        /// <summary>
        /// 世界目标「基础攻克奖励」统一去重入口（按世界目标稳定ID）。
        /// 先确认奖励能够成功提交（评级系统激活、额度加法不溢出），再标记稳定ID已领取，
        /// 保证基础 200 点跨存档只在首次攻克时各世界目标发放一次。
        /// </summary>
        public static bool TryGrantWorldTargetBaseReward(WorldObject? target, int points)
        {
            if (!IsRatingSystemActive() || target == null || points <= 0) return false;
            MechanoidMechanitorPurgeDirectiveRuntimeState? rs = Runtime;
            if (rs == null) return false;

            string? stableId = PurgeDirectiveQuestTargetUtility.TryGetStableId(target);
            if (stableId == null) return false;
            if (rs.HasAwardedBaseReward(stableId)) return false;

            // 先尝试发放（同时校验系统仍激活、额度不溢出），成功后再标记，保证原子性。
            bool granted = TryAddPurgeDirectiveRewardPoints(points);
            if (!granted) return false;
            rs.MarkBaseRewardAwarded(stableId);
            return true;
        }

        /// <summary>任务额外奖励（按目标类型）。成功时调用，与基础奖励各自只发一次。</summary>
        public static int GetQuestExtraReward(PurgeDirectiveTargetType type)
        {
            switch (type)
            {
                case PurgeDirectiveTargetType.WorkSite: return Config.questWorkSiteExtraRewardPoints;
                case PurgeDirectiveTargetType.Outpost: return Config.questOutpostExtraRewardPoints;
                case PurgeDirectiveTargetType.Settlement: return Config.questSettlementExtraRewardPoints;
                default: return 0;
            }
        }

        /// <summary>任务失败/放弃/超时评级处罚（按目标类型，不扣肃清额度）。</summary>
        public static int GetQuestFailurePenalty(PurgeDirectiveTargetType type)
        {
            switch (type)
            {
                case PurgeDirectiveTargetType.WorkSite: return Config.workSiteFailurePenalty;
                case PurgeDirectiveTargetType.Outpost: return Config.outpostFailurePenalty;
                case PurgeDirectiveTargetType.Settlement: return Config.baseFailurePenalty;
                default: return 0;
            }
        }

        /// <summary>
        /// 通用回退：按世界对象真实类型推导任务失败处罚。
        /// WorkSite：工作站；Outpost：前哨；据点（Settlement 等据点类）按基地处罚。
        /// </summary>
        public static int GetQuestFailurePenalty(WorldObject? target)
        {
            if (target == null) return 0;
            PurgeDirectiveTargetType type = PurgeDirectiveQuestTargetUtility.ClassifyTargetType(target);
            if (type == PurgeDirectiveTargetType.Invalid) return 0;
            return GetQuestFailurePenalty(type);
        }

        /// <summary>对评级值应用对应失败处罚（不扣肃清额度）。</summary>
        public static bool ApplyQuestFailurePenalty(PurgeDirectiveTargetType type)
        {
            int penalty = GetQuestFailurePenalty(type);
            if (penalty <= 0) return false;
            return TryAddRating(-penalty);
        }

        // ===== 血肉殖民者警告阶段处罚（仅扣评级，不扣肃清额度，每个阶段只罚一次） =====

        public static bool ApplyYellowWarningPenalty()
            => TryAddRating(-Config.yellowWarningPenalty);

        public static bool ApplyOrange1WarningPenalty()
            => TryAddRating(-Config.orange1WarningPenalty);

        public static bool ApplyOrange2WarningPenalty()
            => TryAddRating(-Config.orange2WarningPenalty);

        /// <summary>
        /// 是否还有足够肃清额度支付指定费用（折扣后最终费用）。
        /// 订单总览与扣款共用此方法，避免 UI 另算。
        /// </summary>
        public static bool HasEnoughCreditsFor(int finalCost)
        {
            MechanoidMechanitorPurgeDirectiveRuntimeState? rs = Runtime;
            if (rs == null) return false;
            return rs.RewardPoints >= finalCost;
        }

        // ===== 兼容旧调用（保留符号，内部指向新实现） =====

        [System.Obsolete("Use Config.GetRatingLevel")]
        public static int GetRatingLevel(int value) => Config.GetRatingLevel(value);
    }
}
