using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>太阳设施的独立派系入口；创建设施时生成派系，读档后修复缺失或错误的设施归属。</summary>
    internal static class SunBossFactionUtility
    {
        private const string FactionDefName = "MAP_AncientMechHive";
        private const string SiteDefName = "MAP_SunBossFacility";

        internal static Faction CreateFacilityFaction()
        {
            List<FactionRelation> relations = new List<FactionRelation>();
            foreach (Faction other in Find.FactionManager.AllFactionsListForReading)
                relations.Add(new FactionRelation
                {
                    other = other,
                    kind = FactionRelationKind.Hostile,
                    baseGoodwill = -100
                });

            Faction faction = FactionGenerator.NewGeneratedFactionWithRelations(
                new FactionGeneratorParms(DefDatabase<FactionDef>.GetNamed(FactionDefName),
                    default(IdeoGenerationParms), hidden: true), relations);
            faction.temporary = true;
            Find.FactionManager.Add(faction);
            return faction;
        }

        internal static Faction GetFacilityFaction(Map map)
        {
            // 设施世界对象保存归属；初次进入和撤退后重新生成地图均只读取，不另建派系。
            Faction? faction = map.Parent.Faction;
            if (map.Parent.def.defName != SiteDefName || faction?.def.defName != FactionDefName)
                throw new InvalidOperationException("[MAP-机械族机械师] 太阳设施缺少对应的远古机械巢派系。");
            return faction;
        }

        internal static void RepairFacilityFactionsAfterLoad()
        {
            if (!ModsConfig.OdysseyActive || Find.World == null || Find.FactionManager == null) return;
            List<Site> facilities = Find.WorldObjects.AllWorldObjects.OfType<Site>()
                .Where(site => !site.Destroyed && site.def.defName == SiteDefName).ToList();
            // 无设施时不补造临时派系；从快照遍历，避免创建派系时影响枚举。
            if (facilities.Count == 0) return;
            List<Faction> candidates = Find.FactionManager.AllFactionsListForReading
                .Where(faction => faction.def?.defName == FactionDefName).ToList();
            HashSet<Faction> reserved = new HashSet<Faction>(Find.WorldObjects.AllWorldObjects
                .Select(worldObject => worldObject.Faction).OfType<Faction>()
                .Where(candidates.Contains));
            // 地图保存的有效引用也先预留，防止多个设施修复时抢占另一设施的派系。
            foreach (Site site in facilities)
            {
                Faction? mapFaction = site.Map?.GetComponent<MapComponent_SunBossArena>().FacilityFaction;
                if (mapFaction != null && candidates.Contains(mapFaction)) reserved.Add(mapFaction);
            }

            foreach (Site site in facilities)
            {
                Faction? faction = site.Faction;
                if (faction == null || !candidates.Contains(faction))
                {
                    Faction? mapFaction = site.Map?.GetComponent<MapComponent_SunBossArena>().FacilityFaction;
                    // 优先恢复本设施地图已保存的派系；已属于其他世界对象的派系不得挪用。
                    if (mapFaction != null && candidates.Contains(mapFaction)
                        && !Find.WorldObjects.AllWorldObjects.Any(other => other != site && other.Faction == mapFaction))
                        faction = mapFaction;
                    else
                        faction = candidates.FirstOrDefault(candidate => !reserved.Contains(candidate));
                    if (faction == null)
                    {
                        faction = CreateFacilityFaction();
                        candidates.Add(faction);
                    }
                }
                reserved.Add(faction);
                faction.hidden = true;
                faction.temporary = true;
                if (site.Faction != faction) site.SetFaction(faction);
                // 只同步地图的归属引用；不重新生成地图、触发苏醒或发送抵达信件。
                site.Map?.GetComponent<MapComponent_SunBossArena>().BindFacilityFaction();
            }
        }

        // 原版培养舱在生成 Pawn、查找 Lord 和创建 Lord 时共用此查询。
        // 普通地图及未采用新归属的既有地图沿用原版机械主巢。
        internal static Faction? GetMechFactionForMap(Map map)
            => map.GetComponent<MapComponent_SunBossArena>().CombatFaction;
    }
}
