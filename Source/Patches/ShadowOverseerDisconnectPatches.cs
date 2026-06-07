using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace MMT
{
    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.ForceDisconnectMechFromOverseer))]
    public static class ShadowOverseerDisconnectPatches
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn mech)
        {
            if (mech == null || OverseerlessMechanitorUtility.IsNode(mech))
            {
                return true;
            }

            MMT_ShadowOverseerManager? manager = MMT_ShadowOverseerManager.Current;
            Pawn? shadowOverseer = manager?.GetShadowOverseer(mech);
            if (shadowOverseer == null)
            {
                return true;
            }

            manager!.RemoveShadowOverseer(mech);
            mech.OverseerSubject?.Notify_DisconnectedFromOverseer();
            SoundDefOf.DisconnectedMech.PlayOneShot(new TargetInfo(shadowOverseer));
            Messages.Message(
                "MessageMechanitorDisconnectedFromMech".Translate(shadowOverseer, mech),
                new LookTargets(mech, shadowOverseer),
                MessageTypeDefOf.NeutralEvent);

            if (Prefs.DevMode)
            {
                Log.Message(
                    $"[MMT] Shadow overseer disconnected: controller={shadowOverseer.LabelShort}, " +
                    $"subject={mech.LabelShort}");
            }

            return false;
        }
    }
}
