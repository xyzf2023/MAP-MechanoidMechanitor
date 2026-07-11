using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(RitualBehaviorWorker), nameof(RitualBehaviorWorker.CanStartRitualNow))]
    public static class GravshipRitualStart_CanStartRitualNow_Patch
    {
        public static bool Prefix(
            RitualBehaviorWorker __instance,
            TargetInfo target,
            Precept_Ritual ritual,
            Pawn selectedPawn,
            Dictionary<string, Pawn> forcedForRole,
            ref string? __result)
        {
            if (!ShouldHandle(__instance, ritual, target))
            {
                return true;
            }

            if (target.IsValid && target.Map.Tile.Valid)
            {
                PlanetLayerDef layerDef = target.Map.Tile.LayerDef;
                if ((!ritual.layerWhitelist.NullOrEmpty() && !ritual.layerWhitelist.Contains(layerDef))
                    || (!ritual.layerBlacklist.NullOrEmpty() && ritual.layerBlacklist.Contains(layerDef)))
                {
                    __result = "CantStartRitualLayer".Translate(
                        ritual.Label.Named("RITUAL"),
                        layerDef.gerundLabel.Named("GERUND"),
                        layerDef.LabelCap.Named("LAYER")).CapitalizeFirst();
                    return false;
                }
            }

            if (!ritual.allowOtherInstances)
            {
                foreach (LordJob_Ritual activeRitual in Find.IdeoManager.GetActiveRituals(target.Map))
                {
                    if (activeRitual.Ritual == ritual)
                    {
                        __result = "CantStartRitualAlreadyInProgress".Translate(ritual.Label).CapitalizeFirst();
                        return false;
                    }
                }
            }

            if (selectedPawn != null && ritual.behavior?.def.roles != null)
            {
                foreach (RitualRole role2 in ritual.behavior.def.roles)
                {
                    if (role2.defaultForSelectedColonist
                        && !role2.AppliesToPawn(selectedPawn, out string reason, target, null, null, ritual))
                    {
                        if (reason.NullOrEmpty())
                        {
                            __result = "CantStartRitualSelectedPawnCannotBeRole".Translate(
                                selectedPawn.Named("PAWN"),
                                role2.Label.Named("ROLE")).CapitalizeFirst();
                        }
                        else
                        {
                            __result = reason;
                        }

                        return false;
                    }
                }
            }

            if (target.IsValid)
            {
                List<Pawn> list = target.Map.mapPawns.FreeColonistsAndPrisonersSpawned.ToList();
                list.AddRange(target.Map.mapPawns.SpawnedColonyAnimals);

                foreach (Pawn pawn in target.Map.mapPawns.AllPawnsSpawned)
                {
                    if (IsGravshipJusticeCandidate(pawn))
                    {
                        list.AddUnique(pawn);
                    }
                }

                if (ritual.behavior?.def.roles is List<RitualRole> roleList && !roleList.NullOrEmpty())
                {
                    foreach (RitualRole role in roleList)
                    {
                        if (!role.required || role.substitutable)
                        {
                            continue;
                        }

                        IEnumerable<RitualRole> source = role.mergeId == null
                            ? Gen.YieldSingle(role)
                            : roleList.Where(r => r.mergeId == role.mergeId);

                        if (list.Count(p => role.AppliesToPawn(p, out _, target, null, null, ritual, skipReason: true)) < source.Count()
                            && (forcedForRole == null || !forcedForRole.ContainsKey(role.id)))
                        {
                            Precept? precept = ritual.ideo.PreceptsListForReading.FirstOrDefault(p => p.def == role.precept);
                            if (precept != null)
                            {
                                __result = "MessageNeedAssignedRoleToBeginRitual".Translate(
                                    role.missingDesc ?? Find.ActiveLanguageWorker.WithIndefiniteArticle(precept.LabelCap),
                                    ritual.Label);
                                return false;
                            }

                            if (!role.noCandidatesGizmoDesc.NullOrEmpty())
                            {
                                __result = role.noCandidatesGizmoDesc;
                                return false;
                            }

                            if (source.Count() == 1)
                            {
                                __result = "MessageNoRequiredRolePawnToBeginRitual".Translate(
                                    role.missingDesc ?? Find.ActiveLanguageWorker.WithIndefiniteArticle(role.Label),
                                    ritual.Label);
                                return false;
                            }

                            __result = "MessageNoRequiredRolePawnToBeginRitual".Translate(
                                source.Count() + " " + (role.missingDesc ?? Find.ActiveLanguageWorker.Pluralize(role.Label)),
                                ritual.Label);
                            return false;
                        }
                    }
                }
            }

            __result = null;
            return false;
        }

        private static bool ShouldHandle(
            RitualBehaviorWorker instance,
            Precept_Ritual ritual,
            TargetInfo target)
        {
            if (!ModsConfig.OdysseyActive)
            {
                return false;
            }

            if (instance is not RitualBehaviorWorker_GravshipLaunch)
            {
                return false;
            }

            if (ritual == null || ritual.def != PreceptDefOf.GravshipLaunch)
            {
                return false;
            }

            if (!target.IsValid || target.Thing == null)
            {
                return false;
            }

            return target.Thing.TryGetComp<CompPilotConsole>() != null;
        }

        private static bool IsGravshipJusticeCandidate(Pawn pawn)
        {
            if (!CompGravshipPilotUser.PawnCanUseGravshipPilotConsole(pawn))
            {
                return false;
            }

            if (!pawn.Spawned || pawn.Dead || pawn.Downed)
            {
                return false;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                return false;
            }

            if (pawn.GuestStatus == GuestStatus.Prisoner)
            {
                return false;
            }

            if (pawn.health?.capacities?.CapableOf(PawnCapacityDefOf.Moving) != true)
            {
                return false;
            }

            if (pawn.skills == null || pawn.skills.GetSkill(SkillDefOf.Intellectual).TotallyDisabled)
            {
                return false;
            }

            return true;
        }
    }
}
