using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor
{
    public static class JusticeBossSpawnUtility
    {
        public const float BossReplaceChance = 0.02f;

        public static List<PawnKindDef> BuildWaveComposition(int waveIndex, bool applyBossReplace = true)
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

        public static List<Pawn> SpawnWaveNear(
            Pawn justice,
            IntVec3 fallbackAnchor,
            List<PawnKindDef> kinds,
            ref Lord? sharedAssaultLord,
            out int bossReplacements)
        {
            bossReplacements = 0;
            List<Pawn> spawned = new List<Pawn>();
            Map? map = justice?.Map ?? justice?.MapHeld;
            if (justice == null || map == null || kinds == null || kinds.Count == 0)
            {
                return spawned;
            }

            Faction? faction = justice.Faction ?? Faction.OfMechanoids;
            IntVec3 center = justice.Spawned ? justice.Position : fallbackAnchor;
            if (!center.IsValid)
            {
                center = fallbackAnchor;
            }

            sharedAssaultLord = EnsureAssaultLord(map, faction, sharedAssaultLord);

            for (int i = 0; i < kinds.Count; i++)
            {
                PawnKindDef kind = kinds[i];
                if (kind == null)
                {
                    continue;
                }

                bool wantBoss = kind.isBoss && !JusticePawnUtility.IsBossJustice(kind);
                Pawn? pawn = TryGenerateAndSpawn(kind, faction, map, center);
                if (pawn == null && wantBoss)
                {
                    PawnKindDef? fallback =
                        JusticeBossMechPoolUtility.PickWeighted(
                            JusticeBossMechPoolUtility.BuildCombatPool());
                    if (fallback != null)
                    {
                        pawn = TryGenerateAndSpawn(fallback, faction, map, center);
                    }
                }
                else if (pawn != null && wantBoss)
                {
                    bossReplacements++;
                }

                if (pawn == null)
                {
                    continue;
                }

                spawned.Add(pawn);
                if (sharedAssaultLord != null)
                {
                    sharedAssaultLord.AddPawn(pawn);
                    pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced);
                    sharedAssaultLord.CurLordToil?.UpdateAllDuties();
                }
            }

            return spawned;
        }

        public static List<Pawn> SpawnGuardsNear(
            Map map,
            Faction faction,
            IntVec3 anchor,
            int count,
            out Lord? guardLord)
        {
            guardLord = null;
            List<Pawn> spawned = new List<Pawn>();
            List<PawnGenOption> combat = JusticeBossMechPoolUtility.BuildCombatPool();
            if (combat.Count == 0 || map == null)
            {
                return spawned;
            }

            LordJob_JusticeBossGuards job = new LordJob_JusticeBossGuards(faction, anchor);
            guardLord = LordMaker.MakeNewLord(faction, job, map);

            for (int i = 0; i < count; i++)
            {
                PawnKindDef? kind = JusticeBossMechPoolUtility.PickWeighted(combat);
                if (kind == null)
                {
                    continue;
                }

                Pawn? pawn = TryGenerateAndSpawn(kind, faction, map, anchor);
                if (pawn == null)
                {
                    continue;
                }

                spawned.Add(pawn);
                guardLord.AddPawn(pawn);
            }

            return spawned;
        }

        private static Lord EnsureAssaultLord(Map map, Faction? faction, Lord? existing)
        {
            if (existing != null
                && map.lordManager.lords.Contains(existing)
                && existing.LordJob is LordJob_AssaultColony)
            {
                return existing;
            }

            LordJob_AssaultColony assault = new LordJob_AssaultColony(
                faction,
                canKidnap: false,
                canTimeoutOrFlee: false,
                sappers: false,
                useAvoidGridSmart: true,
                canSteal: false);
            return LordMaker.MakeNewLord(faction, assault, map);
        }

        private static Pawn? TryGenerateAndSpawn(
            PawnKindDef kind,
            Faction? faction,
            Map map,
            IntVec3 center)
        {
            try
            {
                PawnGenerationRequest request = new PawnGenerationRequest(
                    kind,
                    faction,
                    PawnGenerationContext.NonPlayer,
                    forceGenerateNewPawn: true);
                Pawn pawn = PawnGenerator.GeneratePawn(request);
                if (!TryFindSpawnCell(map, center, out IntVec3 cell))
                {
                    pawn.Destroy();
                    return null;
                }

                GenSpawn.Spawn(pawn, cell, map);
                pawn.SetFaction(faction);
                TryPlaySpawnEffect(pawn);
                return pawn;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MAP JusticeBoss] Failed to spawn " + kind?.defName + ": " + e.Message);
                return null;
            }
        }

        private static bool TryFindSpawnCell(Map map, IntVec3 center, out IntVec3 cell)
        {
            for (int radius = 3; radius <= 24; radius += 2)
            {
                if (CellFinder.TryFindRandomCellNear(
                        center,
                        map,
                        radius,
                        c => c.Standable(map)
                            && c.GetEdifice(map) == null
                            && c.GetFirstPawn(map) == null
                            && !c.Fogged(map),
                        out cell))
                {
                    return true;
                }
            }

            cell = IntVec3.Invalid;
            return false;
        }

        private static void TryPlaySpawnEffect(Pawn pawn)
        {
            EffecterDef? effecter = DefDatabase<EffecterDef>.GetNamedSilentFail("Skip_ExitA");
            if (effecter != null && pawn.Spawned)
            {
                Effecter spawned = effecter.Spawn(pawn.Position, pawn.Map);
                spawned.Cleanup();
            }
        }
    }
}