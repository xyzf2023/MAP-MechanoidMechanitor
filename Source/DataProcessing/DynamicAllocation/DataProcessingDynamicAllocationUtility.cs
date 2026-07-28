using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 动态分配工具：依据机械体类型自动决定其应使用的“基础特化”。
    /// 开启动态分配的监管者会按此结果覆盖其名下各目标的手动特化选择（不改变分配档数）。
    /// </summary>
    public static class DataProcessingDynamicAllocationUtility
    {
        private const string HermitDefName = "MAP_Mech_Hermit";
        private const string LoverDefName = "MAP_Mech_Lover";

        // 近战触及范围上限；高于此射程的攻击视为远程。
        private const float MeleeRangeThreshold = 1.42f;

        /// <summary>
        /// 依据机械体类型决定基础特化。
        /// 优先级：强制通用调谐（正义/机械师/隐者/恋人）→ 具备作业能力 → 远程攻击 → 近战战斗 → 通用调谐。
        /// </summary>
        public static DataProcessingSpecialization DetermineBaseSpecialization(Pawn? target)
        {
            if (target == null)
            {
                return DataProcessingSpecialization.GeneralTuning;
            }

            if (ShouldForceGeneralTuning(target))
            {
                return DataProcessingSpecialization.GeneralTuning;
            }

            if (HasEnabledMechWorkTypes(target))
            {
                return DataProcessingSpecialization.ProductionCoordination;
            }

            if (HasRangedAttackVerb(target))
            {
                return DataProcessingSpecialization.FireControlCalculation;
            }

            if (IsMeleeCombatMech(target))
            {
                return DataProcessingSpecialization.AssaultProtocol;
            }

            return DataProcessingSpecialization.GeneralTuning;
        }

        /// <summary>
        /// 以下单位不受动态分配自动归类影响，始终保留通用调谐：
        /// 正义机械体、机械族机械师、隐者、恋人。
        /// </summary>
        private static bool ShouldForceGeneralTuning(Pawn target)
        {
            if (JusticePawnUtility.IsAnyJusticeVariant(target))
            {
                return true;
            }

            if (MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(target))
            {
                return true;
            }

            string? defName = target.def?.defName;
            if (defName == HermitDefName || defName == LoverDefName)
            {
                return true;
            }

            return false;
        }

        private static bool HasEnabledMechWorkTypes(Pawn target)
        {
            List<WorkTypeDef>? workTypes = target.RaceProps.mechEnabledWorkTypes;
            return workTypes != null && workTypes.Count > 0;
        }

        private static bool HasRangedAttackVerb(Pawn target)
        {
            if (HasRangedAttackVerbIn(target.verbTracker?.AllVerbs))
            {
                return true;
            }

            if (target.equipment != null
                && HasRangedAttackVerbIn(target.equipment.AllEquipmentVerbs))
            {
                return true;
            }

            return false;
        }

        private static bool HasRangedAttackVerbIn(IEnumerable<Verb>? verbs)
        {
            if (verbs == null)
            {
                return false;
            }

            foreach (Verb verb in verbs)
            {
                if (verb == null
                    || verb.verbProps == null
                    || !verb.verbProps.violent
                    || verb.IsMeleeAttack)
                {
                    continue;
                }

                if (verb.verbProps.range > MeleeRangeThreshold)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsMeleeCombatMech(Pawn target)
        {
            if (target.kindDef != null
                && (target.kindDef.canMeleeAttack || target.kindDef.isFighter))
            {
                return true;
            }

            if (HasMeleeAttackVerb(target.verbTracker?.AllVerbs))
            {
                return true;
            }

            if (target.equipment != null
                && HasMeleeAttackVerb(target.equipment.AllEquipmentVerbs))
            {
                return true;
            }

            return false;
        }

        private static bool HasMeleeAttackVerb(IEnumerable<Verb>? verbs)
        {
            if (verbs == null)
            {
                return false;
            }

            foreach (Verb verb in verbs)
            {
                if (verb != null
                    && verb.verbProps != null
                    && verb.verbProps.violent
                    && verb.IsMeleeAttack)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
