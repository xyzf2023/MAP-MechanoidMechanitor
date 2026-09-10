using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class MechanicalFlightRoofUtility
    {
        public static bool HasThickRoof(IntVec3 cell, Map? map)
        {
            return map != null && cell.InBounds(map)
                && cell.GetRoof(map)?.isThickRoof == true;
        }

        public static void BreakThinRoofArea(Pawn pawn, MechanicalFlightProfileDef profile)
        {
            Map? map = pawn?.Map;
            if (map == null)
            {
                return;
            }

            int radius = System.Math.Max(0, profile.roofBreakRadius);
            foreach (IntVec3 cell in CellRect.CenteredOn(pawn.Position, radius).ClipInsideMap(map))
            {
                BreakRoofAt(cell, map, pawn, profile);
            }
            map.GetComponent<MechanicalFlightRoofLightingRefresh>().Request(pawn.Position);
        }

        private static void BreakRoofAt(
            IntVec3 cell,
            Map map,
            Pawn pawn,
            MechanicalFlightProfileDef profile)
        {
            RoofDef? roof = map.roofGrid.RoofAt(cell);
            if (roof == null || roof.isThickRoof)
            {
                return;
            }

            if (profile.clearBuildRoofArea && map.areaManager?.BuildRoof != null)
            {
                map.areaManager.BuildRoof[cell] = false;
            }
            if (profile.markNoRoofArea && map.areaManager?.NoRoof != null)
            {
                map.areaManager.NoRoof[cell] = true;
            }

            Pawn? previous = MechanicalFlightRoofPunchProtection.ProtectedPawn;
            MechanicalFlightRoofPunchProtection.ProtectedPawn = pawn;
            try
            {
                roof.soundPunchThrough?.PlayOneShot(new TargetInfo(cell, map));
                RoofCollapserImmediate.DropRoofInCells(new List<IntVec3> { cell }, map);
            }
            finally
            {
                MechanicalFlightRoofPunchProtection.ProtectedPawn = previous;
            }

            if (map.roofGrid.RoofAt(cell) != null)
            {
                map.roofGrid.SetRoof(cell, null);
            }
            map.roofGrid.Drawer.SetDirty();
            map.mapDrawer.MapMeshDirty(cell, MapMeshFlagDefOf.Roofs);
            map.GetComponent<MechanicalFlightRoofLightingRefresh>().Request(cell);
        }
    }

    public sealed class MechanicalFlightRoofLightingRefresh : MapComponent
    {
        private readonly HashSet<IntVec3> pending = new();
        private int refreshAt = -1;

        public MechanicalFlightRoofLightingRefresh(Map map) : base(map)
        {
        }

        public void Request(IntVec3 cell)
        {
            foreach (IntVec3 nearby in CellRect.CenteredOn(cell, 1).ClipInsideMap(map))
            {
                map.glowGrid.DirtyCell(nearby);
                pending.Add(nearby);
            }
            refreshAt = Find.TickManager.TicksGame + 2;
        }

        public override void MapComponentTick()
        {
            if (refreshAt < 0 || Find.TickManager.TicksGame < refreshAt)
            {
                return;
            }

            HashSet<Section> sections = new();
            foreach (IntVec3 cell in pending)
            {
                map.mapDrawer.MapMeshDirty(cell, MapMeshFlagDefOf.GroundGlow);
                sections.Add(map.mapDrawer.SectionAt(cell));
            }
            foreach (Section section in sections)
            {
                SectionLayer? layer = section?.GetLayer(typeof(SectionLayer_LightingOverlay));
                if (layer == null)
                {
                    continue;
                }
                layer.GetSubMesh(MatBases.LightOverlay).Clear(MeshParts.All);
                section.RegenerateSingleLayer(layer);
            }
            pending.Clear();
            refreshAt = -1;
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    internal static class MechanicalFlightRoofPunchProtection
    {
        [System.ThreadStatic]
        internal static Pawn? ProtectedPawn;

        public static bool Prefix(Thing __instance, DamageInfo dinfo,
            ref DamageWorker.DamageResult __result)
        {
            if (ProtectedPawn != null && ReferenceEquals(__instance, ProtectedPawn)
                && dinfo.Def == DamageDefOf.Crush)
            {
                __result = new DamageWorker.DamageResult();
                return false;
            }
            return true;
        }
    }
}
