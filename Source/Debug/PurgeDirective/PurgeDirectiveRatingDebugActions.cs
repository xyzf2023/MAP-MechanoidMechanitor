using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 肃清指令节点评级系统的 DEV 诊断与测试入口（§9）。
    /// 仅用于调试，不影响正式玩法逻辑。
    /// </summary>
    public static class PurgeDirectiveRatingDebugActions
    {
        private const string Category = "MAP-机械族机械师";

        [DebugAction(
            Category,
            "评级 +100",
            false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Playing,
            requiresIdeology = false,
            requiresRoyalty = false,
            requiresBiotech = false,
            requiresAnomaly = false)]
        private static void AddRating100()
        {
            PurgeDirectiveRatingUtility.TryAddRating(100, "DEV");
        }

        [DebugAction(
            Category,
            "评级 +500",
            false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Playing,
            requiresIdeology = false,
            requiresRoyalty = false,
            requiresBiotech = false,
            requiresAnomaly = false)]
        private static void AddRating500()
        {
            PurgeDirectiveRatingUtility.TryAddRating(500, "DEV");
        }

        [DebugAction(
            Category,
            "评级 +1000",
            false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Playing,
            requiresIdeology = false,
            requiresRoyalty = false,
            requiresBiotech = false,
            requiresAnomaly = false)]
        private static void AddRating1000()
        {
            PurgeDirectiveRatingUtility.TryAddRating(1000, "DEV");
        }

        [DebugAction(
            Category,
            "评级清零",
            false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Playing,
            requiresIdeology = false,
            requiresRoyalty = false,
            requiresBiotech = false,
            requiresAnomaly = false)]
        private static void ClearRating()
        {
            PurgeDirectiveRatingUtility.SetRatingDirect(0, "DEV");
        }

        [DebugAction(
            Category,
            "评级设为满级(4000)",
            false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Playing,
            requiresIdeology = false,
            requiresRoyalty = false,
            requiresBiotech = false,
            requiresAnomaly = false)]
        private static void SetRatingMax()
        {
            PurgeDirectiveRatingUtility.SetRatingDirect(
                PurgeDirectiveRatingConfigDefOf.MAP_PurgeDirectiveRatingConfig.maxRatingValue,
                "DEV");
        }

        [DebugAction(
            Category,
            "强制肃清评级任务立即调度",
            false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Playing,
            requiresIdeology = false,
            requiresRoyalty = false,
            requiresBiotech = false,
            requiresAnomaly = false)]
        private static void ForceQuestDue()
        {
            PurgeDirectiveQuestScheduler.ForceDueNow();
        }

        [DebugAction(
            Category,
            "清除肃清评级任务冷却",
            false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Playing,
            requiresIdeology = false,
            requiresRoyalty = false,
            requiresBiotech = false,
            requiresAnomaly = false)]
        private static void ClearQuestCooldown()
        {
            PurgeDirectiveQuestScheduler.ClearCooldown();
        }

        [DebugAction(
            Category,
            "对最近敌对据点发放基础奖励(去重)",
            false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Playing,
            requiresIdeology = false,
            requiresRoyalty = false,
            requiresBiotech = false,
            requiresAnomaly = false)]
        private static void GrantBaseRewardToNearestHostileBase()
        {
            WorldObject? target = Find.WorldObjects.Settlements
                .FirstOrDefault(s => PurgeDirectiveQuestTargetUtility.IsValidTarget(s))
                ?? (WorldObject?)Find.WorldObjects.Sites
                    .FirstOrDefault(s => PurgeDirectiveQuestTargetUtility.IsValidTarget(s));
            if (target == null)
            {
                Log.Message("[MAP] 肃清评级 DEBUG：未找到可发放奖励的敌对据点。");
                return;
            }

            bool granted = GameComponent_MechanoidMechanitorStoryState
                .TryAddPurgeDirectiveWorldTargetBaseReward(
                    target,
                    PurgeDirectiveQuestConfigDefOf.MAP_PurgeDirectiveQuestConfig.baseSuccessRewardPoints);
            Log.Message(
                $"[MAP] 肃清评级 DEBUG：基础奖励发放 = {granted}（稳定ID={PurgeDirectiveRatingUtility.GetStableWorldTargetId(target)}）");
        }

        [DebugAction(
            Category,
            "日志：当前肃清评级状态",
            false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Playing,
            requiresIdeology = false,
            requiresRoyalty = false,
            requiresBiotech = false,
            requiresAnomaly = false)]
        private static void LogRatingState()
        {
            int level = PurgeDirectiveRatingUtility.CurrentRatingLevel;
            int value = PurgeDirectiveRatingUtility.RatingValue;
            float discount = PurgeDirectiveRatingUtility.GetDiscountRate();
            bool active = PurgeDirectiveRatingUtility.IsRatingSystemActive();
            List<string> flags = new List<string>
            {
                $"活跃={active}",
                $"等级={level}",
                $"评级值={value}",
                $"折扣={discount:P0}",
                $"机械族解锁Lv={PurgeDirectiveRatingUtility.RequiredLevelForMechWeight(null)}",
                $"部队支援上限={PurgeDirectiveRatingUtility.ForceSupportMaxThreatPoints()}",
                $"机械集群上限={PurgeDirectiveRatingUtility.ClusterMaxThreatPoints()}",
                $"环境影响器={PurgeDirectiveRatingUtility.ClusterEnvironmentAllowed()}",
            };
            Log.Message("[MAP] 肃清评级状态： " + string.Join("，", flags));
        }
    }
}
