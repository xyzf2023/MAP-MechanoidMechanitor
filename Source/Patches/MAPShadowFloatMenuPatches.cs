using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(FloatMenuOptionProvider_Mechanitor), nameof(FloatMenuOptionProvider_Mechanitor.GetOptionsFor))]
    public static class MAPShadowFloatMenuPatches
    {
        [HarmonyPostfix]
        public static void GetOptionsFor_Postfix(
            Pawn clickedPawn,
            FloatMenuContext context,
            ref IEnumerable<FloatMenuOption> __result)
        {
            __result = AppendShadowOptions(__result, clickedPawn, context);
        }

        private static IEnumerable<FloatMenuOption> AppendShadowOptions(
            IEnumerable<FloatMenuOption> original,
            Pawn clickedPawn,
            FloatMenuContext context)
        {
            foreach (FloatMenuOption option in original)
            {
                yield return option;
            }

            Pawn? controller = context.FirstSelectedPawn;
            if (controller == null
                || !ModsConfig.BiotechActive
                || !MAPMechanitorNodeUtility.IsShadowController(controller)
                || VanillaRelayMechanitorUtility.IsVanillaRelayMechanitor(controller)
                || !clickedPawn.IsColonyMech)
            {
                yield break;
            }

            bool takeover = MAPShadowOverseerUtility.IsTakeoverScenario(clickedPawn);
            string targetLabel = clickedPawn.LabelShort;

            if (MAPShadowOverseerUtility.CanAssignShadowOverseer(controller, clickedPawn, out string rejectReason))
            {
                string label = takeover
                    ? $"Shadow take over {targetLabel}"
                    : $"Shadow control {targetLabel}";

                yield return new FloatMenuOption(label, delegate
                {
                    if (MAPShadowOverseerUtility.TryAssignOrTakeOverShadowOverseer(
                            controller,
                            clickedPawn,
                            out string failureReason))
                    {
                        Messages.Message(
                            $"{controller.LabelShort} established shadow control over {clickedPawn.LabelShort}.",
                            controller,
                            MessageTypeDefOf.PositiveEvent);

                        if (Prefs.DevMode)
                        {
                            Log.Message(
                                $"[MAP-MechanoidMechanitor] Shadow overseer assigned/taken over: " +
                                $"{controller.LabelShort} -> {clickedPawn.LabelShort}");
                        }
                    }
                    else
                    {
                        Messages.Message(failureReason, controller, MessageTypeDefOf.RejectInput);
                    }
                });
            }
            else
            {
                string label = takeover
                    ? $"Cannot shadow take over {targetLabel}: {rejectReason}"
                    : $"Cannot shadow control {targetLabel}: {rejectReason}";

                yield return new FloatMenuOption(label, null);
            }
        }
    }
}
