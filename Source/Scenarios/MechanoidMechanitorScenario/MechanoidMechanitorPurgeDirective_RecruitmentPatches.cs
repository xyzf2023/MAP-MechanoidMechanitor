using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// RecruitUtility.Recruit 底层保险：阻止把血肉智慧生物招募为玩家自由殖民者。
    /// 这一层静默阻止，不弹消息——正常 UI 拦截在更上层完成。
    /// </summary>
    [HarmonyPatch(typeof(RecruitUtility), nameof(RecruitUtility.Recruit))]
    public static class
        MechanoidMechanitorPurgeDirective_RecruitUtility_Recruit_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn pawn, Faction faction)
        {
            return !MechanoidMechanitorPurgeDirectivePopulationPolicy
                .WouldAddForbiddenFreeColonist(pawn, faction);
        }
    }

    /// <summary>
    /// InteractionWorker_RecruitAttempt.DoRecruit 保险：
    /// 即使有人绕过 UI 直接走 RecruitAttempt，也不要执行原版内的
    /// 招募成功信 / Tale / 招募统计 / RecruitedMe thought / RecruitUtility.Recruit。
    /// 这条底层保险同时覆盖 M03 倒地难民 / M04 囚犯救援 的永久加入路线：
    /// GenStep 设置 WillJoinColonyIfRescued=true →
    /// JobDriver_OfferHelp → Pawn_MindState.JoinColonyBecauseRescuedBy →
    /// InteractionWorker_RecruitAttempt.DoRecruit → RecruitUtility.Recruit。
    /// 因此 M03/M04 不依赖 QuestPart_PawnsArrive 白名单，最终在此处被拦截。
    /// </summary>
    [HarmonyPatch]
    public static class
        MechanoidMechanitorPurgeDirective_RecruitAttempt_DoRecruit_Patch
    {
        private static MethodBase? TargetMethod()
        {
            return AccessTools.Method(
                typeof(InteractionWorker_RecruitAttempt),
                nameof(InteractionWorker_RecruitAttempt.DoRecruit),
                new[]
                {
                    typeof(Pawn),
                    typeof(Pawn),
                    typeof(string).MakeByRefType(),
                    typeof(string).MakeByRefType(),
                    typeof(bool),
                    typeof(bool)
                });
        }

        [HarmonyPrefix]
        public static bool Prefix(
            Pawn recruiter,
            Pawn recruitee,
            ref string letterLabel,
            ref string letter)
        {
            if (!MechanoidMechanitorPurgeDirectivePopulationPolicy
                    .WouldAddForbiddenFreeColonist(
                        recruitee,
                        Faction.OfPlayerSilentFail))
            {
                return true;
            }

            letterLabel = string.Empty;
            letter = string.Empty;
            return false;
        }
    }
}
