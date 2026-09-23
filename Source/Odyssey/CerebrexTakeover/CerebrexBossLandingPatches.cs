using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    internal static class CerebrexBossLandingUtility
    {
        public static bool IsBossSite(PlanetTile tile)
        {
            return Find.World?.worldObjects.MapParentAt(tile) is Site site
                && site.MainSitePartDef == SitePartDefOf.OrbitalMechhive;
        }

        public static bool IsBossSite(Map map)
        {
            return map?.Parent is Site site
                && site.MainSitePartDef == SitePartDefOf.OrbitalMechhive;
        }

        public static void TurnMechHiveHostile()
        {
            Faction? player = Faction.OfPlayerSilentFail;
            Faction? mechHive = Faction.OfMechanoids;
            if (player == null || mechHive == null)
            {
                return;
            }

            GameComponent_MechanoidMechanitorStoryState? storyState =
                Current.Game?.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            storyState?.MarkCerebrexFacilityTrespassed();
            if (mechHive.HostileTo(player))
            {
                return;
            }

            // 借用现有双向关系写入入口；存档标记使剧本关系锁以后仍保持敌对。
            if (storyState != null)
            {
                if (MechanoidMechanitorMechHiveRelationApplier.ApplyExactMechHiveRelation(
                        mechHive,
                        FactionRelationKind.Hostile,
                        hostileOnHarmByPlayer: false,
                        allowUnconfiguredMechHive: true))
                {
                    return;
                }

                Log.Error("[MAP-机械族机械师] 登陆主脑轨道设施后无法将机械巢设为敌对。");
                return;
            }

            // 普通剧本没有本 MOD 的关系配置，沿用原版攻击据点的好感结算。
            player.TryAffectGoodwillWith(
                mechHive,
                player.GoodwillToMakeHostile(mechHive),
                canSendMessage: false,
                canSendHostilityLetter: true,
                HistoryEventDefOf.AttackedSettlement);
        }
    }

    [HarmonyPatch]
    public static class CerebrexBossLandingConfirmationPatch
    {
        public static bool Prepare() => ModsConfig.OdysseyActive;

        public static MethodBase? TargetMethod()
        {
            return AccessTools.Method(
                typeof(SettlementProximityGoodwillUtility),
                "GetConfirmationDescriptions",
                new[] { typeof(PlanetTile), typeof(Building_GravEngine) });
        }

        [HarmonyPostfix]
        public static void Postfix(
            PlanetTile tile,
            Building_GravEngine gravEngine,
            ref IEnumerable<TaggedString> __result)
        {
            Faction? player = Faction.OfPlayerSilentFail;
            Faction? mechHive = Faction.OfMechanoids;
            if (gravEngine == null
                || player == null
                || mechHive == null
                || mechHive.HostileTo(player)
                || !CerebrexBossLandingUtility.IsBossSite(tile))
            {
                return;
            }

            __result = AppendWarning(__result);
        }

        private static IEnumerable<TaggedString> AppendWarning(
            IEnumerable<TaggedString> original)
        {
            foreach (TaggedString description in original)
            {
                yield return description;
            }

            yield return "MAP_CerebrexBoss.Landing.Warning".Translate();
        }
    }

    [HarmonyPatch(typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.InitiateLanding))]
    public static class CerebrexBossLandingHostilityPatch
    {
        public static bool Prepare() => ModsConfig.OdysseyActive;

        [HarmonyPrefix]
        public static void Prefix(Map map)
        {
            if (CerebrexBossLandingUtility.IsBossSite(map))
            {
                CerebrexBossLandingUtility.TurnMechHiveHostile();
            }
        }
    }
}
