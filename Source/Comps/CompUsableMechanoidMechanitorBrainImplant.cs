using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_UsableMechanoidMechanitorBrainImplant
        : CompProperties_Usable
    {
        public RecipeDef? sourceRecipe;

        public CompProperties_UsableMechanoidMechanitorBrainImplant()
        {
            compClass = typeof(CompUsableMechanoidMechanitorBrainImplant);
        }
    }

    public sealed class CompUsableMechanoidMechanitorBrainImplant : CompUsableImplant
    {
        private CompProperties_UsableMechanoidMechanitorBrainImplant BrainImplantProps =>
            (CompProperties_UsableMechanoidMechanitorBrainImplant)props;

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn myPawn)
        {
            if (!CanDisplayUseOption(myPawn))
            {
                yield break;
            }

            foreach (FloatMenuOption option in base.CompFloatMenuOptions(myPawn))
            {
                yield return option;
            }
        }

        public override AcceptanceReport CanBeUsedBy(
            Pawn p,
            bool forced = false,
            bool ignoreReserveAndReachable = false)
        {
            if (!MechanoidMechanitorBrainImplantFeatureState.EnabledForSession)
            {
                return "MAP_MechanoidMechanitor.BrainImplant.FeatureDisabled".Translate();
            }

            if (!MechanoidMechanitorImplantUtility.HasImplantInstallationCapability(p))
            {
                return "MAP_MechanoidMechanitor.BrainImplant.MissingCapability".Translate();
            }

            AcceptanceReport vanillaReport =
                base.CanBeUsedBy(p, forced, ignoreReserveAndReachable);
            if (!vanillaReport.Accepted)
            {
                return vanillaReport;
            }

            return CheckRecipeCompatibility(p);
        }

        private static bool CanDisplayUseOption(Pawn pawn)
        {
            return MechanoidMechanitorBrainImplantFeatureState.EnabledForSession
                && MechanoidMechanitorImplantUtility.HasImplantInstallationCapability(pawn);
        }

        private AcceptanceReport CheckRecipeCompatibility(Pawn pawn)
        {
            RecipeDef? recipe = BrainImplantProps.sourceRecipe;
            if (recipe?.addsHediff == null)
            {
                return "MAP_MechanoidMechanitor.BrainImplant.InvalidRecipe".Translate();
            }

            BodyPartRecord? part =
                MechanoidMechanitorImplantUtility.GetPrimaryConsciousnessSourcePart(pawn);
            if (part == null || !pawn.health.hediffSet.GetNotMissingParts().Contains(part))
            {
                return "MAP_MechanoidMechanitor.BrainImplant.NoConsciousnessSource".Translate();
            }

            if (pawn.health.hediffSet.PartOrAnyAncestorHasDirectlyAddedParts(part))
            {
                return "MAP_MechanoidMechanitor.BrainImplant.PartReplaced".Translate();
            }

            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff existing = hediffs[i];
                if (existing.Part != part)
                {
                    continue;
                }

                if (existing.def == recipe.addsHediff)
                {
                    return "InstallImplantAlreadyInstalled".Translate();
                }

                if (!recipe.CompatibleWithHediff(existing.def))
                {
                    return "MAP_MechanoidMechanitor.BrainImplant.IncompatibleImplant"
                        .Translate(existing.LabelBase);
                }
            }

            return true;
        }
    }
}
