using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 机械巢节点材料交付与肃清额度换算的公共计算。价值一律以实际 <see cref="Thing.MarketValue"/>
    /// （单件市场价值）× 实际结算数量计算，兼容品质/材质/耐久度及其他 MOD 的价值修改。
    /// 额度奖励先按倍率汇总价值再向下取整，最后通过统一肃清额度接口增加。
    /// </summary>
    public static class MechHiveNodeDeliveryUtility
    {
        public const float DeliveryQuotaMultiplier = 0.5f;

        public const float ExcessQuotaMultiplier = 0.2f;

        /// <summary>单个 Thing（整堆）的市场总价值 = 单件市场价值 × 数量。</summary>
        public static float StackMarketValue(Thing thing)
        {
            if (thing == null)
            {
                return 0f;
            }

            return thing.MarketValue * thing.stackCount;
        }

        /// <summary>远行队中指定材料的可用总数。</summary>
        public static int CountInCaravan(Caravan caravan, ThingDef def)
        {
            if (caravan == null || def == null)
            {
                return 0;
            }

            int total = 0;
            List<Thing> items = CaravanInventoryUtility.AllInventoryItems(caravan);
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].def == def)
                {
                    total += items[i].stackCount;
                }
            }

            return total;
        }

        /// <summary>
        /// 从远行队多个堆叠中精确扣除 <paramref name="count"/> 件指定材料，返回实际扣除物品总价值。
        /// 数量不足返回 -1 且不扣除任何物品。其余物资保持不变。
        /// </summary>
        public static float TryConsumeFromCaravan(Caravan caravan, ThingDef def, int count)
        {
            if (caravan == null || def == null || count <= 0)
            {
                return -1f;
            }

            if (CountInCaravan(caravan, def) < count)
            {
                return -1f;
            }

            float removedValue = 0f;
            int remaining = count;
            List<Thing> items = CaravanInventoryUtility.AllInventoryItems(caravan);
            for (int i = 0; i < items.Count && remaining > 0; i++)
            {
                Thing thing = items[i];
                if (thing.def != def)
                {
                    continue;
                }

                int take = Mathf.Min(remaining, thing.stackCount);
                removedValue += thing.MarketValue * take;
                remaining -= take;

                if (take >= thing.stackCount)
                {
                    thing.Destroy();
                }
                else
                {
                    thing.stackCount -= take;
                }
            }

            return removedValue;
        }

        /// <summary>按倍率汇总价值向下取整后，通过统一肃清额度接口增加。仅在肃清指令开启时生效。</summary>
        public static void TryAddPurgeQuota(float totalValue, float multiplier)
        {
            if (totalValue <= 0f || multiplier <= 0f)
            {
                return;
            }

            if (!GameComponent_MechanoidMechanitorStoryState.IsPurgeDirectiveActive)
            {
                return;
            }

            int amount = Mathf.FloorToInt(totalValue * multiplier);
            if (amount > 0)
            {
                GameComponent_MechanoidMechanitorStoryState.TryAddPurgeDirectiveRewardPoints(amount);
            }
        }
    }
}
