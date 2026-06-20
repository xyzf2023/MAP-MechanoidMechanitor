using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class StatPart_JusticeSelfMechanitorOffset : StatPart
    {
        private static StatDef? workSpeedGlobalOffsetMechDef;
        private static bool missingWorkSpeedGlobalOffsetMechLogged;

        public override void TransformValue(StatRequest req, ref float val)
        {
            if (TryGetOffset(req, out float offset))
            {
                val += offset;
            }
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (TryGetOffset(req, out float offset))
            {
                return "MAP_MechanoidMechanitor.JusticeSelfMechanitorWorkSpeedFeedback"
                    .Translate(offset.ToStringPercent());
            }

            return null!;
        }

        private static bool TryGetOffset(StatRequest req, out float offset)
        {
            offset = 0f;

            if (!ModsConfig.BiotechActive
                || !req.HasThing
                || req.Thing is not Pawn pawn)
            {
                return false;
            }

            if (!JusticeMechanitorImplantUtility.IsJusticePawn(pawn))
            {
                return false;
            }

            StatDef? sourceStat = ResolveWorkSpeedGlobalOffsetMech();
            if (sourceStat == null)
            {
                return false;
            }

            offset = pawn.GetStatValue(sourceStat);
            return offset != 0f;
        }

        private static StatDef? ResolveWorkSpeedGlobalOffsetMech()
        {
            if (workSpeedGlobalOffsetMechDef != null)
            {
                return workSpeedGlobalOffsetMechDef;
            }

            workSpeedGlobalOffsetMechDef =
                DefDatabase<StatDef>.GetNamedSilentFail("WorkSpeedGlobalOffsetMech");

            if (workSpeedGlobalOffsetMechDef == null && !missingWorkSpeedGlobalOffsetMechLogged)
            {
                missingWorkSpeedGlobalOffsetMechLogged = true;
                Log.Error(
                    "[MAP_MechanoidMechanitor] StatPart_JusticeSelfMechanitorOffset: StatDef 'WorkSpeedGlobalOffsetMech' not found.");
            }

            return workSpeedGlobalOffsetMechDef;
        }
    }
}
