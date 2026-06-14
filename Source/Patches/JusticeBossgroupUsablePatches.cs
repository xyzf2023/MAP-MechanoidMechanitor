using System.Collections.Generic;
using HarmonyLib;
using MAP_MechanoidMechanitor;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    // Vanilla CompUsable only allows flesh pawns. MAP mechanitor nodes (e.g. Justice) are mechanoids
    // but must still use Bossgroup caller buildings (CommsConsole, etc.). This patch skips only that
    // generic flesh gate for Bossgroup callers; power, path, reservation, CompUseEffect_CallBossgroup,
    // and BossgroupWorker.CanResolve checks all remain vanilla.
    [HarmonyPatch(typeof(CompUsable), nameof(CompUsable.CanBeUsedBy))]
    public static class Patch_CompUsable_CanBeUsedBy_JusticeBossgroup
    {
        [HarmonyPrefix]
        public static bool Prefix(
            CompUsable __instance,
            Pawn p,
            bool forced,
            bool ignoreReserveAndReachable,
            ref AcceptanceReport __result)
        {
            if (!ShouldHandle(__instance, p))
            {
                return true;
            }

            __result = CanJusticeUseBossgroupCaller(__instance, p, forced, ignoreReserveAndReachable);
            return false;
        }

        private static bool ShouldHandle(CompUsable usable, Pawn? p)
        {
            if (p == null || usable?.parent == null)
            {
                return false;
            }

            if (!ModsConfig.BiotechActive)
            {
                return false;
            }

            if (p.Faction == null || !p.Faction.IsPlayerSafe())
            {
                return false;
            }

            if (!MAPMechanitorNodeUtility.HasNode(p)
                || MAPMechanitorNodeUtility.RequiresExternalOverseer(p))
            {
                return false;
            }

            return HasBossgroupUseEffect(usable.parent);
        }

        private static bool HasBossgroupUseEffect(Thing parent)
        {
            if (parent is not ThingWithComps parentWithComps)
            {
                return false;
            }

            foreach (CompUseEffect comp in parentWithComps.GetComps<CompUseEffect>())
            {
                if (comp is CompUseEffect_CallBossgroup)
                {
                    return true;
                }
            }

            return false;
        }

        private static AcceptanceReport CanJusticeUseBossgroupCaller(
            CompUsable usable,
            Pawn p,
            bool forced,
            bool ignoreReserveAndReachable)
        {
            Thing parent = usable.parent;
            CompProperties_Usable props = usable.Props;

            PlanetTile tile = p.MapHeld.Tile;
            if (tile.Valid && !props.layerWhitelist.NullOrEmpty() && !props.layerWhitelist.Contains(tile.LayerDef))
            {
                return "CannotPerformPlanetLayer".Translate(
                    tile.LayerDef.gerundLabel.Named("GERUND"),
                    tile.LayerDef.label.Named("LAYER")).Resolve();
            }

            if (tile.Valid && !props.layerBlacklist.NullOrEmpty() && props.layerBlacklist.Contains(tile.LayerDef))
            {
                return "CannotPerformPlanetLayer".Translate(
                    tile.LayerDef.gerundLabel.Named("GERUND"),
                    tile.LayerDef.label.Named("LAYER")).Resolve();
            }

            if (parent.TryGetComp<CompPowerTrader>(out CompPowerTrader powerTrader) && !powerTrader.PowerOn)
            {
                return "NoPower".Translate();
            }

            if (!ignoreReserveAndReachable && !p.CanReach(parent, PathEndMode.Touch, Danger.Deadly))
            {
                return "NoPath".Translate();
            }

            if (!ignoreReserveAndReachable && !p.CanReserve(parent, 1, -1, null, forced))
            {
                Pawn reserver = p.Map.reservationManager.FirstRespectedReserver(parent, p)
                    ?? p.Map.physicalInteractionReservationManager.FirstReserverOf(parent);
                if (reserver != null)
                {
                    return "ReservedBy".Translate(reserver.LabelShort, reserver);
                }

                return "Reserved".Translate();
            }

            if (props.userMustHaveHediff != null && !p.health.hediffSet.HasHediff(props.userMustHaveHediff))
            {
                return "MustHaveHediff".Translate(props.userMustHaveHediff);
            }

            if (parent is not ThingWithComps parentWithComps)
            {
                return false;
            }

            List<ThingComp> allComps = parentWithComps.AllComps;
            for (int i = 0; i < allComps.Count; i++)
            {
                if (allComps[i] is CompUseEffect compUseEffect)
                {
                    AcceptanceReport result = compUseEffect.CanBeUsedBy(p);
                    if (!result.Accepted)
                    {
                        return result;
                    }
                }
            }

            return true;
        }
    }
}
