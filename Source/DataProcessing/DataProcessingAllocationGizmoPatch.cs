using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [StaticConstructorOnStartup]
    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.GetMechGizmos))]
    public static class Patch_MechanitorUtility_GetMechGizmos_DataProcessingAllocation
    {
        private const string LabelKey = "MAP_DataProcessingAllocation_Label";
        private const string DescriptionKey = "MAP_DataProcessingAllocation_Desc";

        private static readonly Texture2D DataProcessingAllocationIcon =
            ContentFinder<Texture2D>.Get("UI/MM_DataProcessingAllocation");

        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn mech)
        {
            foreach (Gizmo gizmo in __result)
            {
                yield return gizmo;
            }

            if (!ShouldShowDataProcessingAllocationGizmo(mech))
            {
                yield break;
            }

            yield return MakeDataProcessingAllocationCommand(mech);
        }

        private static bool ShouldShowDataProcessingAllocationGizmo(Pawn? mech)
        {
            return ModsConfig.BiotechActive
                && ResearchFeatureUnlockUtility.IsDataProcessingAllocationUnlocked()
                && mech != null
                && !mech.Dead
                && !mech.Destroyed
                && mech.RaceProps.IsMechanoid
                && mech.Faction != null
                && mech.Faction.IsPlayerSafe()
                && mech.mechanitor != null
                && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(mech);
        }

        private static Command_Action MakeDataProcessingAllocationCommand(Pawn overseer)
        {
            Pawn localOverseer = overseer;
            return new Command_Action
            {
                defaultLabel = LabelKey.Translate(),
                defaultDesc = DescriptionKey.Translate(),
                icon = DataProcessingAllocationIcon,
                action = delegate
                {
                    Find.WindowStack.Add(
                        new Dialog_DataProcessingAllocationMatrix(localOverseer));
                }
            };
        }
    }
}
