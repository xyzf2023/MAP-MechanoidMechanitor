using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor
{
    public static class JusticeBossDeploymentUtility
    {
        private const int MaxTriesPerBuilding = 200;

        private static bool loggedAnySkip;

        public static void DeployInfrastructure(
            Pawn justice,
            IntVec3 anchorCell,
            int justiceEventId,
            out List<Thing> deployedInfrastructure,
            out List<PawnKindDef> failedGuardKinds,
            out Lord? guardLord)
        {
            deployedInfrastructure = new List<Thing>();
            failedGuardKinds = new List<PawnKindDef>();
            guardLord = null;
            Map? map = justice?.Map;
            Faction? faction = justice?.Faction;
            if (map == null || faction == null || !anchorCell.IsValid)
            {
                return;
            }

            loggedAnySkip = false;
            List<IntVec3> occupied = new List<IntVec3>();

            ThingDef? autoMortar = DefDatabase<ThingDef>.GetNamedSilentFail("Turret_AutoMortar");
            DeployBuildings(
                map,
                faction,
                anchorCell,
                autoMortar,
                3,
                minRadius: 7f,
                maxRadius: 13f,
                fixedRotation: Rot4.South,
                occupied,
                deployedInfrastructure);

            DeployBuildings(
                map,
                faction,
                anchorCell,
                ThingDefOf.Turret_AutoChargeBlaster,
                3,
                minRadius: 10f,
                maxRadius: 18f,
                fixedRotation: Rot4.South,
                occupied,
                deployedInfrastructure);

            DeployBuildings(
                map,
                faction,
                anchorCell,
                ThingDefOf.Turret_AutoInferno,
                3,
                minRadius: 10f,
                maxRadius: 18f,
                fixedRotation: Rot4.South,
                occupied,
                deployedInfrastructure);

            if (ModsConfig.RoyaltyActive)
            {
                DeployBuildings(
                    map,
                    faction,
                    anchorCell,
                    ThingDefOf.ShieldGeneratorMortar,
                    1,
                    minRadius: 3f,
                    maxRadius: 7f,
                    fixedRotation: null,
                    occupied,
                    deployedInfrastructure);
                DeployBuildings(
                    map,
                    faction,
                    anchorCell,
                    ThingDefOf.ShieldGeneratorBullets,
                    1,
                    minRadius: 3f,
                    maxRadius: 7f,
                    fixedRotation: null,
                    occupied,
                    deployedInfrastructure);
            }
            else
            {
                JusticeBossDropLaunchResult guardResult =
                    JusticeBossSpawnUtility.LaunchGuardDropPodsNear(
                        map,
                        faction,
                        anchorCell,
                        justiceEventId,
                        5,
                        out guardLord);
                failedGuardKinds.AddRange(guardResult.FailedKinds);
            }
        }

        public static void ActivateDeployedThing(Thing thing)
        {
            if (thing == null || thing.Destroyed || !thing.Spawned)
            {
                return;
            }

            CompInitiatable? initiatable = thing.TryGetComp<CompInitiatable>();
            if (initiatable != null)
            {
                initiatable.initiationDelayTicksOverride = 1;
            }

            thing.TryGetComp<CompCanBeDormant>()?.WakeUp();
            thing.TryGetComp<CompWakeUpDormant>()?.Activate(null, sendSignal: true, silent: true);
        }

        private static void DeployBuildings(
            Map map,
            Faction faction,
            IntVec3 anchor,
            ThingDef? def,
            int count,
            float minRadius,
            float maxRadius,
            Rot4? fixedRotation,
            List<IntVec3> occupied,
            List<Thing> deployedInfrastructure)
        {
            if (def == null)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                if (!TryFindPlacement(
                        map,
                        anchor,
                        def,
                        minRadius,
                        maxRadius,
                        fixedRotation,
                        occupied,
                        out IntVec3 cell,
                        out Rot4 rot))
                {
                    if (!loggedAnySkip)
                    {
                        loggedAnySkip = true;
                        Log.Warning(
                            "[MAP JusticeBoss] Skipped deploying "
                            + def.defName
                            + " (no legal cell).");
                    }

                    continue;
                }

                Thing? building = SpawnBuildingViaDropPod(map, faction, def, cell, rot);
                if (building != null)
                {
                    deployedInfrastructure.Add(building);
                }

                foreach (IntVec3 c in GenAdj.OccupiedRect(cell, rot, def.size))
                {
                    occupied.Add(c);
                }
            }
        }

        private static bool TryFindPlacement(
            Map map,
            IntVec3 anchor,
            ThingDef def,
            float preferredMin,
            float preferredMax,
            Rot4? fixedRotation,
            List<IntVec3> occupied,
            out IntVec3 cell,
            out Rot4 rot)
        {
            cell = IntVec3.Invalid;
            rot = Rot4.North;
            FloatRange[] rings =
            {
                new FloatRange(preferredMin, preferredMax),
                new FloatRange(4f, 18f),
                new FloatRange(18f, 26f),
                new FloatRange(26f, 34f),
            };

            Rot4[] tryRots = fixedRotation.HasValue
                ? new[] { fixedRotation.Value }
                : Rot4.AllRotations.ToArray();

            foreach (FloatRange ring in rings)
            {
                for (int attempt = 0; attempt < MaxTriesPerBuilding; attempt++)
                {
                    float dist = Rand.Range(ring.min, ring.max);
                    float angle = Rand.Range(0f, 360f);
                    Vector3 offset = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward * dist;
                    IntVec3 candidate = anchor + IntVec3.FromVector3(offset);
                    if (!candidate.InBounds(map))
                    {
                        continue;
                    }

                    for (int r = 0; r < tryRots.Length; r++)
                    {
                        Rot4 tryRot = tryRots[r];
                        if (!IsValidBuildingCell(map, def, candidate, tryRot, occupied))
                        {
                            continue;
                        }

                        cell = candidate;
                        rot = tryRot;
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsValidBuildingCell(
            Map map,
            ThingDef def,
            IntVec3 cell,
            Rot4 rot,
            List<IntVec3> occupied)
        {
            CellRect rect = GenAdj.OccupiedRect(cell, rot, def.size);
            if (!rect.InBounds(map))
            {
                return false;
            }

            TerrainAffordanceDef? need = def.GetTerrainAffordanceNeed();
            foreach (IntVec3 c in rect)
            {
                if (c.GetRoof(map) == RoofDefOf.RoofRockThick)
                {
                    return false;
                }

                if (need != null && !c.GetAffordances(map).Contains(need))
                {
                    return false;
                }

                if (c.GetEdifice(map) != null || c.GetFirstPawn(map) != null)
                {
                    return false;
                }

                if (occupied.Contains(c))
                {
                    return false;
                }

                List<Thing> things = c.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing t = things[i];
                    if (t.def.preventSkyfallersLandingOn)
                    {
                        return false;
                    }

                    if (t.Faction == Faction.OfPlayer
                        && t.def.category == ThingCategory.Building)
                    {
                        return false;
                    }
                }
            }

            if (def.building != null && def.building.minDistanceToSameTypeOfBuilding > 0f)
            {
                float minDist = def.building.minDistanceToSameTypeOfBuilding;
                List<Building> buildings = map.listerBuildings.allBuildingsNonColonist;
                for (int i = 0; i < buildings.Count; i++)
                {
                    Building b = buildings[i];
                    if (b.def == def && cell.InHorDistOf(b.Position, minDist))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static Thing? SpawnBuildingViaDropPod(
            Map map,
            Faction faction,
            ThingDef def,
            IntVec3 cell,
            Rot4 rot)
        {
            // 对齐原版 SketchThing.TransportPod：spawnWipeMode=null + moveItemsAside。
            Thing building = ThingMaker.MakeThing(def);
            building.Position = cell;
            building.Rotation = rot;
            if (faction != null && building.def.CanHaveFaction)
            {
                building.SetFactionDirect(faction);
            }

            CompInitiatable? initiatable = building.TryGetComp<CompInitiatable>();
            if (initiatable != null)
            {
                initiatable.initiationDelayTicksOverride = 1;
            }

            ActiveTransporterInfo info = new ActiveTransporterInfo();
            info.innerContainer.TryAdd(building, 1);
            info.openDelay = 60;
            info.leaveSlag = false;
            info.despawnPodBeforeSpawningThing = true;
            info.spawnWipeMode = null;
            info.moveItemsAsideBeforeSpawning = true;
            info.setRotation = rot;
            DropPodUtility.MakeDropPodAt(cell, map, info, faction);
            return building;
        }
    }
}
