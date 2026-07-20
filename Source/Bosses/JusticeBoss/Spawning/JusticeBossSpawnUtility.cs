using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor
{
    public static class JusticeBossSpawnUtility
    {
        public const float BossReplaceChance = 0.02f;

        public const int DropOpenDelayTicks = 60;

        public static List<PawnKindDef> BuildWaveComposition(
            int waveIndex,
            bool applyBossReplace = true)
        {
            int total = waveIndex <= 1 ? 10 : 5;
            int heavySlots = waveIndex <= 1 ? 4 : 2;
            int normalSlots = total - heavySlots;

            List<PawnGenOption> combat = JusticeBossMechPoolUtility.BuildCombatPool();
            List<PawnGenOption> heavy = JusticeBossMechPoolUtility.BuildHeavyPool(combat);
            List<PawnKindDef> result = new List<PawnKindDef>(total);

            for (int i = 0; i < heavySlots; i++)
            {
                PawnKindDef? kind = JusticeBossMechPoolUtility.PickWeighted(heavy)
                    ?? JusticeBossMechPoolUtility.PickWeighted(combat);
                if (kind != null)
                {
                    result.Add(kind);
                }
            }

            for (int i = 0; i < normalSlots; i++)
            {
                PawnKindDef? kind = JusticeBossMechPoolUtility.PickWeighted(combat);
                if (kind != null)
                {
                    result.Add(kind);
                }
            }

            if (applyBossReplace)
            {
                TryApplyBossReplacement(result);
            }

            return result;
        }

        public static bool TryApplyBossReplacement(
            List<PawnKindDef> composition,
            bool force = false)
        {
            if (composition == null || composition.Count == 0)
            {
                return false;
            }

            if (!force && !Rand.Chance(BossReplaceChance))
            {
                return false;
            }

            List<PawnKindDef> bosses = JusticeBossMechPoolUtility.BuildBossCandidatePool();
            PawnKindDef? bossKind = JusticeBossMechPoolUtility.PickBossOrNull(bosses);
            if (bossKind == null)
            {
                return false;
            }

            int index = Rand.Range(0, composition.Count);
            composition[index] = bossKind;
            return true;
        }

        public static int LaunchWaveDropPodsNear(
            Pawn justice,
            IntVec3 fallbackAnchor,
            int justiceEventId,
            List<PawnKindDef> kinds,
            out Lord? assaultLordCache)
        {
            assaultLordCache = null;
            int bossReplacements = 0;
            Map? map = justice?.Map ?? justice?.MapHeld;
            if (justice == null || map == null || kinds == null || kinds.Count == 0)
            {
                return 0;
            }

            Faction? faction = justice.Faction ?? Faction.OfMechanoids;
            IntVec3 center = justice.Spawned ? justice.Position : fallbackAnchor;
            if (!center.IsValid)
            {
                center = fallbackAnchor;
            }

            assaultLordCache = JusticeBossLordUtility.EnsureAssaultLord(
                map,
                faction,
                justiceEventId);

            List<Pawn> pawns = new List<Pawn>();
            HashSet<Pawn> bossPawns = new HashSet<Pawn>();
            for (int i = 0; i < kinds.Count; i++)
            {
                PawnKindDef kind = kinds[i];
                if (kind == null)
                {
                    continue;
                }

                bool wantBoss = kind.isBoss && !JusticePawnUtility.IsBossJustice(kind);
                Pawn? pawn = TryGeneratePawn(kind, faction);
                if (pawn == null && wantBoss)
                {
                    PawnKindDef? fallback =
                        JusticeBossMechPoolUtility.PickWeighted(
                            JusticeBossMechPoolUtility.BuildCombatPool());
                    if (fallback != null)
                    {
                        pawn = TryGeneratePawn(fallback, faction);
                    }
                }
                else if (pawn != null && wantBoss)
                {
                    bossPawns.Add(pawn);
                }

                if (pawn != null)
                {
                    pawns.Add(pawn);
                }
            }

            if (!LaunchPawnDropPods(
                    map,
                    faction,
                    center,
                    fallbackAnchor,
                    justiceEventId,
                    JusticeBossDropRole.Assault,
                    pawns,
                    bossPawns,
                    out bossReplacements))
            {
                return 0;
            }

            return bossReplacements;
        }

        public static bool LaunchGuardDropPodsNear(
            Map map,
            Faction faction,
            IntVec3 anchor,
            int justiceEventId,
            int count,
            out Lord? guardLord)
        {
            guardLord = null;
            List<PawnGenOption> combat = JusticeBossMechPoolUtility.BuildCombatPool();
            if (combat.Count == 0 || map == null || faction == null)
            {
                return false;
            }

            guardLord = JusticeBossLordUtility.EnsureGuardLord(
                map,
                faction,
                justiceEventId,
                anchor);

            List<Pawn> pawns = new List<Pawn>();
            for (int i = 0; i < count; i++)
            {
                PawnKindDef? kind = JusticeBossMechPoolUtility.PickWeighted(combat);
                if (kind == null)
                {
                    continue;
                }

                Pawn? pawn = TryGeneratePawn(kind, faction);
                if (pawn != null)
                {
                    pawns.Add(pawn);
                }
            }

            return LaunchPawnDropPods(
                map,
                faction,
                anchor,
                anchor,
                justiceEventId,
                JusticeBossDropRole.Guard,
                pawns,
                bossPawns: null,
                out _);
        }

        private static Pawn? TryGeneratePawn(PawnKindDef kind, Faction? faction)
        {
            try
            {
                PawnGenerationRequest request = new PawnGenerationRequest(
                    kind,
                    faction,
                    PawnGenerationContext.NonPlayer,
                    forceGenerateNewPawn: true);
                Pawn pawn = PawnGenerator.GeneratePawn(request);
                pawn.SetFaction(faction);
                MechanoidMechanitorWorkModeUtility.EnsureMobileCombatHediff(pawn);
                if (!Find.WorldPawns.Contains(pawn))
                {
                    Find.WorldPawns.PassToWorld(pawn);
                }

                return pawn;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MAP JusticeBoss] Failed to generate " + kind?.defName + ": " + e.Message);
                return null;
            }
        }

        private static bool LaunchPawnDropPods(
            Map map,
            Faction? faction,
            IntVec3 center,
            IntVec3 anchor,
            int justiceEventId,
            JusticeBossDropRole role,
            List<Pawn> pawns,
            HashSet<Pawn>? bossPawns,
            out int bossReplacementsLaunched)
        {
            bossReplacementsLaunched = 0;
            if (pawns.Count == 0)
            {
                return true;
            }

            MapComponent_JusticeBossDropTracker tracker =
                MapComponent_JusticeBossDropTracker.For(map);

            List<IntVec3> reserved = new List<IntVec3>();
            List<List<Pawn>> podGroups = new List<List<Pawn>>();
            List<IntVec3> podCells = new List<IntVec3>();

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (TryFindDropCell(map, center, faction, reserved, out IntVec3 cell))
                {
                    reserved.Add(cell);
                    podGroups.Add(new List<Pawn> { pawn });
                    podCells.Add(cell);
                }
                else if (podGroups.Count > 0)
                {
                    podGroups[podGroups.Count - 1].Add(pawn);
                }
                else
                {
                    Log.ErrorOnce(
                        "[MAP JusticeBoss] No legal drop cells for summoned mechanoids; aborting launch.",
                        map.uniqueID ^ 0x5B0D);
                    DiscardPawns(pawns);
                    return false;
                }
            }

            int launchedPods = 0;
            for (int i = 0; i < podGroups.Count; i++)
            {
                List<Pawn> group = podGroups[i];
                IntVec3 cell = podCells[i];
                foreach (Pawn pawn in group)
                {
                    tracker.Register(
                        new PendingJusticeBossDrop
                        {
                            pawn = pawn,
                            justiceEventId = justiceEventId,
                            role = role,
                            faction = faction,
                            anchorCell = anchor,
                            registeredTick = Find.TickManager.TicksGame,
                        });
                }

                if (!TryMakeDropPod(map, faction, cell, group))
                {
                    foreach (Pawn pawn in group)
                    {
                        tracker.Unregister(pawn);
                    }

                    DiscardPawns(group);
                    Log.Warning("[MAP JusticeBoss] Failed to create drop pod for summoned mechs.");
                    continue;
                }

                launchedPods++;
                if (bossPawns != null)
                {
                    for (int p = 0; p < group.Count; p++)
                    {
                        if (bossPawns.Contains(group[p]))
                        {
                            bossReplacementsLaunched++;
                        }
                    }
                }
            }

            return launchedPods > 0;
        }

        private static bool TryMakeDropPod(
            Map map,
            Faction? faction,
            IntVec3 cell,
            List<Pawn> group)
        {
            try
            {
                ActiveTransporterInfo info = new ActiveTransporterInfo();
                info.openDelay = DropOpenDelayTicks;
                info.leaveSlag = false;
                info.savePawnsWithReferenceMode = true;
                info.spawnWipeMode = null;
                info.moveItemsAsideBeforeSpawning = true;
                info.despawnPodBeforeSpawningThing = true;

                for (int i = 0; i < group.Count; i++)
                {
                    if (info.innerContainer.TryAdd(group[i], 1) <= 0)
                    {
                        return false;
                    }
                }

                DropPodUtility.MakeDropPodAt(cell, map, info, faction);
                return true;
            }
            catch (Exception e)
            {
                Log.Warning("[MAP JusticeBoss] Drop pod creation failed: " + e.Message);
                return false;
            }
        }

        private static bool TryFindDropCell(
            Map map,
            IntVec3 center,
            Faction? faction,
            List<IntVec3> reserved,
            out IntVec3 cell)
        {
            IntVec2 size = IntVec2.One;
            for (int radius = 3; radius <= 24; radius += 2)
            {
                if (DropCellFinder.TryFindDropSpotNear(
                        center,
                        map,
                        out cell,
                        allowFogged: false,
                        canRoofPunch: false,
                        radius,
                        allowIndoors: true,
                        size,
                        mustBeReachableFromCenter: false)
                    && DropCellFinder.SkyfallerCanLandAt(cell, map, size, faction)
                    && !reserved.Contains(cell)
                    && cell.GetRoof(map) != RoofDefOf.RoofRockThick)
                {
                    return true;
                }
            }

            cell = IntVec3.Invalid;
            return false;
        }

        private static void DiscardPawns(List<Pawn> pawns)
        {
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.Destroyed)
                {
                    continue;
                }

                if (pawn.ParentHolder is IThingHolder holder
                    && holder.GetDirectlyHeldThings() is ThingOwner owner)
                {
                    owner.Remove(pawn);
                }

                if (Find.WorldPawns.Contains(pawn))
                {
                    Find.WorldPawns.RemovePawn(pawn);
                }

                if (!pawn.Destroyed)
                {
                    pawn.Destroy();
                }
            }
        }
    }
}