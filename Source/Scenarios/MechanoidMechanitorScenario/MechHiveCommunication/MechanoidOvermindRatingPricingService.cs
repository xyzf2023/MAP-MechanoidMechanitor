using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 肃清评级折扣计价服务。UI 显示、订单余额校验与扣款必须都读取本服务计算出的
    /// 最终费用（含折扣），保证「折扣显示 / 余额检查 / 实际扣款」三者完全一致。
    /// 折扣率统一来自 PurgeDirectiveRatingUtility，禁止在此另行维护等级表。
    /// </summary>
    public static class MechanoidOvermindRatingPricingService
    {
        /// <summary>
        /// 计算订单最终费用（含评级折扣）。所有调用方（UI / 余额校验 / 扣款）必须走这里。
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

            int preTotal = mechPre + thingPre;
            discountRate = PurgeDirectiveRatingUtility.GetDiscountRate();
            discountAmount = PurgeDirectiveRatingUtility.GetDiscountAmount(preTotal);
            finalCost = System.Math.Max(0, preTotal - discountAmount);
            return true;
        }

        /// <summary>
        /// 单件/单个单位价格经折扣后的展示价（用于 UI 行内展示）。
        /// </summary>
        public static int GetDiscountedUnitPrice(int baseUnitPrice)
        {
            if (baseUnitPrice <= 0)
            {
                return 0;
            }

            float rate = PurgeDirectiveRatingUtility.GetDiscountRate();
            int discount = PurgeDirectiveRatingUtility.GetDiscountAmount(baseUnitPrice);
            return System.Math.Max(0, baseUnitPrice - discount);
        }

        public static float CurrentDiscountRate => PurgeDirectiveRatingUtility.GetDiscountRate();
    }
}
