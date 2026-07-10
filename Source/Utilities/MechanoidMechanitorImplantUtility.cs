using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class MechanoidMechanitorImplantUtility
    {
        private static readonly HashSet<string> bodiesMissingConsciousnessSourceLogged =
            new HashSet<string>();

        public static bool IsMechanoidMechanitor(Pawn? pawn)
        {
            return MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn);
        }

        public static bool IsSupportedMechanitorImplant(Thing? implant)
        {
            if (implant is not ThingWithComps thingWithComps)
            {
                return false;
            }

            return thingWithComps.TryGetComp<CompMechanoidMechanitorImplantMarker>() != null
                && thingWithComps.TryGetComp<CompUsableImplant>() != null
                && thingWithComps.TryGetComp<CompUseEffect_InstallImplant>() != null;
        }

        public static bool CanUseMechanitorImplant(Pawn? pawn, Thing? implant)
        {
            return IsMechanoidMechanitor(pawn) && IsSupportedMechanitorImplant(implant);
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

            BodyDef? body = pawn.RaceProps?.body;
            if (body == null)
            {
                return requestedPart;
            }

            List<BodyPartRecord> consciousnessSources =
                body.GetPartsWithTag(BodyPartTagDefOf.ConsciousnessSource);
            if (!consciousnessSources.NullOrEmpty())
            {
                return consciousnessSources[0].def;
            }

            if (bodiesMissingConsciousnessSourceLogged.Add(body.defName))
            {
                Log.Warning(
                    "[MAP-机械族机械师] MechanoidMechanitorImplantUtility：" +
                    $"身体定义 {body.defName} 不包含 ConsciousnessSource 部位，" +
                    "机械师植入体将回退使用原始身体部位要求。");
            }

            return requestedPart;
        }
    }
}
