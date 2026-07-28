using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor
{
    public sealed class JusticeBossDropLaunchResult
    {
        public JusticeBossDropLaunchResult(int requestedPawnCount)
        {
            RequestedPawnCount = requestedPawnCount;
        }

        public int RequestedPawnCount { get; }

        public int LaunchedPawnCount { get; internal set; }

        public int BossReplacementCount { get; internal set; }

        public bool FatalFailure { get; internal set; }

        public List<PawnKindDef> FailedKinds { get; } = new List<PawnKindDef>();

        public bool AnyPodLaunched => LaunchedPawnCount > 0;

        public bool FullyLaunched =>
            !FatalFailure
            && FailedKinds.Count == 0
            && LaunchedPawnCount == RequestedPawnCount;
    }

    public static class JusticeBossSpawnUtility
    {
        public const float BossReplaceChance = 0.02f;

        public const int DropOpenDelayTicks = 60;

        public static List<PawnKindDef> BuildWaveComposition(
            int waveIndex,
            bool applyBossReplace = true)
        {
            return BuildWaveComposition(
                waveIndex,
                JusticeBossDifficultyValues.DefaultMechsPerWave,
                applyBossReplace);
        }

        public static List<PawnKindDef> BuildWaveComposition(
            int waveIndex,
            int mechsPerWave,
            bool applyBossReplace = true)
        {
            int baseCount =
                JusticeBossDifficultyValues.ClampMechsPerWave(
                    mechsPerWave);

            int total = waveIndex <= 1
                ? baseCount * 2
                : baseCount;

            int heavySlots = total * 2 / 5;
            int normalSlots = total - heavySlots;

            List<PawnGenOption> combat =
                JusticeBossMechPoolUtility.BuildCombatPool();

            List<PawnGenOption> heavy =
                JusticeBossMechPoolUtility.BuildHeavyPool(combat);

            List<PawnKindDef> result =
                new List<PawnKindDef>(total);

            for (int i = 0; i < heavySlots; i++)
            {
                PawnKindDef? kind =
                    JusticeBossMechPoolUtility.PickWeighted(heavy)
                    ?? JusticeBossMechPoolUtility.PickWeighted(combat);

                if (kind != null)
                {
                    result.Add(kind);
                }
            }

            for (int i = 0; i < normalSlots; i++)
            {
                PawnKindDef? kind =
                    JusticeBossMechPoolUtility.PickWeighted(combat);

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

        public static JusticeBossDropLaunchResult LaunchWaveDropPodsNear(
            Pawn justice,
            IntVec3 fallbackAnchor,
            int justiceEventId,
            List<PawnKindDef> kinds,
            out Lord? assaultLordCache)
        {
            assaultLordCache = null;
            JusticeBossDropLaunchResult result =
                new JusticeBossDropLaunchResult(kinds?.Count ?? 0);
            Map? map = justice?.Map ?? justice?.MapHeld;
            if (justice == null || map == null || kinds == null || kinds.Count == 0)
            {
                if (kinds != null)
                {
                    result.FailedKinds.AddRange(kinds);
                }

                return result;
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
            Dictionary<Pawn, PawnKindDef> pawnKinds = new Dictionary<Pawn, PawnKindDef>();
            HashSet<Pawn> bossPawns = new HashSet<Pawn>();
            for (int i = 0; i < kinds.Count; i++)
            {
                PawnKindDef kind = kinds[i];
                if (kind == null)
                {
                    continue;
                }

                bool wantBoss = kind.isBoss && !JusticePawnUtility.IsBossJustice(kind);
                PawnKindDef launchKind = kind;
                Pawn? pawn = TryGeneratePawn(kind, faction);
                if (pawn == null && wantBoss)
                {
                    PawnKindDef? fallback =
                        JusticeBossMechPoolUtility.PickWeighted(
                            JusticeBossMechPoolUtility.BuildCombatPool());
                    if (fallback != null)
                    {
                        launchKind = fallback;
                        pawn = TryGeneratePawn(fallback, faction);
                    }
                }
                else if (pawn != null && wantBoss)
                {
                    bossPawns.Add(pawn);
                }

                if (pawn == null)
                {
                    result.FailedKinds.Add(launchKind);
                    continue;
                }

                pawns.Add(pawn);
                pawnKinds[pawn] = launchKind;
            }

            JusticeBossDropLaunchResult launchResult = LaunchPawnDropPods(
                map,
                faction,
                center,
                fallbackAnchor,
                justiceEventId,
                JusticeBossDropRole.Assault,
                pawns,
                pawnKinds,
                bossPawns);
            result.LaunchedPawnCount = launchResult.LaunchedPawnCount;
            result.BossReplacementCount = launchResult.BossReplacementCount;
            result.FatalFailure = launchResult.FatalFailure;
            result.FailedKinds.AddRange(launchResult.FailedKinds);
            return result;
        }

        public static JusticeBossDropLaunchResult LaunchGuardDropPodsNear(
            Map map,
            Faction faction,
            IntVec3 anchor,
            int justiceEventId,
            int count,
            out Lord? guardLord)
        {
            List<PawnGenOption> combat = JusticeBossMechPoolUtility.BuildCombatPool();
            if (combat.Count == 0 || map == null || faction == null)
            {
                guardLord = null;
                JusticeBossDropLaunchResult failed = new JusticeBossDropLaunchResult(count)
                {
                    FatalFailure = true,
                };
                Log.ErrorOnce(
                    "[MAP JusticeBoss] Cannot build the guard mechanoid pool.",
                    map?.uniqueID ^ 0x2C91 ?? 0x2C91);
                return failed;
            }

            List<PawnKindDef> kinds = new List<PawnKindDef>(count);
            for (int i = 0; i < count; i++)
            {
                PawnKindDef? kind = JusticeBossMechPoolUtility.PickWeighted(combat);
                if (kind != null)
                {
                    kinds.Add(kind);
                }
            }

            return LaunchGuardDropPodsNear(
                map,
                faction,
                anchor,
                justiceEventId,
                kinds,
                out guardLord);
        }

        public static JusticeBossDropLaunchResult LaunchGuardDropPodsNear(
            Map map,
            Faction faction,
            IntVec3 anchor,
            int justiceEventId,
            List<PawnKindDef> kinds,
            out Lord? guardLord)
        {
            guardLord = null;
            JusticeBossDropLaunchResult result =
                new JusticeBossDropLaunchResult(kinds?.Count ?? 0);
            if (map == null || faction == null || kinds == null || kinds.Count == 0)
            {
                if (kinds != null)
                {
                    result.FailedKinds.AddRange(kinds);
                }

                return result;
            }

            guardLord = JusticeBossLordUtility.EnsureGuardLord(
                map,
                faction,
                justiceEventId,
                anchor);

            List<Pawn> pawns = new List<Pawn>();
            Dictionary<Pawn, PawnKindDef> pawnKinds = new Dictionary<Pawn, PawnKindDef>();
            for (int i = 0; i < kinds.Count; i++)
            {
                PawnKindDef kind = kinds[i];
                Pawn? pawn = TryGeneratePawn(kind, faction);
                if (pawn == null)
                {
                    result.FailedKinds.Add(kind);
                    continue;
                }

                pawns.Add(pawn);
                pawnKinds[pawn] = kind;
            }

            JusticeBossDropLaunchResult launchResult = LaunchPawnDropPods(
                map,
                faction,
                anchor,
                anchor,
                justiceEventId,
                JusticeBossDropRole.Guard,
                pawns,
                pawnKinds,
                bossPawns: null);
            result.LaunchedPawnCount = launchResult.LaunchedPawnCount;
            result.FatalFailure = launchResult.FatalFailure;
            result.FailedKinds.AddRange(launchResult.FailedKinds);
            return result;
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
                return pawn;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MAP JusticeBoss] Failed to generate " + kind?.defName + ": " + e.Message);
                return null;
            }
        }

        private static JusticeBossDropLaunchResult LaunchPawnDropPods(
            Map map,
            Faction? faction,
            IntVec3 center,
            IntVec3 anchor,
            int justiceEventId,
            JusticeBossDropRole role,
            List<Pawn> pawns,
            Dictionary<Pawn, PawnKindDef> pawnKinds,
            HashSet<Pawn>? bossPawns)
        {
            JusticeBossDropLaunchResult result = new JusticeBossDropLaunchResult(pawns.Count);
            if (pawns.Count == 0)
            {
                return result;
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
                    AddFailedKinds(result, pawns, pawnKinds);
                    DiscardPawns(pawns);
                    return result;
                }
            }

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

                    AddFailedKinds(result, group, pawnKinds);
                    DiscardPawns(group);
                    Log.Warning("[MAP JusticeBoss] Failed to create drop pod for summoned mechs.");
                    continue;
                }

                result.LaunchedPawnCount += group.Count;
                if (bossPawns != null)
                {
                    for (int p = 0; p < group.Count; p++)
                    {
                        if (bossPawns.Contains(group[p]))
                        {
                            result.BossReplacementCount++;
                        }
                    }
                }
            }

            return result;
        }

        private static void AddFailedKinds(
            JusticeBossDropLaunchResult result,
            List<Pawn> pawns,
            Dictionary<Pawn, PawnKindDef> pawnKinds)
        {
            for (int i = 0; i < pawns.Count; i++)
            {
                if (pawnKinds.TryGetValue(pawns[i], out PawnKindDef kind) && kind != null)
                {
                    result.FailedKinds.Add(kind);
                }
            }
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
                info.savePawnsWithReferenceMode = false;
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
                        mustBeReachableFromCenter: true)
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
