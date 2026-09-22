using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 仅供右侧工作缺口警报使用的机械族机械师能力判断。
    /// 不修改 FreeColonists 集合，也不在高频 Getter 中补建 Pawn 组件或工作设置。
    /// </summary>
    internal static class MechanoidMechanitorWorkAlertUtility
    {
        internal static bool HasAvailableMechanitorForWork(
            Map? map,
            WorkTypeDef? workType,
            SkillDef? requiredSkill = null,
            int minimumSkill = 0,
            bool requireWardenAuthorization = false)
        {
            if (map == null || workType == null)
            {
                return false;
            }

            List<Pawn> pawns = map.mapPawns.PawnsInFaction(Faction.OfPlayer);
            for (int i = 0; i < pawns.Count; i++)
            {
                if (IsAvailableMechanitorForWork(
                        pawns[i],
                        map,
                        workType,
                        requiredSkill,
                        minimumSkill,
                        requireWardenAuthorization))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool IsAvailableMechanitorForWork(
            Pawn? pawn,
            Map? map,
            WorkTypeDef? workType,
            SkillDef? requiredSkill = null,
            int minimumSkill = 0,
            bool requireWardenAuthorization = false)
        {
            if (pawn == null
                || map == null
                || workType == null
                || pawn.Destroyed
                || pawn.Dead
                || pawn.Downed
                || pawn.Faction != Faction.OfPlayer
                || (pawn.Spawned && pawn.Map != map)
                || (!pawn.Spawned && !pawn.BrieflyDespawned())
                || !MechanoidMechanitorCapabilityUtility.HasCapability(pawn, MechanoidMechanitorCapability.GeneralMechWork))
            {
                return false;
            }

            if (requireWardenAuthorization && !WardenWorkUtility.IsAuthorized(pawn))
            {
                return false;
            }

            if (pawn.workSettings == null
                || pawn.WorkTypeIsDisabled(workType)
                || !pawn.workSettings.WorkIsActive(workType))
            {
                return false;
            }

            if (requiredSkill == null)
            {
                return true;
            }

            if (ProductivityCoreUtility.HasActiveEffect(pawn))
            {
                return true;
            }

            if (MechanoidMechanitorSkillUtility.TryGetPreferredSkillLevel(
                    pawn,
                    requiredSkill,
                    out int preferredLevel))
            {
                return preferredLevel >= minimumSkill;
            }

            // 真实技能基础设施缺失时才按既有策略退回种族固定机械技能。
            return pawn.RaceProps.mechFixedSkillLevel >= minimumSkill;
        }
    }
}
