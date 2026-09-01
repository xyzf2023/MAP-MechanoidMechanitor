using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 肃清指令「节点评级」系统的统一权限入口与状态访问器。
    /// 所有涉及评级等级的判定（机械族重量级、物资分级、特殊协议上限、折扣、任务调度资格）
    /// 都必须经由本工具，禁止在调用方各自维护一份等级表。
    ///
    /// 评级机制在接管主脑（CerebrexTakeover）后完全停用：增/减评级、评级任务与评级信件全部停止，
    /// 但机械族/物资仍按统一折扣（20%）开放，与普通肃清额度逻辑互不影响。
    /// </summary>
    public static class PurgeDirectiveRatingUtility
    {
        public static PurgeDirectiveRatingConfigDef Config =>
            PurgeDirectiveRatingConfigDefOf.MAP_PurgeDirectiveRatingConfig;

        public static GameComponent_MechanoidMechanitorStoryState? StoryState =>
            Current.Game?.GetComponent<GameComponent_MechanoidMechanitorStoryState>();

        public static MechanoidMechanitorPurgeDirectiveRuntimeState? RuntimeState =>
            StoryState?.PurgeDirectiveRuntimeState;

        /// <summary>
        /// 评级机制是否处于活动状态：肃清额度系统启用且未接管主脑。
        /// 接管主脑后返回 false，从而停止评级增减与评级任务。
        /// </summary>
        public static bool IsRatingSystemActive()
        {
            if (GameComponent_CerebrexTakeoverState.IsActive)
            {
                return false;
            }

            return GameComponent_MechanoidMechanitorStoryState.IsPurgeDirectiveActive;
        }

        public static int RatingValue => RuntimeState?.RatingValue ?? 0;

        public static int CurrentRatingLevel => Config.GetRatingLevel(RatingValue);

        /// <summary>
        /// 下一级所需评级值；已满级时返回最大评级值。
        /// </summary>
        public static int NextThresholdValue()
        {
            int value = RatingValue;
            PurgeDirectiveRatingConfigDef c = Config;
            if (value < c.ratingLevel1Threshold) return c.ratingLevel1Threshold;
            if (value < c.ratingLevel2Threshold) return c.ratingLevel2Threshold;
            if (value < c.ratingLevel3Threshold) return c.ratingLevel3Threshold;
            if (value < c.ratingLevel4Threshold) return c.ratingLevel4Threshold;
            return c.maxRatingValue;
        }

        public static bool IsMaxRatingLevel() => CurrentRatingLevel >= 5;

        public static int ClampRating(int value) =>
            Mathf.Clamp(value, 0, Config.maxRatingValue);

        // ===== 评级增减 =====

        public static (int prevLevel, int newLevel, bool changed) TryAddRating(
            int amount,
            string? reason = null)
        {
            int prevLevel = CurrentRatingLevel;
            if (amount <= 0 || !IsRatingSystemActive())
            {
                return (prevLevel, prevLevel, false);
            }

            MechanoidMechanitorPurgeDirectiveRuntimeState? rs = RuntimeState;
            if (rs == null)
            {
                return (prevLevel, prevLevel, false);
            }

            int before = rs.RatingValue;
            int after = ClampRating(before + amount);
            rs.RatingValue = after;
            return NotifyLevelChange(prevLevel, Config.GetRatingLevel(after), reason);
        }

        public static void ApplyPenalty(int amount, string? reason = null)
        {
            int prevLevel = CurrentRatingLevel;
            if (amount <= 0 || !IsRatingSystemActive())
            {
                return;
            }

            MechanoidMechanitorPurgeDirectiveRuntimeState? rs = RuntimeState;
            if (rs == null)
            {
                return;
            }

            int before = rs.RatingValue;
            int after = ClampRating(before - amount);
            rs.RatingValue = after;
            NotifyLevelChange(prevLevel, Config.GetRatingLevel(after), reason);
        }

        /// <summary>
        /// 黄色肃清警告首次发生：评级 -yellowWarningPenalty（不低于0）。
        /// 仅由现有持久化警告状态推进到黄色阶段时调用一次。
        /// </summary>
        public static void ApplyYellowWarningPenalty()
        {
            ApplyPenalty(Config.yellowWarningPenalty);
        }

        /// <summary>第一次橙色警告首次发生：评级 -orange1WarningPenalty。</summary>
        public static void ApplyOrange1WarningPenalty()
        {
            ApplyPenalty(Config.orange1WarningPenalty);
        }

        /// <summary>第二次橙色警告首次发生：评级 -orange2WarningPenalty。</summary>
        public static void ApplyOrange2WarningPenalty()
        {
            ApplyPenalty(Config.orange2WarningPenalty);
        }

        /// <summary>
        /// 肃清评级任务失败/放弃/超时按目标类型取处罚值：
        /// 据点(Settlement) -baseFailurePenalty，工作站(WorkSite) -workSiteFailurePenalty，前哨(其它Site) -outpostFailurePenalty。
        /// </summary>
        public static int GetQuestFailurePenalty(WorldObject? target)
        {
            if (target is Settlement)
            {
                return Config.baseFailurePenalty;
            }

            if (target is Site site)
            {
                SitePartDef? main = site.MainSitePartDef;
                if (main?.tags != null && main.tags.Contains("WorkSite"))
                {
                    return Config.workSiteFailurePenalty;
                }

                return Config.outpostFailurePenalty;
            }

            return Config.outpostFailurePenalty;
        }

        /// <summary>
        /// 直接设定评级值（用于 DEV 调试与红色警告清零）。红色（终末）警告触发时调用并传入 0。
        /// </summary>
        public static void SetRatingDirect(int value, string? reason = null)
        {
            int prevLevel = CurrentRatingLevel;
            if (!IsRatingSystemActive())
            {
                return;
            }

            MechanoidMechanitorPurgeDirectiveRuntimeState? rs = RuntimeState;
            if (rs == null)
            {
                return;
            }

            int after = ClampRating(value);
            rs.RatingValue = after;
            NotifyLevelChange(prevLevel, Config.GetRatingLevel(after), reason);
        }

        private static (int prevLevel, int newLevel, bool changed) NotifyLevelChange(
            int prevLevel,
            int newLevel,
            string? reason)
        {
            if (newLevel > prevLevel)
            {
                PurgeDirectiveRatingLetterUtility.SendUpgradeLetter(prevLevel, newLevel, RatingValue);
            }
            else if (newLevel < prevLevel)
            {
                PurgeDirectiveRatingLetterUtility.SendDowngradeLetter(newLevel, prevLevel, RatingValue);
            }

            return (prevLevel, newLevel, newLevel != prevLevel);
        }

        // ===== 折扣 =====

        /// <summary>
        /// 当前适用的折扣率（0..1）。接管主脑固定 20%，否则按当前等级取值，0 级为 0。
        /// UI 显示、订单余额校验与扣款必须共用本方法，保证三者完全一致。
        /// </summary>
        public static float GetDiscountRate()
        {
            if (GameComponent_CerebrexTakeoverState.IsActive)
            {
                return Config.takeoverDiscount;
            }

            int level = CurrentRatingLevel;
            if (level <= 0)
            {
                return 0f;
            }

            return Config.GetDiscountRateForLevel(level);
        }

        public static int GetDiscountAmount(int baseCredits)
        {
            if (baseCredits <= 0)
            {
                return 0;
            }

            float rate = GetDiscountRate();
            return Mathf.RoundToInt(baseCredits * rate);
        }

        // ===== 机械族重量级权限 =====

        public static int RequiredLevelForMechWeight(MechWeightClassDef? weight) =>
            Config.RequiredLevelForMechWeight(weight);

        public static bool IsMechUnlocked(MechWeightClassDef? weight) =>
            GameComponent_CerebrexTakeoverState.IsActive
            || CurrentRatingLevel >= RequiredLevelForMechWeight(weight);

        // ===== 物资分级权限 =====

        /// <summary>
        /// 某物资（按 Def）所需的最低评级等级 = max(自动分级等级, XML 覆盖等级)。
        /// </summary>
        public static int RequiredLevelForThing(ThingDef? def)
        {
            if (def == null)
            {
                return 1;
            }

            int auto = Config.AutoGradeGoodsLevel(def);
            int over = PurgeDirectiveGoodsRatingOverrideUtility.GetOverrideLevel(def);
            return Mathf.Max(auto, over);
        }

        public static bool IsThingUnlocked(ThingDef? def) =>
            GameComponent_CerebrexTakeoverState.IsActive
            || CurrentRatingLevel >= RequiredLevelForThing(def);

        // ===== 特殊协议分级限制 =====

        public static int ForceSupportMaxThreatPoints() =>
            CurrentRatingLevel <= 0 ? 0 : Config.GetForceSupportMaxThreat(CurrentRatingLevel);

        public static int ClusterMaxThreatPoints() =>
            CurrentRatingLevel <= 0 ? 0 : Config.GetClusterMaxThreat(CurrentRatingLevel);

        public static bool ClusterEnvironmentAllowed() =>
            GameComponent_CerebrexTakeoverState.IsActive
            || CurrentRatingLevel >= Config.mechClusterEnvironmentMinLevel;

        // ===== 特殊协议可用性（接管主脑后全部开放） =====

        public static bool IsClusterAvailable() =>
            GameComponent_CerebrexTakeoverState.IsActive || CurrentRatingLevel >= 1;

        public static bool IsForceSupportAvailable() =>
            GameComponent_CerebrexTakeoverState.IsActive || CurrentRatingLevel >= 1;

        /// <summary>
        /// 机械集群威胁点有效上限：接管主脑后回退到原版常量（完全开放），
        /// 否则按当前评级等级取值；0 级即未解锁（上限为 0）。
        /// </summary>
        public static int EffectiveClusterMaxThreat() =>
            GameComponent_CerebrexTakeoverState.IsActive
                ? MechClusterDeploymentOrder.MaxThreatPoints
                : ClusterMaxThreatPoints();

        /// <summary>
        /// 部队支援威胁点有效上限：接管主脑后完全开放；否则按当前评级等级取值。
        /// </summary>
        public static int EffectiveForceSupportMaxThreat() =>
            GameComponent_CerebrexTakeoverState.IsActive
                ? int.MaxValue
                : ForceSupportMaxThreatPoints();

        // ===== 世界目标基础奖励去重（幂等） =====

        /// <summary>
        /// 世界目标基础奖励（如摧毁据点 200 点）。按世界目标稳定ID持久化去重，防止重复结算。
        /// 奖励既增加肃清额度，也经统一入口同步增加等量评级。
        /// 返回 false 表示已结算或系统未激活（调用方不应重复发放）。
        /// </summary>
        public static bool TryGrantWorldTargetBaseReward(WorldObject target, int points)
        {
            if (!IsRatingSystemActive() || target == null || points <= 0)
            {
                return false;
            }

            MechanoidMechanitorPurgeDirectiveRuntimeState? rs = RuntimeState;
            if (rs == null)
            {
                return false;
            }

            string stableId = GetStableWorldTargetId(target);
            if (rs.HasAwardedBaseReward(stableId))
            {
                return false;
            }

            rs.MarkBaseRewardAwarded(stableId);
            return GameComponent_MechanoidMechanitorStoryState.TryAddPurgeDirectiveRewardPoints(points);
        }

        public static string GetStableWorldTargetId(WorldObject target)
        {
            if (target == null)
            {
                return "null";
            }

            // 优先复用原版据点稳定性ID（若存在），否则退回世界对象唯一加载ID。
            string? stability = TryGetFactionBaseStabilityId(target);
            if (stability != null)
            {
                return stability;
            }

            return target.GetUniqueLoadID();
        }

        private static string? TryGetFactionBaseStabilityId(WorldObject target)
        {
            // PurgeDirective_FactionBaseRewardPatches 中使用的扩展方法本体为私有，
            // 这里用等价逻辑：据点/前哨/站点均带稳定ID概念，统一用唯一加载ID即可保证去重正确性。
            return null;
        }
    }
}
