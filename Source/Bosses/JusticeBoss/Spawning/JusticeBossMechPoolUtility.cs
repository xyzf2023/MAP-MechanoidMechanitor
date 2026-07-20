using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class JusticeBossMechPoolUtility
    {
        private static bool loggedEmptyCombatPool;

        private static bool loggedEmptyHeavyPool;

        private static bool loggedEmptyBossPool;

        public static List<PawnGenOption> BuildCombatPool()
        {
            Dictionary<PawnKindDef, float> weights = new Dictionary<PawnKindDef, float>();
            FactionDef? mechFaction = FactionDefOf.Mechanoid;
            if (mechFaction?.pawnGroupMakers == null)
            {
                return GetFallbackCombatOptions();
            }

            foreach (PawnGroupMaker maker in mechFaction.pawnGroupMakers)
            {
                if (maker.kindDef != PawnGroupKindDefOf.Combat || maker.options == null)
                {
                    continue;
                }

                foreach (PawnGenOption option in maker.options)
                {
                    if (option?.kind == null || !IsValidCombatKind(option.kind))
                    {
                        continue;
                    }

                    PawnKindDef kind = option.kind;
                    if (weights.TryGetValue(kind, out float existing))
                    {
                        weights[kind] = existing + option.selectionWeight;
                    }
                    else
                    {
                        weights[kind] = option.selectionWeight;
                    }
                }
            }

            List<PawnGenOption> result = weights
                .Select(kv => new PawnGenOption
                {
                    kind = kv.Key,
                    selectionWeight = kv.Value,
                })
                .ToList();
            if (result.Count == 0)
            {
                if (!loggedEmptyCombatPool)
                {
                    loggedEmptyCombatPool = true;
                    Log.Error(
                        "[MAP JusticeBoss] Mechanoid Combat pawn group pool is empty; using fallback.");
                }

                return GetFallbackCombatOptions();
            }

            return result;
        }

        public static List<PawnGenOption> BuildHeavyPool(List<PawnGenOption> combatPool)
        {
            List<PawnGenOption> heavy = combatPool
                .Where(o => o?.kind != null
                    && o.kind.RaceProps.mechWeightClass == MechWeightClassDefOf.Heavy)
                .ToList();
            if (heavy.Count == 0)
            {
                if (!loggedEmptyHeavyPool)
                {
                    loggedEmptyHeavyPool = true;
                    Log.Warning(
                        "[MAP JusticeBoss] Heavy mech pool empty; falling back to combat pool.");
                }

                return combatPool;
            }

            return heavy;
        }

        public static List<PawnKindDef> BuildBossCandidatePool()
        {
            List<PawnKindDef> bosses = DefDatabase<PawnKindDef>.AllDefsListForReading
                .Where(IsValidBossCandidate)
                .ToList();
            if (bosses.Count == 0 && !loggedEmptyBossPool)
            {
                loggedEmptyBossPool = true;
                Log.Message("[MAP JusticeBoss] No boss candidates available for 2% replacement.");
            }

            return bosses;
        }

        public static PawnKindDef? PickWeighted(List<PawnGenOption> options)
        {
            if (options == null || options.Count == 0)
            {
                return null;
            }

            return options.RandomElementByWeight(o => o.selectionWeight).kind;
        }

        public static PawnKindDef? PickBossOrNull(List<PawnKindDef> bosses)
        {
            if (bosses == null || bosses.Count == 0)
            {
                return null;
            }

            return bosses.RandomElement();
        }

        private static bool IsValidCombatKind(PawnKindDef? kind)
        {
            if (kind?.race == null || !kind.RaceProps.IsMechanoid)
            {
                return false;
            }

            if (JusticePawnUtility.IsBossJustice(kind) || kind.isBoss)
            {
                return false;
            }

            return kind.combatPower > 0f;
        }

        private static bool IsValidBossCandidate(PawnKindDef kind)
        {
            if (kind?.race == null || !kind.RaceProps.IsMechanoid || !kind.isBoss)
            {
                return false;
            }

            return !JusticePawnUtility.IsBossJustice(kind);
        }

        private static List<PawnGenOption> GetFallbackCombatOptions()
        {
            string[] fallbackNames =
            {
                "Mech_Lancer",
                "Mech_Scyther",
                "Mech_CentipedeBlaster",
                "Mech_Pikeman",
            };

            List<PawnGenOption> list = new List<PawnGenOption>();
            foreach (string name in fallbackNames)
            {
                PawnKindDef? kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(name);
                if (kind != null && IsValidCombatKind(kind))
                {
                    list.Add(new PawnGenOption { kind = kind, selectionWeight = 1f });
                }
            }

            return list;
        }
    }
}