using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 共生盟约「联合军事行动」的可读取配置入口。
    /// 所有天数与奖励数值集中在此，避免散落硬编码。
    /// 该机制只在 CurrentCovenantLevel >= MinimumCovenantLevel（L2）时生效。
    /// </summary>
    public sealed class SymbiosisCovenantJointOperationDef : Def
    {
        /// <summary>
        /// 联合军事行动的解锁等级。所有「等级是否够」的判断都必须引用这里，
        /// 不允许在调度器 / QuestPart / UI 里散写 2 或 4。
        /// </summary>
        public const int MinimumCovenantLevel = 2;

        /// <summary>
        /// 旧存档兼容：更新前已接受、尚未写入等级快照的行动，其援军承诺固定为旧版 50%。
        /// 不允许按读档时的当前等级重新计算，否则旧存档行动会突然从 50% 变成 75%。
        /// </summary>
        public const float LegacySupportPointsFactor = 0.50f;

        // 调度与倒计时（ticks）
        public int offerTimeoutTicks = 180000;            // 邀请等待玩家决定的时限：3 天
        public int operationTimeoutTicks = 900000;        // 接取后完成行动的时限：15 天
        public int declinedOrExpiredCooldownTicks = 300000; // 拒绝/过期后的冷却：5 天
        public int completedOrFailedCooldownTicks = 720000; // 成功或失败后的冷却：12 天
        public int invalidEndCooldownTicks = 300000;      // 无效结束后的冷却：5 天

        // 团结度与信任变化
        public int successUnityDelta = 35;
        public int failUnityDelta = -30;
        public int successTrustDeltaPerValidParticipant = 35;
        public int failTrustDeltaPerValidParticipant = -8;

        // 援军规模：所有参与派系“合计”的援军点数 = 目标威胁点 × 接取时锁定的等级倍率
        //（不是每个派系各算一次；点数再平均分配给仍有效且能生成战斗编组的参与派系）。
        // 定位：L2 观察性小规模支援 → L3 正式作战力量 → L4 既有强度 → L5 接近主力规模。
        public float level2SupportPointsFactor = 0.15f;
        public float level3SupportPointsFactor = 0.30f;
        public float level4SupportPointsFactor = 0.50f;
        public float level5SupportPointsFactor = 0.75f;
        public int maxParticipants = 3;                   // 含发起者，玩家不计入
        public TechLevel industrialArrivalThreshold = TechLevel.Industrial;

        // 奖励：实物奖励本次暂不发放（见 QuestPart.GrantReward 注释）。
        // rewardValueFactor / minRewardValue / maxRewardValue 仅保留供未来以原版 Quest reward 路径扩展。
        public float rewardValueFactor = 0.5f;
        public int minRewardValue = 300;
        public int maxRewardValue = 3000;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            if (offerTimeoutTicks <= 0)
            {
                yield return $"{defName}: offerTimeoutTicks must be positive.";
            }

            if (operationTimeoutTicks <= 0)
            {
                yield return $"{defName}: operationTimeoutTicks must be positive.";
            }

            if (declinedOrExpiredCooldownTicks < 0
                || completedOrFailedCooldownTicks < 0
                || invalidEndCooldownTicks < 0)
            {
                yield return $"{defName}: cooldown ticks cannot be negative.";
            }

            if (successUnityDelta < 0)
            {
                yield return $"{defName}: successUnityDelta must be non-negative.";
            }

            if (failUnityDelta > 0)
            {
                yield return $"{defName}: failUnityDelta must be non-positive.";
            }

            if (successTrustDeltaPerValidParticipant < 0)
            {
                yield return $"{defName}: successTrustDeltaPerValidParticipant must be non-negative.";
            }

            if (failTrustDeltaPerValidParticipant > 0)
            {
                yield return $"{defName}: failTrustDeltaPerValidParticipant must be non-positive.";
            }

            if (level2SupportPointsFactor < 0f
                || level3SupportPointsFactor < 0f
                || level4SupportPointsFactor < 0f
                || level5SupportPointsFactor < 0f)
            {
                yield return $"{defName}: support points factors (L2..L5) cannot be negative.";
            }

            // 必须非递减：等级越高，盟友投入的援军比例不应反而变小。
            if (level2SupportPointsFactor > level3SupportPointsFactor
                || level3SupportPointsFactor > level4SupportPointsFactor
                || level4SupportPointsFactor > level5SupportPointsFactor)
            {
                yield return $"{defName}: support points factors (L2..L5) must be non-decreasing.";
            }

            if (maxParticipants < 1 || maxParticipants > 3)
            {
                yield return $"{defName}: maxParticipants must be in 1..3.";
            }

            if (rewardValueFactor <= 0f)
            {
                yield return $"{defName}: rewardValueFactor must be positive.";
            }

            if (minRewardValue < 0 || maxRewardValue < minRewardValue)
            {
                yield return $"{defName}: reward value range is invalid.";
            }
        }

        /// <summary>
        /// 指定盟约等级对应的援军点数倍率。UI 与实际业务必须都走这里，
        /// 保证玩家看到的百分比就是部署时真正使用的百分比。
        /// 低于解锁等级时返回 L2 倍率（只用于防御性读取，正常情况下不应发生）。
        /// </summary>
        public float GetSupportPointsFactorForLevel(int covenantLevel)
        {
            if (covenantLevel >= 5)
            {
                return level5SupportPointsFactor;
            }

            if (covenantLevel == 4)
            {
                return level4SupportPointsFactor;
            }

            if (covenantLevel == 3)
            {
                return level3SupportPointsFactor;
            }

            return level2SupportPointsFactor;
        }
    }

    [DefOf]
    public static class SymbiosisCovenantJointOperationDefOf
    {
        public static SymbiosisCovenantJointOperationDef MAP_SymbiosisCovenant_JointOperationConfig = null!;

        static SymbiosisCovenantJointOperationDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(SymbiosisCovenantJointOperationDefOf));
        }
    }

    /// <summary>
    /// 单个参与派系在援军生成时的支持记录，随 QuestPart 存档。
    /// 用于恢复追踪本行动自己的援军 Lord（actionId + aidTag + 自定义 LordJob）。
    /// </summary>
    public sealed class SymbiosisCovenantJointOperationFactionSupportRecord : IExposable
    {
        public Faction? faction;
        public float supportPoints;
        public int pawnCount;
        public string? aidTag;

        public SymbiosisCovenantJointOperationFactionSupportRecord()
        {
        }

        public SymbiosisCovenantJointOperationFactionSupportRecord(
            Faction faction,
            float supportPoints,
            int pawnCount,
            string? aidTag)
        {
            this.faction = faction;
            this.supportPoints = supportPoints;
            this.pawnCount = pawnCount;
            this.aidTag = aidTag;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref supportPoints, "supportPoints", 0f);
            Scribe_Values.Look(ref pawnCount, "pawnCount", 0);
            Scribe_Values.Look(ref aidTag, "aidTag");
        }
    }
}
