using HarmonyLib;
using MAP_MechanoidMechanitor;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(Pawn_DraftController), nameof(Pawn_DraftController.ShowDraftGizmo), MethodType.Getter)]
    public static class DraftingPatches_ShowDraftGizmo
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_DraftController __instance, ref bool __result)
        {
            Pawn pawn = __instance.pawn;
            if (pawn == null || !ModsConfig.BiotechActive)
            {
                return;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                return;
            }

            if (!MAPOverseerlessNodeUtility.IsOverseerlessNodeSubject(pawn))
            {
                return;
            }

            __result = true;
        }
    }

    /// <summary>
    /// 监听原版 <see cref="Pawn_DraftController.Drafted"/> Setter，
    /// 作为征召状态变化的唯一入口。仅当征召状态确实发生变化时，
    /// 才把发生变化的 Pawn 交给工作模式工具进行单 Pawn 同步。
    /// </summary>
    [HarmonyPatch(typeof(Pawn_DraftController), nameof(Pawn_DraftController.Drafted), MethodType.Setter)]
    public static class DraftingPatches_DraftedSetter
    {
        [HarmonyPrefix]
        public static void Prefix(Pawn_DraftController __instance, ref bool value,
            out bool __state)
        {
            // 记录修改前的征召状态。
            __state = __instance.Drafted;
            if (value && MechanicalFlightEmergencyUtility.IsEmergencySequence(__instance.pawn))
            {
                value = false;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(Pawn_DraftController __instance, bool __state)
        {
            // 仅在征召状态确实变化时才同步，避免重复赋值触发无意义处理。
            if (__instance.Drafted == __state)
            {
                return;
            }

            MechanoidMechanitorSelfWorkModeUtility.NotifyDraftedStateChanged(__instance.pawn);
        }
    }

    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.CanDraftMech))]
    public static class DraftingPatches_CanDraftMech
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn mech, ref AcceptanceReport __result)
        {
            if (mech == null || !ModsConfig.BiotechActive)
            {
                return true;
            }

            if (MechanicalFlightEmergencyUtility.IsEmergencySequence(mech))
            {
                __result = "MAP_MechanicalFlight_EmergencyLandingBlocked".Translate();
                return false;
            }

            if (mech.Faction == null || !mech.Faction.IsPlayerSafe())
            {
                return true;
            }

            if (!MAPOverseerlessNodeUtility.IsOverseerlessNodeSubject(mech))
            {
                return true;
            }

            if (mech.needs?.energy != null && mech.needs.energy.IsLowEnergySelfShutdown)
            {
                __result = "IsLowEnergySelfShutdown".Translate(mech.Named("PAWN"));
                return false;
            }

            __result = true;
            return false;
        }
    }
}
