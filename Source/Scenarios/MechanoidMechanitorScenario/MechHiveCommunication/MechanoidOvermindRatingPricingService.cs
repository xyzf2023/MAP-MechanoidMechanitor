using System;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 肃清评级折扣计价服务。UI 显示、订单余额校验与扣款必须都读取本服务计算出的
    /// 最终费用（含折扣），保证「折扣显示 / 余额检查 / 实际扣款」三者完全一致。
    /// 折扣率统一来自 PurgeDirectiveRatingUtility，禁止在此另行维护等级表。
    /// 最终费用固定为 ceil(折扣前合计 * (1 - 折扣率))，非空订单最终费用不低于 1。
    /// 折扣金额 = 折扣前合计 - 最终费用（不使用 RoundToInt 后相减）。
    /// 特殊协议费用不经过该折扣服务。
    /// </summary>
    public static class MechanoidOvermindRatingPricingService
    {
        /// <summary>
        /// 计算订单最终费用（含评级折扣）。所有调用方（UI / 余额校验 / 扣款）必须走这里。
        /// 折扣只作用于机械族调拨与物资请求子计（机械族小计 + 物资小计）合计后统一应用。
        /// </summary>
        public static bool TryCalculateFinalOrderCosts(
            MechanoidOvermindOrder order,
            out int mechPre,
            out int thingPre,
            out float discountRate,
            out int discountAmount,
            out int finalCost)
        {
            mechPre = 0;
            thingPre = 0;
            discountRate = 0f;
            discountAmount = 0;
            finalCost = 0;

            if (order == null
                || !MechanoidOvermindPricingService.TryCalculateOrderCosts(
                    order,
                    out mechPre,
                    out thingPre,
                    out _))
            {
                return false;
            }

            long preTotalLong = (long)mechPre + (long)thingPre;
            discountRate = PurgeDirectiveRatingUtility.GetDiscountRate();
            ApplyDiscount(preTotalLong, discountRate, out long finalLong, out long discountLong);
            finalCost = (int)finalLong;
            discountAmount = (int)discountLong;
            return true;
        }

        /// <summary>
        /// 单件/单个单位价格经折扣后的展示价（用于 UI 行内展示），
        /// 使用与订单合计相同的向上取整原则。
        /// </summary>
        public static int GetDiscountedUnitPrice(int baseUnitPrice)
        {
            if (baseUnitPrice <= 0)
            {
                return 0;
            }

            float rate = PurgeDirectiveRatingUtility.GetDiscountRate();
            ApplyDiscount(baseUnitPrice, rate, out long finalLong, out _);
            return (int)finalLong;
        }

        /// <summary>
        /// 给定折扣前合计与折扣率，统一计算（最终费用, 折扣金额）。
        /// 最终费用 = ceil(合计 * (1 - 折扣率))，非空订单不低于 1；
        /// 折扣金额 = 合计 - 最终费用。全程使用 long，避免加法/乘法溢出。
        /// </summary>
        public static void ApplyDiscount(long preTotal, float discountRate, out long finalCost, out long discountAmount)
        {
            if (preTotal <= 0L)
            {
                finalCost = 0L;
                discountAmount = 0L;
                return;
            }

            double factor = 1.0 - (double)discountRate;
            if (factor < 0.0) factor = 0.0;
            long final = (long)Math.Ceiling(preTotal * factor);
            if (final < 1L) final = 1L; // 非空订单最终费用不得低于 1
            finalCost = final;
            discountAmount = preTotal - final;
        }

        public static float CurrentDiscountRate => PurgeDirectiveRatingUtility.GetDiscountRate();
    }
}
