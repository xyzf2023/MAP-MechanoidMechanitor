using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Discard))]
    internal static class MechTransformationPawnDiscardPatch
    {
        private static void Postfix(Pawn __instance)
        {
            // 原版可能拒绝丢弃仍在 WorldPawns 中的 Pawn；只清理实际完成的永久丢弃。
            GameComponent_MechTransformationRegistry.NotifyPawnDiscarded(__instance);
        }
    }
}
