using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class StatPart_MechanoidMechanitorSelfWorkSpeedOffset : StatPart
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
                return "MAP_MechanoidMechanitor.MechanoidMechanitorSelfWorkSpeedFeedback"
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

            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
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
                    "[MAP-机械族机械师] StatPart_MechanoidMechanitorSelfWorkSpeedOffset：未找到 StatDef 'WorkSpeedGlobalOffsetMech'。");
            }

            return workSpeedGlobalOffsetMechDef;
        }
    }
}
