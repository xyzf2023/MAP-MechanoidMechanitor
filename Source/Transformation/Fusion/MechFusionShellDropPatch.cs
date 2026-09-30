using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 原版掉落会在服装移出穿戴容器后才通知组件；解除若延期，外甲就会落地。
    /// 在放置前统一解除；未能销毁外甲时取消原版地图放置。
    /// </summary>
    [HarmonyPatch]
    internal static class MechFusionShellDropPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(Pawn_ApparelTracker),
                nameof(Pawn_ApparelTracker.TryDrop),
                new[]
                {
                    typeof(Apparel),
                    typeof(Apparel).MakeByRefType(),
                    typeof(IntVec3),
                    typeof(bool)
                });
        }

        private static bool Prefix(
            Pawn_ApparelTracker __instance,
            Apparel ap,
            ref Apparel resultingAp,
            ref bool __result)
        {
            if (ap == null)
            {
                return true;
            }

            MechFusionSession? session = ap.TryGetComp<CompMechFusionShell>()
                ?.GetBoundSession();
            if (session == null
                || session.TeardownCompleted
                || !ReferenceEquals(session.WearerPawn, __instance.pawn))
            {
                return true;
            }

            MechFusionTeardownService.TryTeardown(
                session,
                MechFusionExitReason.ApparelLost,
                force: false);

            resultingAp = null!;
            __result = ap.Destroyed;
            return false;
        }
    }
}
