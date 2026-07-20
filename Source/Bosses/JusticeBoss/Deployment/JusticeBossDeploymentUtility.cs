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

        public static void DeployInfrastructure(Pawn justice, IntVec3 anchorCell, out Lord? guardLord)
        {
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
                requireOutwardFacing: false,
                occupied);

            DeployBuildings(
                map,
                faction,
                anchorCell,
                ThingDefOf.Turret_AutoChargeBlaster,
                3,
                minRadius: 10f,
                maxRadius: 18f,
                requireOutwardFacing: true,
                occupied);

            DeployBuildings(
                map,
                faction,
                anchorCell,
                ThingDefOf.Turret_AutoInferno,
                3,
                minRadius: 10f,
                maxRadius: 18f,
                requireOutwardFacing: true,
                occupied);

            if (ModsConfig.RoyaltyActive)
            {
                ThingDef? mortarShield = ThingDefOf.ShieldGeneratorMortar;
                ThingDef? bulletShield = ThingDefOf.ShieldGeneratorBullets;
                DeployBuildings(
                    map,
                    faction,
                    anchorCell,
                    mortarShield,
                    1,
                    minRadius: 3f,
                    maxRadius: 7f,
                    requireOutwardFacing: false,
                    occupied);
                DeployBuildings(
                    map,
                    faction,
                    anchorCell,
                    bulletShield,
                    1,
                    minRadius: 3f,
                    maxRadius: 7f,
                    requireOutwardFacing: false,
                    occupied);
            }
            else
            {
                JusticeBossSpawnUtility.SpawnGuardsNear(map, faction, anchorCell, 5, out guardLord);
            }
        }

        public static void ActivateFactionBuildingsNear(Map map, Faction faction, IntVec3 anchor, float radius)
        {
            if (map == null || faction == null)
            {
                return;
            }

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(anchor, radius, useCenter: true))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing is Building && thing.Faction == faction)
                    {
                        ActivateDeployedThing(thing);
                    }
                }
            }
        }

        public static void ActivateDeployedThing(Thing thing)
        {
            if (thing == null || thing.Destroyed)
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
            bool requireOutwardFacing,
            List<IntVec3> occupied)
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
                        requireOutwardFacing,
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

                SpawnBuildingViaDropPod(map, faction, def, cell, rot);
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
            bool requireOutwardFacing,
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

                    Rot4 preferredRot = requireOutwardFacing
                        ? Rot4.FromAngleFlat((candidate - anchor).AngleFlat)
                        : Rot4.Random;
                    Rot4[] tryRots = requireOutwardFacing
                        ? new[] { preferredRot, preferredRot.Rotated(RotationDirection.Clockwise), preferredRot.Rotated(RotationDirection.Counterclockwise), Rot4.Random }
                        : Rot4.AllRotations.ToArray();

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

        private static void SpawnBuildingViaDropPod(
            Map map,
            Faction faction,
            ThingDef def,
            IntVec3 cell,
            Rot4 rot)
        {
            Thing building = ThingMaker.MakeThing(def);
            building.SetFaction(faction);
            if (building.def.rotatable)
            {
                building.Rotation = rot;
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
            info.spawnWipeMode = WipeMode.Vanish;
            DropPodUtility.MakeDropPodAt(cell, map, info, faction);
        }
    }
}