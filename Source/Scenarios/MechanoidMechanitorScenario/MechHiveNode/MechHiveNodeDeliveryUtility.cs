using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 机械巢节点材料交付与肃清额度/评级换算的公共计算。价值一律以实际 <see cref="Thing.MarketValue"/>
    /// （单件市场价值）× 实际结算数量计算，兼容品质/材质/耐久度及其他 MOD 的价值修改。
    /// 满足当前需求的部分按 0.5 折算肃清额度，并按评级配置额外增加节点评级；
    /// 超额或非需求部分仅按 0.2 折算肃清额度，不增加评级。
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
            return TryConsumeFromCaravan(caravan, def, count, out _);
        }

        public static float TryConsumeFromCaravan(
            Caravan caravan, ThingDef def, int count, out bool quotaUnavailable)
        {
            quotaUnavailable = false;
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
            // 原版返回共享静态列表；价值查询和 Destroy 回调都可能再次查询它。
            List<Thing> items = new List<Thing>(CaravanInventoryUtility.AllInventoryItems(caravan));
            List<KeyValuePair<Thing, int>> allocations = new List<KeyValuePair<Thing, int>>();
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
                allocations.Add(new KeyValuePair<Thing, int>(thing, take));
            }

            if (remaining > 0)
            {
                return -1f;
            }

            // 正常中立建设不要求肃清奖励；奖励路线则先拒绝无账户、非法价值及溢出，
            // 避免已知无法入账时仍消耗材料；这不承诺外部销毁回调异常时全量回滚。
            if ((GameComponent_CerebrexTakeoverState.IsActive
                    || GameComponent_MechanoidMechanitorStoryState.IsPurgeDirectiveActive)
                && !CanAddPurgeQuota(removedValue, DeliveryQuotaMultiplier))
            {
                quotaUnavailable = true;
                return -1f;
            }

            for (int i = 0; i < allocations.Count; i++)
            {
                Thing thing = allocations[i].Key;
                int take = allocations[i].Value;

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

        private static bool CanAddPurgeQuota(float totalValue, float multiplier)
        {
            if (!TryCalculateQuota(totalValue, multiplier, out int amount))
            {
                return false;
            }

            if (GameComponent_CerebrexTakeoverState.IsActive)
            {
                return GameComponent_CerebrexTakeoverState.Current != null
                    && (long)GameComponent_MechanoidMechanitorStoryState.GetPurgeDirectiveRewardPoints()
                        + amount <= int.MaxValue;
            }

            MechanoidMechanitorPurgeDirectiveRuntimeState? runtime = PurgeDirectiveRatingUtility.Runtime;
            return runtime != null && (long)runtime.RewardPoints + amount <= int.MaxValue;
        }

        internal static bool TryCalculateQuota(float totalValue, float multiplier, out int amount)
        {
            // 保留原有 float 乘法再向下取整的计价，只在转 int 前排除非法值和溢出。
            double rounded = System.Math.Floor(totalValue * multiplier);
            amount = 0;
            if (float.IsNaN(totalValue) || float.IsInfinity(totalValue) || totalValue < 0f
                || float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier <= 0f
                || double.IsNaN(rounded) || double.IsInfinity(rounded) || rounded > int.MaxValue)
            {
                return false;
            }

            amount = (int)rounded;
            return true;
        }

        internal static void ReportQuotaFailure(float totalValue, float multiplier)
        {
            Log.Error($"[MAP-机械族机械师] 节点交付额度结算失败：价值={totalValue}，倍率={multiplier}，"
                + $"接管={GameComponent_CerebrexTakeoverState.IsActive}。未登记成功，也未自动补偿物资。");
            try
            {
                Messages.Message("MAP_MechanoidMechanitor.MechHiveNode.QuotaFailed".Translate(),
                    MessageTypeDefOf.NegativeEvent, historical: false);
            }
            catch (System.Exception ex)
            {
                // 失败提示不是交付提交点，通知异常不能再次打断运输容器的既定收尾。
                Log.Error("[MAP-机械族机械师] 节点交付失败提示发送异常：" + ex);
            }
        }

        /// <summary>
        /// 按倍率汇总价值向下取整后增加肃清额度。仅在肃清指令可用时生效。
        /// 当前仅有 DeliveryQuotaMultiplier(0.5) 表示“实际满足节点需求”的交付，
        /// 此时在额度成功结算后，再按统一评级配置对同一有效需求价值增加节点评级；
        /// ExcessQuotaMultiplier(0.2) 及其他倍率只增加额度，不增加评级。
        /// </summary>
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

            if (!TryCalculateQuota(totalValue, multiplier, out int amount))
            {
                ReportQuotaFailure(totalValue, multiplier);
                return;
            }
            if (amount == 0) return;
            if (!TryAddPurgeCreditsOnly(amount))
            {
                ReportQuotaFailure(totalValue, multiplier);
                return;
            }

            if (!Mathf.Approximately(multiplier, DeliveryQuotaMultiplier)
                || GameComponent_CerebrexTakeoverState.IsActive)
            {
                return;
            }

            float ratingMultiplier = PurgeDirectiveRatingUtility.Config.nodeDemandDeliveryRatingMultiplier;
            if (ratingMultiplier <= 0f)
            {
                return;
            }

            int ratingAmount = Mathf.FloorToInt(totalValue * ratingMultiplier);
            if (ratingAmount > 0)
            {
                PurgeDirectiveRatingUtility.TryAddRating(ratingAmount);
            }
        }

        /// <summary>
        /// 节点交付专用的“只增加肃清额度”入口。普通肃清状态直接写入额度字段，不同步增加评级；
        /// 接管主脑后仅在节点作用域内转入主脑资源额度。所有路径均先做 int 溢出保护。
        /// </summary>
        private static bool TryAddPurgeCreditsOnly(int amount)
        {
            if (amount <= 0)
            {
                return false;
            }

            if (GameComponent_CerebrexTakeoverState.IsActive)
            {
                GameComponent_CerebrexTakeoverState? takeoverState =
                    GameComponent_CerebrexTakeoverState.Current;
                return takeoverState != null
                    && CerebrexTakeoverNodeScope.Active
                    && takeoverState.TryAddCredits(amount);
            }

            if (!GameComponent_MechanoidMechanitorStoryState.IsPurgeDirectiveActive)
            {
                return false;
            }

            MechanoidMechanitorPurgeDirectiveRuntimeState? runtime =
                Current.Game?
                    .GetComponent<GameComponent_MechanoidMechanitorStoryState>()?
                    .PurgeDirectiveRuntimeState;
            if (runtime == null)
            {
                return false;
            }

            long next = (long)runtime.RewardPoints + amount;
            if (next > int.MaxValue)
            {
                return false;
            }

            runtime.AddRewardPoints(amount);
            return true;
        }
    }
}
