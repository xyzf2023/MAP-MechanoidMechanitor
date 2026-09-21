using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 仅接管原版标准 GetOverseer 入口，不再补丁全游戏通用的
    /// Pawn_RelationsTracker.GetFirstDirectRelationPawn。
    /// </summary>
    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.GetOverseer))]
    public static class Patch_MechanitorUtility_GetOverseer_MAPNodeDirection
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn pawn, ref Pawn? __result)
        {
            if (!AutonomousMechUtility.TryGetSubjectProfile(
                    pawn,
                    out bool requiresExternalOverseer))
            {
                return true;
            }

            __result = requiresExternalOverseer
                ? MAPOverseerRelationDirectionUtility.FindActualOverseer(pawn)
                : null;
            return false;
        }
    }

    /// <summary>
    /// 原版防守监管者 AI 绕过 GetOverseer，直接读取 reflexive 关系。
    /// 只对 MAP 原版控制节点改用实际控制组方向。
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_AIDefendOverseer), "GetDefendee")]
    public static class Patch_JobGiver_AIDefendOverseer_GetDefendee_MAPDirection
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn pawn, ref Pawn? __result)
        {
            if (!AutonomousMechUtility.TryGetSubjectProfile(
                    pawn,
                    out bool requiresExternalOverseer))
            {
                return true;
            }

            __result = requiresExternalOverseer
                ? MAPOverseerRelationDirectionUtility.FindActualOverseer(pawn)
                : null;
            return false;
        }
    }

    /// <summary>
    /// 原版工作模式标签节点直接读取第一条 Overseer 关系。
    /// 对 MAP 节点从实际监管者控制组读取标签，并在任一环节缺失时安全返回 false。
    /// </summary>
    [HarmonyPatch(typeof(ThinkNode_ConditionalWorkModeTag), "Satisfied")]
    public static class Patch_ThinkNode_ConditionalWorkModeTag_Satisfied_MAPDirection
    {
        [HarmonyPrefix]
        public static bool Prefix(
            ThinkNode_ConditionalWorkModeTag __instance,
            Pawn pawn,
            ref bool __result)
        {
            if (!AutonomousMechUtility.TryGetSubjectProfile(
                    pawn,
                    out bool requiresExternalOverseer))
            {
                return true;
            }

            if (!pawn.RaceProps.IsMechanoid
                || pawn.Faction != Faction.OfPlayer
                || pawn.relations == null
                || !requiresExternalOverseer)
            {
                __result = false;
                return false;
            }

            Pawn? overseer =
                MAPOverseerRelationDirectionUtility.FindActualOverseer(pawn);
            MechanitorControlGroup? group = overseer?.mechanitor?.GetControlGroup(pawn);
            __result = group != null && group.GetTag(pawn) == __instance.tag;
            return false;
        }
    }

    /// <summary>
    /// 原版控制组目标漫游同样绕过 GetOverseer。
    /// 无实际监管者或控制组时返回无效目标，使原版 TryGiveJob 安全返回 null。
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_WanderControlGroupTarget), "Target")]
    public static class Patch_JobGiver_WanderControlGroupTarget_Target_MAPDirection
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn pawn, ref GlobalTargetInfo __result)
        {
            if (!AutonomousMechUtility.TryGetSubjectProfile(
                    pawn,
                    out bool requiresExternalOverseer))
            {
                return true;
            }

            Pawn? overseer = requiresExternalOverseer
                ? MAPOverseerRelationDirectionUtility.FindActualOverseer(pawn)
                : null;
            MechanitorControlGroup? group = overseer?.mechanitor?.GetControlGroup(pawn);
            __result = group?.Target ?? GlobalTargetInfo.Invalid;
            return false;
        }
    }
}
