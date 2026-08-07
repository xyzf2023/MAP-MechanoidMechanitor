using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_RoyalPermits_Mechanitor
    {
        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(
            IEnumerable<Gizmo> __result,
            Pawn __instance)
        {
            foreach (Gizmo gizmo in __result)
            {
                yield return gizmo;
            }

            if (!MechanoidMechanitorRoyaltyUtility
                    .IsRoyaltyEligibleMechanitor(__instance)
                || __instance.royalty == null
                || !__instance.Spawned
                || __instance.MentalStateDef != null)
            {
                yield break;
            }

            bool anyPermitOnCooldown = false;
            List<FactionPermit> permits =
                __instance.royalty.AllFactionPermits;
            for (int i = 0; i < permits.Count; i++)
            {
                FactionPermit factionPermit = permits[i];
                if (factionPermit.OnCooldown)
                {
                    anyPermitOnCooldown = true;
                }

                IEnumerable<Gizmo>? permitGizmos =
                    factionPermit.Permit.Worker.GetPawnGizmos(
                        __instance,
                        factionPermit.Faction);
                if (permitGizmos == null)
                {
                    continue;
                }

                foreach (Gizmo gizmo in permitGizmos)
                {
                    yield return gizmo;
                }
            }

            if (__instance.royalty.HasAidPermit)
            {
                yield return __instance.royalty.RoyalAidGizmo();
            }

            if (DebugSettings.ShowDevGizmos
                && anyPermitOnCooldown)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Reset permit cooldowns",
                    action = delegate
                    {
                        for (int i = 0; i < permits.Count; i++)
                        {
                            permits[i].ResetCooldown();
                        }
                    }
                };
            }
        }
    }
}
