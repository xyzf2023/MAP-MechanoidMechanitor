using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体强制退出边界。人类死亡或倒地时统一进入解除流程；
    /// 飞行中由统一解除流程先完成着陆或坠毁，再恢复真实源机械族。
    /// </summary>
    [HarmonyPatch(
        typeof(Pawn),
        nameof(Pawn.Kill),
        new[] { typeof(DamageInfo?), typeof(Hediff) })]
    internal static class MechFusionWearerKillPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance)
        {
            if (!GameComponent_MechFusionSessionRegistry.TryGetSessionForWearer(
                    __instance,
                    out MechFusionSession? session)
                || session == null
                || !session.IsActive)
            {
                return;
            }

            Corpse? corpse = __instance.Corpse;
            if (corpse != null)
            {
                session.UpdateRecoveryLocation(
                    corpse.Spawned ? corpse.Map : session.PendingMap,
                    corpse.Spawned ? corpse.Position : session.PendingPosition,
                    corpse.Rotation);
            }

            MechFusionTeardownService.TryTeardown(
                session,
                MechFusionExitReason.HumanDeathOrDowned,
                force: false);
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), "MakeDowned")]
    internal static class MechFusionWearerDownedPatch
    {
        private static readonly AccessTools.FieldRef<Pawn_HealthTracker, Pawn>
            PawnField = AccessTools.FieldRefAccess<Pawn_HealthTracker, Pawn>(
                "pawn");

        [HarmonyPostfix]
        public static void Postfix(Pawn_HealthTracker __instance)
        {
            Pawn pawn = PawnField(__instance);
            if (!GameComponent_MechFusionSessionRegistry.TryGetSessionForWearer(
                    pawn,
                    out MechFusionSession? session)
                || session == null
                || !session.IsActive)
            {
                return;
            }

            MechFusionTeardownService.TryTeardown(
                session,
                MechFusionExitReason.HumanDeathOrDowned,
                force: false);
        }
    }
}
