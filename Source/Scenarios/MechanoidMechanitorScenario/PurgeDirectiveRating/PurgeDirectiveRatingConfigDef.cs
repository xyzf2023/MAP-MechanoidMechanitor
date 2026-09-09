using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 肃清指令「节点评级」系统的统一配置入口。
    /// 所有等级阈值、折扣、重量级权限、物资分级、特殊协议上限、任务调度与处罚数值都必须集中在此，
    /// 不允许在 UI / 订单 / 扣款 / 交付 / 支援 / 集群 / 任务中分别维护互相漂移的等级表。
    ///
    /// 评级值固定区间 0~4000，五个等级起点固定为 0 / 750 / 1500 / 2250 / 3000。
    /// 3000~4000 为五级缓冲区，不产生第六级。
    /// </summary>
    public sealed class PurgeDirectiveRatingConfigDef : Def
    {
        // ===== 评级等级起点（0~maxRatingValue 钳制） =====
        // 等级只由评级值推导，不在别处保存等级字段。
        public int ratingLevelStart1 = 0;    // 一级评级起点
        public int ratingLevelStart2 = 750;  // 二级评级起点
        public int ratingLevelStart3 = 1500; // 三级评级起点
        public int ratingLevelStart4 = 2250; // 四级评级起点
        public int ratingLevelStart5 = 3000; // 五级评级起点
        public int maxRatingValue = 4000;    // 评级上限（五级缓冲终点）

        // ===== 折扣（按等级 L1..L5） =====
        // 一、二、三级均为 0%；四级 10%；五级与接管主脑 20%。
        public float discountLevel1 = 0f;
        public float discountLevel2 = 0f;
        public float discountLevel3 = 0f;
        public float discountLevel4 = 0.10f;
        public float discountLevel5 = 0.20f;
        public float takeoverDiscount = 0.20f; // 接管主脑后机械族/物资享 20%

        // ===== 机械族重量级权限（所需最低评级等级） =====
        // 判级只按 MechWeightClassDef；非空且不等于原版四种重量级的第三方自定义重量级按四级处理。
        public int mechLightRequiredLevel = 1;
        public int mechMediumRequiredLevel = 2;
        public int mechHeavyRequiredLevel = 3;
        public int mechUltraHeavyRequiredLevel = 4;

        // ===== 物资三级自动分类边界（按目录条目的默认参考单位市场价值） =====
        // 固定三级：可搬运建筑(Building)、Special 分类、默认参考单位价值 > specialMarketValueThreshold。
        // 默认一级：食物；默认参考单位价值 <= medicineTier1MaxValue 的药品；
        //          默认参考单位价值 <= materialTier1MaxValue 的原材料；
        //          默认参考单位价值 <= otherTier1MaxValue 的 Other。
        // 默认二级：武器、服装；以及超过一级门槛但未触发三级的药品/材料/Other。
        public float goodsSpecialMarketValueThreshold = 2000f;
        public float goodsMedicineTier1MaxValue = 25f;
        public float goodsMaterialTier1MaxValue = 10f;
        public float goodsOtherTier1MaxValue = 50f;

        // ===== 机械巢节点建设贡献评级 =====
        // 仅实际满足节点当前需求的物资价值参与评级；超额与非需求交付只结算肃清额度。
        public float nodeDemandDeliveryRatingMultiplier = 0.075f;

        // ===== 特殊协议分级上限（按等级 L1..L5，下标 0..4） =====
        // 0 表示在该等级锁定。列表末项使用 int.MaxValue 表示「无硬上限」（沿用原版警告机制）。
        // 部队支援：L1锁定, L2≤5000, L3≤10000, L4≤20000, L5无硬上限。
        public List<int> mechForceSupportMaxThreatPointsByLevel =
            new List<int> { 0, 5000, 10000, 20000, int.MaxValue };
        // 机械集群：L1~L3锁定, L4≤5000, L5≤10000。
        public List<int> mechClusterMaxThreatPointsByLevel =
            new List<int> { 0, 0, 0, 5000, 10000 };
        // 环境影响器（condition causer）只在达到该等级后允许。
        public int mechClusterEnvironmentMinLevel = 5;

        // ===== 肃清评级任务调度（唯一权威数值源） =====
        public int questMinRatingLevel = 1;       // 额定最低等级（修复后 0 点即一级，此值仅用于下限保护）
        public int questMaxActive = 1;            // 同一时间最多 1 个（含待接受邀请）
        public int questOfferTimeoutDays = 3;     // 玩家抉择期（天），超过则按拒绝（无处罚）结束
        public int questOperationTimeoutDays = 15;// 接取后完成期限（天），超时按任务失败
        public int questFirstDelayDaysMin = 4;    // 新游戏/旧存档首次任务：随机下限（天）
        public int questFirstDelayDaysMax = 6;    // 新游戏/旧存档首次任务：随机上限（天）
        public int questRetryDelayDaysMin = 7;    // 任意结束后再尝试：随机下限（天）
        public int questRetryDelayDaysMax = 10;   // 任意结束后再尝试：随机上限（天）
        public int questNoTargetRetryDays = 1;    // 到期但无合法目标：1 天后重试

        // ===== 肃清评级任务奖励（唯一权威数值源） =====
        // 基础攻克奖励（按世界目标稳定ID去重，同时增加肃清额度与等量评级）。
        public int questBaseRewardPoints = 200;
        // 任务额外奖励（按目标类型，仅成功时发放，同时增加肃清额度与等量评级）。
        public int questWorkSiteExtraRewardPoints = 150;
        public int questOutpostExtraRewardPoints = 300;
        public int questSettlementExtraRewardPoints = 600;

        // ===== 评级处罚（不扣肃清额度，仅减少评级值，且每阶段只处罚一次） =====
        // 血肉殖民者警告阶段（依靠现有持久化警告状态保证每阶段只触发一次）：
        public int yellowWarningPenalty = 100;   // 黄色肃清警告首次发生 -100
        public int orange1WarningPenalty = 250;  // 第一次橙色警告首次发生 -250
        public int orange2WarningPenalty = 500;  // 第二次橙色警告首次发生 -500
        // 肃清评级任务失败/放弃/超时（按目标类型区分，不扣肃清额度）：
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

            if (ratingLevelStart1 != 0)
            {
                yield return $"{defName}: ratingLevelStart1 must be 0 (level 1 always covers 0).";
            }

            if (ratingLevelStart2 <= ratingLevelStart1
                || ratingLevelStart3 <= ratingLevelStart2
                || ratingLevelStart4 <= ratingLevelStart3
                || ratingLevelStart5 <= ratingLevelStart4)
            {
                yield return
                    $"{defName}: rating level starts must be strictly increasing (0<750<1500<2250<3000).";
            }

            if (maxRatingValue <= ratingLevelStart5)
            {
                yield return $"{defName}: maxRatingValue must be greater than level5 start (3000).";
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

            if (goodsSpecialMarketValueThreshold <= 0f
                || goodsMedicineTier1MaxValue < 0f
                || goodsMaterialTier1MaxValue < 0f
                || goodsOtherTier1MaxValue < 0f)
            {
                yield return $"{defName}: goods tier thresholds must be non-negative.";
            }

            if (nodeDemandDeliveryRatingMultiplier < 0f)
            {
                yield return $"{defName}: nodeDemandDeliveryRatingMultiplier cannot be negative.";
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

            if (questOfferTimeoutDays <= 0 || questOperationTimeoutDays <= 0
                || questFirstDelayDaysMin <= 0 || questFirstDelayDaysMax < questFirstDelayDaysMin
                || questRetryDelayDaysMin <= 0 || questRetryDelayDaysMax < questRetryDelayDaysMin
                || questNoTargetRetryDays <= 0)
            {
                yield return $"{defName}: quest timing values must be positive and ranges ordered.";
            }

            if (questBaseRewardPoints < 0
                || questWorkSiteExtraRewardPoints < 0
                || questOutpostExtraRewardPoints < 0
                || questSettlementExtraRewardPoints < 0)
            {
                yield return $"{defName}: quest reward values must be >= 0.";
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
        /// 由评级值推导当前等级（1..5）。0 点也是一级，3000 点及以上为五级；不允许 0 级。
        /// UI 与业务必须共用此方法，保证显示与结算一致。
        /// </summary>
        public int GetRatingLevel(int value)
        {
            if (value < ratingLevelStart1)
            {
                value = ratingLevelStart1;
            }

            if (value >= ratingLevelStart5) return 5;
            if (value >= ratingLevelStart4) return 4;
            if (value >= ratingLevelStart3) return 3;
            if (value >= ratingLevelStart2) return 2;
            return 1;
        }

        /// <summary>指定等级的起点评级值（等级 1 返回 0）。</summary>
        public int GetLevelStart(int level)
        {
            switch (level)
            {
                case 1: return ratingLevelStart1;
                case 2: return ratingLevelStart2;
                case 3: return ratingLevelStart3;
                case 4: return ratingLevelStart4;
                case 5: return ratingLevelStart5;
                default: return ratingLevelStart1;
            }
        }

        /// <summary>下一等级的起点评级值；已满级时返回最大评级值。</summary>
        public int GetNextLevelStart(int level)
        {
            switch (level)
            {
                case 1: return ratingLevelStart2;
                case 2: return ratingLevelStart3;
                case 3: return ratingLevelStart4;
                case 4: return ratingLevelStart5;
                default: return maxRatingValue;
            }
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
        /// 机械族重量级所需最低评级等级。null 一律按轻量处理；
        /// 非空且不等于原版四种重量级的第三方自定义重量级按最高四级权限处理。
        /// </summary>
        public int RequiredLevelForMechWeight(MechWeightClassDef? weight)
        {
            if (weight == null || weight == MechWeightClassDefOf.Light) return mechLightRequiredLevel;
            if (weight == MechWeightClassDefOf.Medium) return mechMediumRequiredLevel;
            if (weight == MechWeightClassDefOf.Heavy) return mechHeavyRequiredLevel;
            if (weight == MechWeightClassDefOf.UltraHeavy) return mechUltraHeavyRequiredLevel;
            return mechUltraHeavyRequiredLevel;
        }

        /// <summary>
        /// 物资按目录类别与默认参考单位市场价值自动分级为 1..3（固定三级安全规则）。
        /// 调用方需先经黑名单与 XML 覆盖判断；本方法只负责自动分类。
        /// </summary>
        public int ClassifyGoodsLevel(
            MechanoidOvermindThingCategory category,
            float defaultReferenceMarketValue)
        {
            // 固定三级：可搬运建筑、Special、默认参考单位价值过高。
            if (category == MechanoidOvermindThingCategory.Building
                || category == MechanoidOvermindThingCategory.Special)
            {
                return 3;
            }

            if (defaultReferenceMarketValue > goodsSpecialMarketValueThreshold)
            {
                return 3;
            }

            switch (category)
            {
                case MechanoidOvermindThingCategory.Food:
                    return 1;
                case MechanoidOvermindThingCategory.Medicine:
                    return defaultReferenceMarketValue <= goodsMedicineTier1MaxValue ? 1 : 2;
                case MechanoidOvermindThingCategory.Material:
                    return defaultReferenceMarketValue <= goodsMaterialTier1MaxValue ? 1 : 2;
                case MechanoidOvermindThingCategory.Other:
                    return defaultReferenceMarketValue <= goodsOtherTier1MaxValue ? 1 : 2;
                case MechanoidOvermindThingCategory.Weapon:
                case MechanoidOvermindThingCategory.Apparel:
                    return 2;
                default:
                    return 2;
            }
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
