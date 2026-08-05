using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        private const int MinimumSafeDropSearchRadius = 5;

        private const int MaximumDropSearchRadius = 24;

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
            int waveIndex =
                JusticeBossLaunchTracePatchManager.ResolveWaveIndex(
                    justiceEventId);
            bool traceSession = JusticeBossLaunchTracePatchManager.Active;
            bool trace = JusticeBossLaunchTracePatchManager.CanWriteNormal;
            long launchStarted =
                traceSession ? Stopwatch.GetTimestamp() : 0L;

            if (trace)
            {
                JusticeBossLaunchTracePatchManager.Write(
                    "WAVE_LAUNCH_BEGIN",
                    "event=" + justiceEventId
                        + " wave=" + waveIndex
                        + " role=Assault"
                        + " requested=" + result.RequestedPawnCount
                        + " map=" + (map?.uniqueID ?? -1),
                    JusticeBossTraceWriteMode.Critical);
            }

            try
            {
                if (justice == null || map == null || kinds == null || kinds.Count == 0)
                {
                    if (kinds != null)
                    {
                        result.FailedKinds.AddRange(kinds);
                    }

                    if (trace)
                    {
                        JusticeBossLaunchTracePatchManager.Write(
                            "WAVE_LAUNCH_END",
                            "event=" + justiceEventId
                                + " wave=" + waveIndex
                                + " role=Assault result=invalid-input"
                                + " requested=" + result.RequestedPawnCount
                                + " elapsedMs="
                                + JusticeBossTraceFormatting.FormatElapsed(launchStarted),
                            JusticeBossTraceWriteMode.Critical);
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
                Dictionary<Pawn, PawnKindDef> pawnKinds =
                    new Dictionary<Pawn, PawnKindDef>();
                HashSet<Pawn> bossPawns = new HashSet<Pawn>();

                if (trace)
                {
                    JusticeBossLaunchTracePatchManager.Write(
                        "WAVE_GENERATION_BEGIN",
                        "event=" + justiceEventId
                            + " wave=" + waveIndex
                            + " role=Assault requested=" + kinds.Count,
                        JusticeBossTraceWriteMode.Critical);
                }

                for (int i = 0; i < kinds.Count; i++)
                {
                    PawnKindDef kind = kinds[i];
                    if (kind == null)
                    {
                        continue;
                    }

                    bool wantBoss =
                        kind.isBoss && !JusticePawnUtility.IsBossJustice(kind);
                    PawnKindDef launchKind = kind;
                    Pawn? pawn = TryGeneratePawn(
                        kind,
                        faction,
                        justiceEventId,
                        waveIndex,
                        JusticeBossDropRole.Assault,
                        i,
                        attempt: 1);

                    if (pawn == null && wantBoss)
                    {
                        PawnKindDef? fallback =
                            JusticeBossMechPoolUtility.PickWeighted(
                                JusticeBossMechPoolUtility.BuildCombatPool());
                        if (fallback != null)
                        {
                            launchKind = fallback;
                            pawn = TryGeneratePawn(
                                fallback,
                                faction,
                                justiceEventId,
                                waveIndex,
                                JusticeBossDropRole.Assault,
                                i,
                                attempt: 2);
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

                if (trace)
                {
                    JusticeBossLaunchTracePatchManager.Write(
                        "WAVE_GENERATION_END",
                        "event=" + justiceEventId
                            + " wave=" + waveIndex
                            + " role=Assault generated=" + pawns.Count
                            + " failed=" + result.FailedKinds.Count
                            + " elapsedMs="
                            + JusticeBossTraceFormatting.FormatElapsed(launchStarted),
                        JusticeBossTraceWriteMode.Critical);
                }

                JusticeBossDropLaunchResult launchResult = LaunchPawnDropPods(
                    map,
                    faction,
                    center,
                    fallbackAnchor,
                    justiceEventId,
                    waveIndex,
                    JusticeBossDropRole.Assault,
                    pawns,
                    pawnKinds,
                    bossPawns);
                result.LaunchedPawnCount = launchResult.LaunchedPawnCount;
                result.BossReplacementCount = launchResult.BossReplacementCount;
                result.FatalFailure = launchResult.FatalFailure;
                result.FailedKinds.AddRange(launchResult.FailedKinds);

                if (trace)
                {
                    JusticeBossLaunchTracePatchManager.Write(
                        "WAVE_LAUNCH_END",
                        "event=" + justiceEventId
                            + " wave=" + waveIndex
                            + " role=Assault requested=" + result.RequestedPawnCount
                            + " launched=" + result.LaunchedPawnCount
                            + " failed=" + result.FailedKinds.Count
                            + " fatal=" + result.FatalFailure
                            + " elapsedMs="
                            + JusticeBossTraceFormatting.FormatElapsed(launchStarted),
                        JusticeBossTraceWriteMode.Critical);
                }

                return result;
            }
            catch (Exception exception)
            {
                JusticeBossLaunchTracePatchManager.WriteException(
                    "WAVE_LAUNCH_EXCEPTION",
                    "event=" + justiceEventId
                        + " wave=" + waveIndex
                        + " role=Assault"
                        + " requested=" + result.RequestedPawnCount,
                    exception);
                throw;
            }
        }

        public static JusticeBossDropLaunchResult LaunchGuardDropPodsNear(
            Map map,
            Faction faction,
            IntVec3 anchor,
            int justiceEventId,
            int count,
            out Lord? guardLord)
        {
            List<PawnGenOption> combat =
                JusticeBossMechPoolUtility.BuildCombatPool();
            if (combat.Count == 0 || map == null || faction == null)
            {
                guardLord = null;
                JusticeBossDropLaunchResult failed =
                    new JusticeBossDropLaunchResult(count)
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
                PawnKindDef? kind =
                    JusticeBossMechPoolUtility.PickWeighted(combat);
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
            bool traceSession = JusticeBossLaunchTracePatchManager.Active;
            bool trace = JusticeBossLaunchTracePatchManager.CanWriteNormal;
            long launchStarted =
                traceSession ? Stopwatch.GetTimestamp() : 0L;

            if (trace)
            {
                JusticeBossLaunchTracePatchManager.Write(
                    "GUARD_LAUNCH_BEGIN",
                    "event=" + justiceEventId
                        + " wave=0 role=Guard requested=" + result.RequestedPawnCount
                        + " map=" + (map?.uniqueID ?? -1),
                    JusticeBossTraceWriteMode.Critical);
            }

            try
            {
                if (map == null || faction == null || kinds == null || kinds.Count == 0)
                {
                    if (kinds != null)
                    {
                        result.FailedKinds.AddRange(kinds);
                    }

                    if (trace)
                    {
                        JusticeBossLaunchTracePatchManager.Write(
                            "GUARD_LAUNCH_END",
                            "event=" + justiceEventId
                                + " wave=0 role=Guard result=invalid-input"
                                + " requested=" + result.RequestedPawnCount
                                + " elapsedMs="
                                + JusticeBossTraceFormatting.FormatElapsed(launchStarted),
                            JusticeBossTraceWriteMode.Critical);
                    }

                    return result;
                }

                guardLord = JusticeBossLordUtility.EnsureGuardLord(
                    map,
                    faction,
                    justiceEventId,
                    anchor);

                List<Pawn> pawns = new List<Pawn>();
                Dictionary<Pawn, PawnKindDef> pawnKinds =
                    new Dictionary<Pawn, PawnKindDef>();
                for (int i = 0; i < kinds.Count; i++)
                {
                    PawnKindDef kind = kinds[i];
                    Pawn? pawn = TryGeneratePawn(
                        kind,
                        faction,
                        justiceEventId,
                        0,
                        JusticeBossDropRole.Guard,
                        i,
                        1);
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
                    0,
                    JusticeBossDropRole.Guard,
                    pawns,
                    pawnKinds,
                    bossPawns: null);
                result.LaunchedPawnCount = launchResult.LaunchedPawnCount;
                result.FatalFailure = launchResult.FatalFailure;
                result.FailedKinds.AddRange(launchResult.FailedKinds);

                if (trace)
                {
                    JusticeBossLaunchTracePatchManager.Write(
                        "GUARD_LAUNCH_END",
                        "event=" + justiceEventId
                            + " wave=0 role=Guard requested=" + result.RequestedPawnCount
                            + " launched=" + result.LaunchedPawnCount
                            + " failed=" + result.FailedKinds.Count
                            + " fatal=" + result.FatalFailure
                            + " elapsedMs="
                            + JusticeBossTraceFormatting.FormatElapsed(launchStarted),
                        JusticeBossTraceWriteMode.Critical);
                }

                return result;
            }
            catch (Exception exception)
            {
                JusticeBossLaunchTracePatchManager.WriteException(
                    "GUARD_LAUNCH_EXCEPTION",
                    "event=" + justiceEventId
                        + " wave=0 role=Guard"
                        + " requested=" + result.RequestedPawnCount,
                    exception);
                throw;
            }
        }

        private static string BuildPawnTraceDetail(
            int justiceEventId,
            int waveIndex,
            JusticeBossDropRole role,
            int pawnIndex,
            int attempt,
            PawnKindDef? kind)
        {
            return "event=" + justiceEventId
                + " wave=" + waveIndex
                + " role=" + role
                + " pawnIndex=" + pawnIndex
                + " attempt=" + attempt
                + " pawnKind="
                + JusticeBossTraceFormatting.Sanitize(kind?.defName);
        }

        private static Pawn? TryGeneratePawn(
            PawnKindDef kind,
            Faction? faction,
            int justiceEventId,
            int waveIndex,
            JusticeBossDropRole role,
            int pawnIndex,
            int attempt)
        {
            bool traceSession = JusticeBossLaunchTracePatchManager.Active;
            bool trace = JusticeBossLaunchTracePatchManager.CanWriteNormal;
            long started =
                traceSession ? Stopwatch.GetTimestamp() : 0L;
            string? baseDetail = trace
                ? BuildPawnTraceDetail(
                    justiceEventId,
                    waveIndex,
                    role,
                    pawnIndex,
                    attempt,
                    kind)
                : null;

            if (trace)
            {
                JusticeBossLaunchTracePatchManager.Write(
                    "GENERATE_PAWN_BEGIN",
                    baseDetail,
                    JusticeBossTraceWriteMode.Critical);
            }

            try
            {
                PawnGenerationRequest request = new PawnGenerationRequest(
                    kind,
                    faction,
                    PawnGenerationContext.NonPlayer,
                    forceGenerateNewPawn: true);
                Pawn pawn = PawnGenerator.GeneratePawn(request);

                if (trace)
                {
                    JusticeBossLaunchTracePatchManager.Write(
                        "GENERATE_PAWN_CREATED",
                        baseDetail
                            + " pawnId=" + pawn.thingIDNumber
                            + " faction="
                            + JusticeBossTraceFormatting.Sanitize(
                                pawn.Faction?.def?.defName),
                        JusticeBossTraceWriteMode.Buffered);
                }

                if (pawn.Faction != faction)
                {
                    if (trace)
                    {
                        JusticeBossLaunchTracePatchManager.Write(
                            "SET_FACTION_BEGIN",
                            baseDetail
                                + " pawnId=" + pawn.thingIDNumber
                                + " currentFaction="
                                + JusticeBossTraceFormatting.Sanitize(
                                    pawn.Faction?.def?.defName)
                                + " targetFaction="
                                + JusticeBossTraceFormatting.Sanitize(
                                    faction?.def?.defName),
                            JusticeBossTraceWriteMode.Critical);
                    }

                    pawn.SetFaction(faction);

                    if (trace)
                    {
                        JusticeBossLaunchTracePatchManager.Write(
                            "SET_FACTION_END",
                            baseDetail
                                + " pawnId=" + pawn.thingIDNumber
                                + " faction="
                                + JusticeBossTraceFormatting.Sanitize(
                                    pawn.Faction?.def?.defName),
                            JusticeBossTraceWriteMode.Buffered);
                    }
                }

                if (trace)
                {
                    JusticeBossLaunchTracePatchManager.Write(
                        "MOBILE_COMBAT_HEDIFF_BEGIN",
                        baseDetail + " pawnId=" + pawn.thingIDNumber,
                        JusticeBossTraceWriteMode.Critical);
                }

                MechanoidMechanitorWorkModeUtility.EnsureMobileCombatHediff(pawn);

                if (trace)
                {
                    JusticeBossLaunchTracePatchManager.Write(
                        "MOBILE_COMBAT_HEDIFF_END",
                        baseDetail
                            + " pawnId=" + pawn.thingIDNumber
                            + " elapsedMs="
                            + JusticeBossTraceFormatting.FormatElapsed(started),
                        JusticeBossTraceWriteMode.Buffered);
                    JusticeBossLaunchTracePatchManager.Write(
                        "GENERATE_PAWN_END",
                        baseDetail
                            + " pawnId=" + pawn.thingIDNumber
                            + " elapsedMs="
                            + JusticeBossTraceFormatting.FormatElapsed(started),
                        JusticeBossTraceWriteMode.Buffered);
                }

                return pawn;
            }
            catch (Exception exception)
            {
                string exceptionDetail =
                    baseDetail
                    ?? BuildPawnTraceDetail(
                        justiceEventId,
                        waveIndex,
                        role,
                        pawnIndex,
                        attempt,
                        kind);
                JusticeBossLaunchTracePatchManager.WriteException(
                    "GENERATE_PAWN_EXCEPTION",
                    exceptionDetail
                        + " elapsedMs="
                        + JusticeBossTraceFormatting.FormatElapsed(started),
                    exception);
                Log.Warning(
                    "[MAP JusticeBoss] Failed to generate "
                        + kind?.defName
                        + ": "
                        + exception.Message);
                return null;
            }
        }

        private static JusticeBossDropLaunchResult LaunchPawnDropPods(
            Map map,
            Faction? faction,
            IntVec3 center,
            IntVec3 anchor,
            int justiceEventId,
            int waveIndex,
            JusticeBossDropRole role,
            List<Pawn> pawns,
            Dictionary<Pawn, PawnKindDef> pawnKinds,
            HashSet<Pawn>? bossPawns)
        {
            JusticeBossDropLaunchResult result =
                new JusticeBossDropLaunchResult(pawns.Count);
            if (pawns.Count == 0)
            {
                return result;
            }

            bool trace = JusticeBossLaunchTracePatchManager.CanWriteNormal;
            long planningStarted =
                trace ? Stopwatch.GetTimestamp() : 0L;
            MapComponent_JusticeBossDropTracker tracker =
                MapComponent_JusticeBossDropTracker.For(map);

            List<IntVec3> reserved = new List<IntVec3>();
            List<List<Pawn>> podGroups = new List<List<Pawn>>();
            List<IntVec3> podCells = new List<IntVec3>();

            if (trace)
            {
                JusticeBossLaunchTracePatchManager.Write(
                    "DROP_PLAN_BEGIN",
                    "event=" + justiceEventId
                        + " wave=" + waveIndex
                        + " role=" + role
                        + " pawns=" + pawns.Count
                        + " center=" + JusticeBossTraceFormatting.DescribeCell(center)
                        + " map=" + map.uniqueID,
                    JusticeBossTraceWriteMode.Critical);
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                string? baseDetail = trace
                    ? "event=" + justiceEventId
                        + " wave=" + waveIndex
                        + " role=" + role
                        + " pawnIndex=" + i
                        + " pawnId=" + pawn.thingIDNumber
                        + " pawnKind="
                        + JusticeBossTraceFormatting.Sanitize(
                            pawnKinds.TryGetValue(pawn, out PawnKindDef kind)
                                ? kind?.defName
                                : pawn.kindDef?.defName)
                        + " reserved=" + reserved.Count
                        + " podGroups=" + podGroups.Count
                    : null;

                if (trace)
                {
                    JusticeBossLaunchTracePatchManager.Write(
                        "DROP_CELL_BEGIN",
                        baseDetail,
                        JusticeBossTraceWriteMode.Buffered);
                }

                bool found = TryFindDropCell(
                    map,
                    center,
                    faction,
                    reserved,
                    justiceEventId,
                    waveIndex,
                    role,
                    i,
                    pawn,
                    out IntVec3 cell,
                    out int radiusUsed);

                if (trace)
                {
                    JusticeBossLaunchTracePatchManager.Write(
                        "DROP_CELL_END",
                        baseDetail
                            + " success=" + found
                            + " cell=" + JusticeBossTraceFormatting.DescribeCell(cell)
                            + " radius=" + radiusUsed,
                        JusticeBossTraceWriteMode.Buffered);
                }

                if (found)
                {
                    reserved.Add(cell);
                    podGroups.Add(new List<Pawn> { pawn });
                    podCells.Add(cell);
                }
                else if (podGroups.Count > 0)
                {
                    podGroups[podGroups.Count - 1].Add(pawn);
                    if (trace)
                    {
                        JusticeBossLaunchTracePatchManager.Write(
                            "DROP_CELL_FALLBACK_GROUP",
                            baseDetail
                                + " targetPodIndex=" + (podGroups.Count - 1)
                                + " targetCell="
                                + JusticeBossTraceFormatting.DescribeCell(
                                    podCells[podCells.Count - 1]),
                            JusticeBossTraceWriteMode.Buffered);
                    }
                }
                else
                {
                    if (trace)
                    {
                        JusticeBossLaunchTracePatchManager.Write(
                            "DROP_PLAN_ABORTED",
                            baseDetail + " reason=no-legal-initial-cell",
                            JusticeBossTraceWriteMode.Critical);
                    }

                    Log.ErrorOnce(
                        "[MAP JusticeBoss] No legal drop cells for summoned mechanoids; aborting launch.",
                        map.uniqueID ^ 0x5B0D);
                    AddFailedKinds(result, pawns, pawnKinds);
                    DiscardPawns(pawns);
                    return result;
                }
            }

            if (trace)
            {
                JusticeBossLaunchTracePatchManager.Write(
                    "DROP_PLAN_END",
                    "event=" + justiceEventId
                        + " wave=" + waveIndex
                        + " role=" + role
                        + " pawns=" + pawns.Count
                        + " reserved=" + reserved.Count
                        + " podGroups=" + podGroups.Count
                        + " elapsedMs="
                        + JusticeBossTraceFormatting.FormatElapsed(planningStarted),
                    JusticeBossTraceWriteMode.Critical);
            }

            long launchStarted =
                trace ? Stopwatch.GetTimestamp() : 0L;
            for (int i = 0; i < podGroups.Count; i++)
            {
                List<Pawn> group = podGroups[i];
                IntVec3 cell = podCells[i];

                if (trace)
                {
                    JusticeBossLaunchTracePatchManager.Write(
                        "POD_GROUP_BEGIN",
                        "event=" + justiceEventId
                            + " wave=" + waveIndex
                            + " role=" + role
                            + " podIndex=" + i
                            + " groupCount=" + group.Count
                            + " cell=" + JusticeBossTraceFormatting.DescribeCell(cell)
                            + " pendingBefore=" + tracker.PendingCount,
                        JusticeBossTraceWriteMode.Critical);
                }

                for (int p = 0; p < group.Count; p++)
                {
                    Pawn pawn = group[p];
                    string? registerDetail = trace
                        ? "event=" + justiceEventId
                            + " wave=" + waveIndex
                            + " role=" + role
                            + " podIndex=" + i
                            + " pawnInGroup=" + p
                            + " pawnId=" + pawn.thingIDNumber
                            + " pawnKind="
                            + JusticeBossTraceFormatting.Sanitize(
                                pawnKinds.TryGetValue(pawn, out PawnKindDef kind)
                                    ? kind?.defName
                                    : pawn.kindDef?.defName)
                            + " pendingBefore=" + tracker.PendingCount
                        : null;

                    if (trace)
                    {
                        JusticeBossLaunchTracePatchManager.Write(
                            "TRACKER_REGISTER_BEGIN",
                            registerDetail,
                            JusticeBossTraceWriteMode.Critical);
                    }

                    tracker.Register(
                        new PendingJusticeBossDrop
                        {
                            pawn = pawn,
                            justiceEventId = justiceEventId,
                            role = role,
                            faction = faction,
                            anchorCell = anchor,
                            registeredTick =
                                Current.Game?.tickManager?.TicksGame ?? -1,
                        });

                    if (trace)
                    {
                        JusticeBossLaunchTracePatchManager.Write(
                            "TRACKER_REGISTER_END",
                            registerDetail
                                + " pendingAfter=" + tracker.PendingCount,
                            JusticeBossTraceWriteMode.Buffered);
                    }
                }

                if (trace)
                {
                    JusticeBossLaunchTracePatchManager.Write(
                        "MAKE_DROP_POD_BEGIN",
                        "event=" + justiceEventId
                            + " wave=" + waveIndex
                            + " role=" + role
                            + " podIndex=" + i
                            + " groupCount=" + group.Count
                            + " cell=" + JusticeBossTraceFormatting.DescribeCell(cell),
                        JusticeBossTraceWriteMode.Critical);
                }

                bool podCreated = TryMakeDropPod(
                    map,
                    faction,
                    cell,
                    group,
                    justiceEventId,
                    waveIndex,
                    role,
                    i);

                if (trace)
                {
                    JusticeBossLaunchTracePatchManager.Write(
                        "MAKE_DROP_POD_END",
                        "event=" + justiceEventId
                            + " wave=" + waveIndex
                            + " role=" + role
                            + " podIndex=" + i
                            + " groupCount=" + group.Count
                            + " cell=" + JusticeBossTraceFormatting.DescribeCell(cell)
                            + " success=" + podCreated,
                        JusticeBossTraceWriteMode.Buffered);
                }

                if (!podCreated)
                {
                    for (int p = 0; p < group.Count; p++)
                    {
                        tracker.Unregister(group[p]);
                    }

                    AddFailedKinds(result, group, pawnKinds);
                    DiscardPawns(group);
                    Log.Warning(
                        "[MAP JusticeBoss] Failed to create drop pod for summoned mechs.");
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

                if (trace)
                {
                    JusticeBossLaunchTracePatchManager.Write(
                        "POD_GROUP_END",
                        "event=" + justiceEventId
                            + " wave=" + waveIndex
                            + " role=" + role
                            + " podIndex=" + i
                            + " groupCount=" + group.Count
                            + " launchedTotal=" + result.LaunchedPawnCount
                            + " pending=" + tracker.PendingCount,
                        JusticeBossTraceWriteMode.Buffered);
                }
            }

            if (trace)
            {
                JusticeBossLaunchTracePatchManager.Write(
                    "POD_LAUNCH_END",
                    "event=" + justiceEventId
                        + " wave=" + waveIndex
                        + " role=" + role
                        + " podGroups=" + podGroups.Count
                        + " launched=" + result.LaunchedPawnCount
                        + " failed=" + result.FailedKinds.Count
                        + " elapsedMs="
                        + JusticeBossTraceFormatting.FormatElapsed(launchStarted),
                    JusticeBossTraceWriteMode.Critical);
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
                if (pawnKinds.TryGetValue(
                        pawns[i],
                        out PawnKindDef kind)
                    && kind != null)
                {
                    result.FailedKinds.Add(kind);
                }
            }
        }

        private static bool TryMakeDropPod(
            Map map,
            Faction? faction,
            IntVec3 cell,
            List<Pawn> group,
            int justiceEventId,
            int waveIndex,
            JusticeBossDropRole role,
            int podIndex)
        {
            bool trace = JusticeBossLaunchTracePatchManager.CanWriteNormal;
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
                    Pawn pawn = group[i];
                    string? detail = trace
                        ? "event=" + justiceEventId
                            + " wave=" + waveIndex
                            + " role=" + role
                            + " podIndex=" + podIndex
                            + " pawnInGroup=" + i
                            + " pawnId=" + pawn.thingIDNumber
                            + " cell=" + JusticeBossTraceFormatting.DescribeCell(cell)
                        : null;

                    if (trace)
                    {
                        JusticeBossLaunchTracePatchManager.Write(
                            "POD_CONTAINER_ADD_BEGIN",
                            detail,
                            JusticeBossTraceWriteMode.Critical);
                    }

                    int added = info.innerContainer.TryAdd(pawn, 1);

                    if (trace)
                    {
                        JusticeBossLaunchTracePatchManager.Write(
                            "POD_CONTAINER_ADD_END",
                            detail + " added=" + added,
                            JusticeBossTraceWriteMode.Buffered);
                    }

                    if (added <= 0)
                    {
                        return false;
                    }
                }

                if (trace)
                {
                    JusticeBossLaunchTracePatchManager.Write(
                        "DROP_POD_CREATE_BEGIN",
                        "event=" + justiceEventId
                            + " wave=" + waveIndex
                            + " role=" + role
                            + " podIndex=" + podIndex
                            + " groupCount=" + group.Count
                            + " cell=" + JusticeBossTraceFormatting.DescribeCell(cell),
                        JusticeBossTraceWriteMode.Critical);
                }

                DropPodUtility.MakeDropPodAt(cell, map, info, faction);

                if (trace)
                {
                    JusticeBossLaunchTracePatchManager.Write(
                        "DROP_POD_CREATE_END",
                        "event=" + justiceEventId
                            + " wave=" + waveIndex
                            + " role=" + role
                            + " podIndex=" + podIndex
                            + " groupCount=" + group.Count
                            + " cell=" + JusticeBossTraceFormatting.DescribeCell(cell),
                        JusticeBossTraceWriteMode.Buffered);
                }

                return true;
            }
            catch (Exception exception)
            {
                JusticeBossLaunchTracePatchManager.WriteException(
                    "MAKE_DROP_POD_EXCEPTION",
                    "event=" + justiceEventId
                        + " wave=" + waveIndex
                        + " role=" + role
                        + " podIndex=" + podIndex
                        + " groupCount=" + group.Count
                        + " cell=" + JusticeBossTraceFormatting.DescribeCell(cell),
                    exception);
                Log.Warning(
                    "[MAP JusticeBoss] Drop pod creation failed: "
                        + exception.Message);
                return false;
            }
        }

        private static bool TryFindDropCell(
            Map map,
            IntVec3 center,
            Faction? faction,
            List<IntVec3> reserved,
            int justiceEventId,
            int waveIndex,
            JusticeBossDropRole role,
            int pawnIndex,
            Pawn pawn,
            out IntVec3 cell,
            out int radiusUsed)
        {
            bool trace = JusticeBossLaunchTracePatchManager.CanWriteNormal;
            IntVec2 size = IntVec2.One;
            for (
                int radius = MinimumSafeDropSearchRadius;
                radius <= MaximumDropSearchRadius;
                radius += 2)
            {
                // RimWorld 1.6 derives its internal search step from maxRadius / 5.
                // Values below 5 produce a zero step and can loop forever after a failed search.
                int safeRadius = Math.Max(radius, MinimumSafeDropSearchRadius);
                string? detail = trace
                    ? "event=" + justiceEventId
                        + " wave=" + waveIndex
                        + " role=" + role
                        + " pawnIndex=" + pawnIndex
                        + " pawnId=" + pawn.thingIDNumber
                        + " radius=" + safeRadius
                        + " reserved=" + reserved.Count
                        + " center=" + JusticeBossTraceFormatting.DescribeCell(center)
                    : null;

                if (trace)
                {
                    JusticeBossLaunchTracePatchManager.Write(
                        "DROP_CELL_RADIUS_BEGIN",
                        detail,
                        JusticeBossTraceWriteMode.Critical);
                }

                bool foundSpot = DropCellFinder.TryFindDropSpotNear(
                    center,
                    map,
                    out IntVec3 candidate,
                    allowFogged: false,
                    canRoofPunch: false,
                    safeRadius,
                    allowIndoors: true,
                    size,
                    mustBeReachableFromCenter: true);

                if (trace)
                {
                    JusticeBossLaunchTracePatchManager.Write(
                        "DROP_CELL_RADIUS_END",
                        detail
                            + " foundSpot=" + foundSpot
                            + " candidate="
                            + JusticeBossTraceFormatting.DescribeCell(candidate),
                        JusticeBossTraceWriteMode.Buffered);
                }

                if (!foundSpot)
                {
                    continue;
                }

                if (trace)
                {
                    JusticeBossLaunchTracePatchManager.Write(
                        "DROP_CELL_VALIDATE_BEGIN",
                        detail
                            + " candidate="
                            + JusticeBossTraceFormatting.DescribeCell(candidate),
                        JusticeBossTraceWriteMode.Critical);
                }

                bool skyfallerCanLand =
                    DropCellFinder.SkyfallerCanLandAt(
                        candidate,
                        map,
                        size,
                        faction);
                bool alreadyReserved = false;
                bool thickRoof = false;
                if (skyfallerCanLand)
                {
                    alreadyReserved = reserved.Contains(candidate);
                    if (!alreadyReserved)
                    {
                        thickRoof =
                            candidate.GetRoof(map)
                            == RoofDefOf.RoofRockThick;
                    }
                }

                if (trace)
                {
                    JusticeBossLaunchTracePatchManager.Write(
                        "DROP_CELL_VALIDATE_END",
                        detail
                            + " candidate="
                            + JusticeBossTraceFormatting.DescribeCell(candidate)
                            + " skyfallerCanLand=" + skyfallerCanLand
                            + " alreadyReserved=" + alreadyReserved
                            + " thickRoof=" + thickRoof,
                        JusticeBossTraceWriteMode.Buffered);
                }

                if (skyfallerCanLand
                    && !alreadyReserved
                    && !thickRoof)
                {
                    cell = candidate;
                    radiusUsed = safeRadius;
                    return true;
                }
            }

            cell = IntVec3.Invalid;
            radiusUsed = -1;
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
