using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 共生盟约「联合军事行动」的可读取配置入口。
    /// 所有天数与奖励数值集中在此，避免散落硬编码。
    /// 该机制只在 CurrentCovenantLevel >= 4（战略同盟）时生效。
    /// </summary>
    public sealed class SymbiosisCovenantJointOperationDef : Def
    {
        // 调度与倒计时（ticks）
        public int offerTimeoutTicks = 180000;            // 邀请等待玩家决定的时限：3 天
        public int operationTimeoutTicks = 900000;        // 接取后完成行动的时限：15 天
        public int declinedOrExpiredCooldownTicks = 300000; // 拒绝/过期后的冷却：5 天
        public int completedOrFailedCooldownTicks = 720000; // 成功或失败后的冷却：12 天
        public int invalidEndCooldownTicks = 300000;      // 无效结束后的冷却：5 天

        // 团结度与信任变化
        public int successUnityDelta = 35;
        public int failUnityDelta = -30;
        public int successTrustDeltaPerValidParticipant = 12;
        public int failTrustDeltaPerValidParticipant = -8;

        // 援军规模：所有参与派系“合计”的援军点数 = 目标威胁点 × supportPointsFactor
        //（不是每个派系各算一次；点数再平均分配给仍有效且能生成战斗编组的参与派系）。
        public float supportPointsFactor = 0.50f;
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

            if (supportPointsFactor < 0f)
            {
                yield return $"{defName}: supportPointsFactor cannot be negative.";
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
