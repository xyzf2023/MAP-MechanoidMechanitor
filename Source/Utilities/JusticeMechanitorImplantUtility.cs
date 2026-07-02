using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class JusticeMechanitorImplantUtility
    {
        private static BodyPartDef? artificialBrainDef;
        private static bool missingArtificialBrainLogged;

        public static bool IsJusticePawn(Pawn? pawn)
        {
            return MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn);
        }

        public static bool IsSupportedMechanitorImplant(Thing? implant)
        {
            if (implant is not ThingWithComps thingWithComps)
            {
                return false;
            }

            return thingWithComps.TryGetComp<CompJusticeMechanitorImplantMarker>() != null
                && thingWithComps.TryGetComp<CompUsableImplant>() != null
                && thingWithComps.TryGetComp<CompUseEffect_InstallImplant>() != null;
        }

        public static bool CanUseMechanitorImplant(Pawn? pawn, Thing? implant)
        {
            return IsJusticePawn(pawn) && IsSupportedMechanitorImplant(implant);
        }

        public static HediffDef? ResolveRequiredHediff(
            HediffDef? required,
            CompUsable comp,
            Pawn pawn)
        {
            if (required == null)
            {
                return null;
            }

            if (!CanUseMechanitorImplant(pawn, comp.parent))
            {
                return required;
            }

            return required == HediffDefOf.MechlinkImplant ? null : required;
        }

        public static bool ResolveAllowNonColonists(
            bool allowNonColonists,
            CompUseEffect_InstallImplant comp,
            Pawn pawn)
        {
            return allowNonColonists || CanUseMechanitorImplant(pawn, comp.parent);
        }

        public static bool ResolveIsFleshOrJustice(
            bool isFlesh,
            CompUsable comp,
            Pawn pawn)
        {
            return isFlesh || CanUseMechanitorImplant(pawn, comp.parent);
        }

        public static BodyPartDef? ResolveImplantBodyPart(
            BodyPartDef? requestedPart,
            CompUseEffect_InstallImplant comp,
            Pawn pawn)
        {
            if (!CanUseMechanitorImplant(pawn, comp.parent))
            {
                return requestedPart;
            }

            if (requestedPart == null || requestedPart.defName != "Brain")
            {
                return requestedPart;
            }

            BodyPartDef? artificialBrain = GetArtificialBrainDef();
            if (artificialBrain == null)
            {
                return requestedPart;
            }

            return artificialBrain;
        }

        private static BodyPartDef? GetArtificialBrainDef()
        {
            artificialBrainDef ??= DefDatabase<BodyPartDef>.GetNamedSilentFail("ArtificialBrain");
            if (artificialBrainDef == null && !missingArtificialBrainLogged)
            {
                missingArtificialBrainLogged = true;
                Log.Error(
                    "[MAP-机械族机械师] JusticeMechanitorImplantUtility：未找到 BodyPartDef 'ArtificialBrain'，回退使用原始身体部位。");
            }

            return artificialBrainDef;
        }
    }
}
