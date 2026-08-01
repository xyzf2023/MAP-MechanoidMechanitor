using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class SyntheticCompanionGizmoUtility
    {
        private const string LovinToggleLabelKey =
            "MAP_MechanoidMechanitor.Lover.Spouse.LovinWithSpouse.Toggle";
        private const string LovinToggleDescKey =
            "MAP_MechanoidMechanitor.Lover.Spouse.LovinWithSpouse.Toggle.Description";

        public static IEnumerable<Gizmo> GetGizmos(Pawn pawn)
        {
            if (pawn.Faction != Faction.OfPlayer
                || !MechanoidMechanitorCapabilityUtility.HasCapability(
                    pawn,
                    MechanoidMechanitorCapability.SyntheticSpouseInteraction)
                || !HasAnyDirectSpouse(pawn)
                || !SyntheticCompanionStateUtility.HasState(pawn))
            {
                yield break;
            }

            yield return new Command_Toggle
            {
                defaultLabel = LovinToggleLabelKey.Translate(),
                defaultDesc = LovinToggleDescKey.Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/LovinJob"),
                isActive = () =>
                    SyntheticCompanionStateUtility.IsLovinWithSpouseEnabled(pawn),
                toggleAction = () =>
                    SyntheticCompanionStateUtility.ToggleLovinWithSpouse(pawn),
                activateIfAmbiguous = true,
            };
        }

        private static bool HasAnyDirectSpouse(Pawn pawn)
        {
            if (pawn.relations == null)
            {
                return false;
            }

            List<DirectPawnRelation> relations = pawn.relations.DirectRelations;
            for (int i = 0; i < relations.Count; i++)
            {
                DirectPawnRelation relation = relations[i];
                if (relation.def == PawnRelationDefOf.Spouse && relation.otherPawn != null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
