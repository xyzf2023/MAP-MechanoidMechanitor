using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 肃清指令「节点评级」系统的统一配置入口。
    /// 所有等级阈值、折扣、重量级权限、物资分级、特殊协议上限、任务调度与处罚数值都必须集中在此，
    /// 不允许在 UI / 订单 / 扣款 / 交付 / 支援 / 集群 / 任务中分别维护互相漂移的等级表。
    /// </summary>
    public sealed class PurgeDirectiveRatingConfigDef : Def
    {
        // ===== 评级阈值（0 ~ maxRatingValue 钳制） =====
        // 等级 = 通过的阈值数量，钳制到 0..5。
        // L1=750, L2=1500, L3=2250, L4=3000, L5=maxRatingValue(4000 满级)。
        public int ratingLevel1Threshold = 750;
        public int ratingLevel2Threshold = 1500;
        public int ratingLevel3Threshold = 2250;
        public int ratingLevel4Threshold = 3000;
        public int maxRatingValue = 4000;

        // ===== 折扣（按等级 L1..L5） =====
        // 四、五级均为 20%。
        public float discountLevel1 = 0.05f;
        public float discountLevel2 = 0.10f;
        public float discountLevel3 = 0.15f;
        public float discountLevel4 = 0.20f;
        public float discountLevel5 = 0.20f;
        public float takeoverDiscount = 0.20f; // 接管主脑后机械族/物资享 20%

        // ===== 机械族重量级权限（所需最低评级等级） =====
        public int mechLightRequiredLevel = 1;
        public int mechMediumRequiredLevel = 2;
        public int mechHeavyRequiredLevel = 3;
        public int mechUltraHeavyRequiredLevel = 4;

        // ===== 物资自动分级市场价值边界（按基础市场价值） =====
        // 0~99→1, 100~499→2, 500~1999→3, 2000~4999→4, ≥5000→5。
        public float goodsTier1MaxMarketValue = 99f;
        public float goodsTier2MaxMarketValue = 499f;
        public float goodsTier3MaxMarketValue = 1999f;
        public float goodsTier4MaxMarketValue = 4999f;

        // ===== 特殊协议分级上限（按等级 L1..L5，下标 0..4） =====
        // 部队支援：1级≤5000, 2级≤10000, 3级≤20000, 4级≤10000, 5级与4级一致。
        public List<int> mechForceSupportMaxThreatPointsByLevel =
            new List<int> { 5000, 10000, 20000, 10000, 10000 };
        // 机械集群：1级≤10000, 2级≤20000, 3级≤30000, 4级≤40000, 5级与4级一致。
        public List<int> mechClusterMaxThreatPointsByLevel =
            new List<int> { 10000, 20000, 30000, 40000, 40000 };
        // 环境影响器（condition causer）只在达到该等级后允许。
        public int mechClusterEnvironmentMinLevel = 5;

        // ===== 肃清评级任务调度 =====
        public int questMinRatingLevel = 1;       // 评级≥1 才可调度
        public int questMaxActive = 1;            // 同一时间最多 1 个进行中
        public int questOfferTimeoutDays = 3;     // 玩家抉择期
        public int questOperationTimeoutDays = 15;// 接取后完成期限
        public int questIdleCooldownDays = 4;     // 无进行中任务且距上次结束满 4 天再尝试
        public int questBaseSuccessRewardPoints = 200; // 世界目标基础奖励（按稳定ID去重）

        // ===== 评级处罚（不扣肃清额度，仅减少评级值，且每阶段只处罚一次） =====
        // 血肉殖民者警告阶段（依靠现有持久化警告状态保证每阶段只触发一次）：
        public int yellowWarningPenalty = 100;   // 黄色肃清警告首次发生 -100
        public int orange1WarningPenalty = 250;  // 第一次橙色警告首次发生 -250
        public int orange2WarningPenalty = 500;  // 第二次橙色警告首次发生 -500
        // 肃清评级任务失败/放弃/超时（按目标类型区分）：
        public int workSiteFailurePenalty = 150; // 工作站 -150
        public int outpostFailurePenalty = 300;  // 前哨 -300
        public int baseFailurePenalty = 600;     // 据点 -600
        // 最终红色（终末）警告：评级直接归零（见 SetRatingDirect(0)）

        // ===== 信件（未配置则退回原版） =====
        public LetterDef? ratingUpgradeLetter;
        public LetterDef? ratingDowngradeLetter;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            if (ratingLevel1Threshold <= 0
                || ratingLevel2Threshold <= ratingLevel1Threshold
                || ratingLevel3Threshold <= ratingLevel2Threshold
                || ratingLevel4Threshold <= ratingLevel3Threshold)
            {
                yield return
                    $"{defName}: rating level thresholds must be strictly increasing and positive.";
            }

            if (maxRatingValue <= ratingLevel4Threshold)
            {
                yield return $"{defName}: maxRatingValue must be greater than level4 threshold.";
            }

            float[] discounts = { discountLevel1, discountLevel2, discountLevel3, discountLevel4, discountLevel5 };
            foreach (float d in discounts)
            {
                if (d < 0f || d >= 1f)
                {
                    yield return $"{defName}: discount rates must be in [0,1).";
                    break;
                }
            }

            if (takeoverDiscount < 0f || takeoverDiscount >= 1f)
            {
                yield return $"{defName}: takeoverDiscount must be in [0,1).";
            }

            if (mechForceSupportMaxThreatPointsByLevel == null
                || mechForceSupportMaxThreatPointsByLevel.Count != 5)
            {
                yield return $"{defName}: mechForceSupportMaxThreatPointsByLevel must have exactly 5 entries.";
            }
            else
            {
                foreach (int v in mechForceSupportMaxThreatPointsByLevel)
                {
                    if (v < 0)
                    {
                        yield return $"{defName}: mechForceSupportMaxThreatPointsByLevel cannot be negative.";
                        break;
                    }
                }
            }

            if (mechClusterMaxThreatPointsByLevel == null
                || mechClusterMaxThreatPointsByLevel.Count != 5)
            {
                yield return $"{defName}: mechClusterMaxThreatPointsByLevel must have exactly 5 entries.";
            }
            else
            {
                foreach (int v in mechClusterMaxThreatPointsByLevel)
                {
                    if (v < 0)
                    {
                        yield return $"{defName}: mechClusterMaxThreatPointsByLevel cannot be negative.";
                        break;
                    }
                }
            }

            if (mechClusterEnvironmentMinLevel < 1 || mechClusterEnvironmentMinLevel > 5)
            {
                yield return $"{defName}: mechClusterEnvironmentMinLevel must be in 1..5.";
            }

            if (questMinRatingLevel < 1 || questMinRatingLevel > 5)
            {
                yield return $"{defName}: questMinRatingLevel must be in 1..5.";
            }

            if (questMaxActive < 1)
            {
                yield return $"{defName}: questMaxActive must be >= 1.";
            }

            if (questOfferTimeoutDays <= 0 || questOperationTimeoutDays <= 0 || questIdleCooldownDays < 0)
            {
                yield return $"{defName}: quest timeout/cooldown values must be positive.";
            }

            if (yellowWarningPenalty < 0)
            {
                yield return $"{defName}: yellowWarningPenalty cannot be negative.";
            }

            if (orange1WarningPenalty < 0)
            {
                yield return $"{defName}: orange1WarningPenalty cannot be negative.";
            }

            if (orange2WarningPenalty < 0)
            {
                yield return $"{defName}: orange2WarningPenalty cannot be negative.";
            }

            if (workSiteFailurePenalty < 0
                || outpostFailurePenalty < 0
                || baseFailurePenalty < 0)
            {
                yield return $"{defName}: quest failure penalties cannot be negative.";
            }
        }

        /// <summary>
        /// 由评级值推导当前等级（0..5）。UI 与业务必须共用此方法，保证显示与结算一致。
        /// </summary>
        public int GetRatingLevel(int value)
        {
            if (value < 0)
            {
                return 0;
            }

            int level = 0;
            if (value >= ratingLevel1Threshold) level = 1;
            if (value >= ratingLevel2Threshold) level = 2;
            if (value >= ratingLevel3Threshold) level = 3;
            if (value >= ratingLevel4Threshold) level = 4;
            if (value >= maxRatingValue) level = 5;
            return level;
        }

        /// <summary>
        /// 指定等级对应的折扣率（0..1）。UI 显示与扣款必须都走这里。
        /// </summary>
        public float GetDiscountRateForLevel(int level)
        {
            switch (level)
            {
                case 1: return discountLevel1;
                case 2: return discountLevel2;
                case 3: return discountLevel3;
                case 4: return discountLevel4;
                case 5: return discountLevel5;
                default: return 0f;
            }
        }

        /// <summary>
        /// 机械族重量级所需最低评级等级。null 一律按轻量处理。
        /// </summary>
        public int RequiredLevelForMechWeight(MechWeightClassDef? weight)
        {
            if (weight == null || weight == MechWeightClassDefOf.Light) return mechLightRequiredLevel;
            if (weight == MechWeightClassDefOf.Medium) return mechMediumRequiredLevel;
            if (weight == MechWeightClassDefOf.Heavy) return mechHeavyRequiredLevel;
            if (weight == MechWeightClassDefOf.UltraHeavy) return mechUltraHeavyRequiredLevel;
            return mechLightRequiredLevel;
        }

        /// <summary>
        /// 物资按基础市场价值自动分级为 1..5。
        /// </summary>
        public int AutoGradeGoodsLevel(ThingDef? def)
        {
            float mv = def?.BaseMarketValue ?? 0f;
            if (mv <= goodsTier1MaxMarketValue) return 1;
            if (mv <= goodsTier2MaxMarketValue) return 2;
            if (mv <= goodsTier3MaxMarketValue) return 3;
            if (mv <= goodsTier4MaxMarketValue) return 4;
            return 5;
        }

        public int GetForceSupportMaxThreat(int level)
        {
            if (level < 1 || mechForceSupportMaxThreatPointsByLevel == null) return 0;
            if (level - 1 >= mechForceSupportMaxThreatPointsByLevel.Count) return 0;
            return mechForceSupportMaxThreatPointsByLevel[level - 1];
        }

        public int GetClusterMaxThreat(int level)
        {
            if (level < 1 || mechClusterMaxThreatPointsByLevel == null) return 0;
            if (level - 1 >= mechClusterMaxThreatPointsByLevel.Count) return 0;
            return mechClusterMaxThreatPointsByLevel[level - 1];
        }
    }

    [DefOf]
    public static class PurgeDirectiveRatingConfigDefOf
    {
        public static PurgeDirectiveRatingConfigDef MAP_PurgeDirectiveRatingConfig = null!;

        static PurgeDirectiveRatingConfigDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(PurgeDirectiveRatingConfigDefOf));
        }
    }
}
