using System;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class DormantJusticeActivationUtility
    {
        public static bool TryActivate(CompDormantJustice? dormantComp, out Pawn? generatedJustice)
        {
            generatedJustice = null;
            if (dormantComp == null)
            {
                return false;
            }

            if (dormantComp.parent is not Building building
                || building.Destroyed
                || !building.Spawned
                || building.Map == null)
            {
                return false;
            }

            if (dormantComp.IsActivationInProgress)
            {
                return false;
            }

            dormantComp.PrepareForActivationAttempt();
            dormantComp.BeginActivationAttempt();

            try
            {
                Map map = building.Map;
                IntVec3 preferredCell = building.OccupiedRect().CenterCell;

                PawnKindDef? justiceKind = JusticeScenarioUtility.JusticePawnKind;
                if (justiceKind == null)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 未启动正义启动失败：未找到 MAP_Mech_Justice " +
                        $"PawnKindDef，building={building.LabelShort}（{building.ThingID}）。");
                    return false;
                }

                Pawn pawn = PawnGenerator.GeneratePawn(BuildJusticeGenerationRequest(justiceKind));
                if (pawn == null || pawn.Destroyed)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 未启动正义启动失败：Pawn 生成失败，" +
                        $"building={building.LabelShort}（{building.ThingID}）。");
                    return false;
                }

                if (!EnsureJusticePawnReady(pawn))
                {
                    Log.Error(
                        "[MAP-机械族机械师] 未启动正义启动失败：机械师记录或角色状态未就绪，" +
                        $"building={building.LabelShort}（{building.ThingID}），" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}）。");
                    pawn.Destroy();
                    return false;
                }

                building.Destroy(DestroyMode.Vanish);

                IntVec3 spawnCell = FindSpawnCell(map, preferredCell, pawn);
                if (!spawnCell.IsValid)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 未启动正义启动失败：未找到有效生成格，" +
                        $"map={map}，preferred={preferredCell}，" +
                        $"pawn={pawn.LabelShort}（{pawn.ThingID}）。");
                    pawn.Destroy();
                    return false;
                }

                GenSpawn.Spawn(pawn, spawnCell, map);
                generatedJustice = pawn;
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 未启动正义启动异常：" +
                    $"{dormantComp.parent?.LabelShort ?? "null"} " +
                    $"（{dormantComp.parent?.ThingID ?? "null"}）：{ex}");
                return false;
            }
            finally
            {
                dormantComp.EndActivationAttempt();
            }
        }

        private static PawnGenerationRequest BuildJusticeGenerationRequest(PawnKindDef kind)
        {
            return new PawnGenerationRequest(
                kind,
                Faction.OfPlayer,
                PawnGenerationContext.NonPlayer,
                null,
                forceGenerateNewPawn: false,
                allowDead: false,
                allowDowned: false,
                canGeneratePawnRelations: true,
                mustBeCapableOfViolence: false,
                1f,
                forceAddFreeWarmLayerIfNeeded: false,
                allowGay: true,
                allowPregnant: false,
                allowFood: true,
                allowAddictions: true,
                inhabitant: false,
                certainlyBeenInCryptosleep: false,
                forceRedressWorldPawnIfFormerColonist: false,
                worldPawnFactionDoesntMatter: false,
                0f,
                0f,
                null,
                1f,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                forceNoIdeo: false,
                forceNoBackstory: false,
                forbidAnyTitle: false,
                forceDead: false,
                null,
                null,
                null,
                null,
                null,
                0f,
                developmentalStages: DevelopmentalStage.Adult);
        }

        private static bool EnsureJusticePawnReady(Pawn pawn)
        {
            if (pawn.Faction != Faction.OfPlayer)
            {
                pawn.SetFaction(Faction.OfPlayer);
            }

            GameComponent_MechanoidMechanitorRegistry.EnsureNativeMechanitorRecord(pawn);
            MechanoidMechanitorRoleUtility.EnsureRoleState(pawn);
            MAPMechanitorNodeLifecycleUtility.EnsureBasicTrackers(pawn);

            return !pawn.Destroyed
                && !pawn.Dead
                && pawn.RaceProps.IsMechanoid
                && pawn.Faction != null
                && pawn.Faction.IsPlayerSafe()
                && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn)
                && GameComponent_MechanoidMechanitorRegistry.TryGetNativeMechanitorRecord(
                    pawn,
                    out _)
                && pawn.relations != null
                && pawn.mechanitor != null
                && MechanitorUtility.IsMechanitor(pawn);
        }

        private static IntVec3 FindSpawnCell(Map map, IntVec3 preferred, Pawn pawn)
        {
            if (GenSpawn.CanSpawnAt(pawn.def, preferred, map))
            {
                return preferred;
            }

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(preferred, 4f, true))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

                if (GenSpawn.CanSpawnAt(pawn.def, cell, map))
                {
                    return cell;
                }
            }

            if (CellFinder.TryRandomClosewalkCellNear(
                    preferred,
                    map,
                    6,
                    out IntVec3 closewalk,
                    c => GenSpawn.CanSpawnAt(pawn.def, c, map)))
            {
                return closewalk;
            }

            return IntVec3.Invalid;
        }
    }
}
