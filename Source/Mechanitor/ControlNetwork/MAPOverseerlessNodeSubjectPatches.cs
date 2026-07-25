using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// CompOverseerSubject.State 的安全接管。
    /// 使用 Prefix 直接返回结果，完全跳过原版 getter，
    /// 因此原版 getter 中 “Overseer.mechanitor.ControlledPawns” 的潜在空引用
    /// 永远不会发生（Postfix 无法阻止原版 getter 先抛异常）。
    /// </summary>
    [HarmonyPatch(typeof(CompOverseerSubject), nameof(CompOverseerSubject.State), MethodType.Getter)]
    public static class MAPOverseerlessNodeSubjectPatches_State
    {
        [HarmonyPrefix]
        public static bool Prefix(
            CompOverseerSubject __instance,
            ref OverseerSubjectState __result)
        {
            Pawn? subject = __instance?.Parent;

            if (subject == null || !ModsConfig.BiotechActive)
            {
                return true;
            }

            // 只接管采用原版控制后端的 MAP 机械师节点。
            // 普通原版机械体、其他 MOD 普通机械体继续执行原版 getter。
            if (!MAPMechanitorNodeUtility.HasNode(subject)
                || !MAPMechanitorNodeUtility.UsesVanillaControlPath(subject))
            {
                return true;
            }

            // 无需外部监管者的节点，例如正义或后天机械族机械师，始终视为受控。
            if (!MAPMechanitorNodeUtility.RequiresExternalOverseer(subject))
            {
                __result = OverseerSubjectState.Overseen;
                return false;
            }

            // 需要外部监管者的节点，例如隐者。实际监管者只解析一次。
            Pawn? overseer = MAPOverseerRelationDirectionUtility.FindActualOverseer(subject);
            if (overseer == null || overseer.Destroyed || overseer.Dead)
            {
                __result = OverseerSubjectState.RequiresOverseer;
                return false;
            }

            Pawn_MechanitorTracker? tracker = overseer.mechanitor;
            if (tracker == null)
            {
                __result = OverseerSubjectState.RequiresOverseer;
                return false;
            }

            List<Pawn>? controlledPawns = tracker.ControlledPawns;
            if (controlledPawns != null && controlledPawns.Contains(subject))
            {
                __result = OverseerSubjectState.Overseen;
            }
            else
            {
                __result = OverseerSubjectState.RequiresBandwidth;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(CompOverseerSubject), nameof(CompOverseerSubject.CompInspectStringExtra))]
    public static class MAPOverseerlessNodeSubjectPatches_InspectString
    {
        [HarmonyPostfix]
        public static void Postfix(CompOverseerSubject __instance, ref string? __result)
        {
            Pawn? subject = __instance?.Parent;
            if (subject == null || subject.Faction != Faction.OfPlayer)
            {
                return;
            }

            // 无需外部监管者（如正义）的节点不显示“需要监管者”提示，
            // 与 State 的 Overseen 判定保持一致。
            if (MAPOverseerlessNodeUtility.IsOverseerlessNodeSubject(subject))
            {
                __result = null;
            }
        }
    }
}
