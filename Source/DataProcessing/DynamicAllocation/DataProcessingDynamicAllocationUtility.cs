using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 动态分配工具：提供运行时状态评估所需的判定（近战接战、当前攻击 Verb、
    /// 远程/近战战斗能力、工作型机械体），以及首次创建单体配置时决定初始默认模式。
    /// 仅用于状态识别与默认模式推断，不负责实际档数分配与特化覆盖（由注册表按计划管理）。
    /// </summary>
    public static class DataProcessingDynamicAllocationUtility
    {
        private const string HermitDefName = "MAP_Mech_Hermit";
        private const string LoverDefName = "MAP_Mech_Lover";

        // 近战触及范围上限；高于此射程的攻击视为远程。
        private const float MeleeRangeThreshold = 1.42f;

        /// <summary>
        /// 仅在“目标没有任何动态配置记录、首次创建单体配置”时调用，决定初始默认模式。
        /// 不得在周期动态分配、LoadedGame 或开关动态分配时覆盖已有玩家选择。
        /// </summary>
        public static DataProcessingSpecialization DetermineInitialDefaultSpecialization(Pawn? target)
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
        /// 当前实战使用的攻击 Verb（优先当前 Stance_Busy 实际 Verb → 当前 Job.verbToUse）。
        /// 用于判断征召时远程/近战。
        /// </summary>
        public static Verb? GetCurrentAttackVerb(Pawn? target)
        {
            if (target == null || target.stances == null)
            {
                return null;
            }

            if (target.stances.curStance is Stance_Busy busy && busy.verb != null)
            {
                return busy.verb;
            }

            Job? curJob = target.CurJob;
            if (curJob != null && curJob.verbToUse != null)
            {
                return curJob.verbToUse;
            }

            return null;
        }

        /// <summary>
        /// 是否正在执行近战接战。固定优先级最高的状态。
        /// 仅使用原版已有状态，不做全图或大半径搜索。
        /// </summary>
        public static bool IsConfirmedCloseMeleeEngagement(
            Pawn? target,
            int checkIntervalTicks)
        {
            if (target == null || target.Dead || target.Destroyed)
            {
                return false;
            }

            if (IsPawnCurrentlyMakingMeleeAttack(target))
            {
                return true;
            }

            if (HasValidRecordedMeleeThreat(target, checkIntervalTicks))
            {
                return true;
            }

            return HasAdjacentEnemyActivelyMeleeAttackingTarget(target);
        }

        private static bool IsPawnCurrentlyMakingMeleeAttack(Pawn? target)
        {
            if (target == null || target.stances == null)
            {
                return false;
            }

            if (target.stances.curStance is Stance_Busy busy
                && busy.verb != null
                && busy.verb.verbProps != null
                && busy.verb.verbProps.IsMeleeAttack)
            {
                return true;
            }

            if (target.jobs?.curDriver is JobDriver_AttackMelee)
            {
                return true;
            }

            return false;
        }

        private static bool HasValidRecordedMeleeThreat(
            Pawn? target,
            int checkIntervalTicks)
        {
            if (target?.mindState == null)
            {
                return false;
            }

            Pawn? threat = target.mindState.meleeThreat;
            if (threat == null)
            {
                return false;
            }

            int memoryTicks = Mathf.Max(400, checkIntervalTicks + 60);
            if (Find.TickManager.TicksGame
                > target.mindState.lastMeleeThreatHarmTick + memoryTicks)
            {
                return false;
            }

            if (threat.Dead || threat.Destroyed || threat.Downed)
            {
                return false;
            }

            if (target.Map != threat.Map || !target.Spawned || !threat.Spawned)
            {
                return false;
            }

            if (target.HostileTo(threat))
            {
                // 一个机械族误把友方记为威胁时应避免误判；原版 MeleeThreatStillThreat 已处理敌意。
            }
            else
            {
                return false;
            }

            if ((float)(target.Position - threat.Position).LengthHorizontalSquared > 9f)
            {
                return false;
            }

            if (!GenSight.LineOfSight(target.Position, threat.Position, target.Map))
            {
                return false;
            }

            return true;
        }

        private static bool HasAdjacentEnemyActivelyMeleeAttackingTarget(Pawn? target)
        {
            if (target?.Map == null || !target.Spawned)
            {
                return false;
            }

            IntVec3 center = target.Position;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    IntVec3 cell = center + new IntVec3(dx, 0, dz);
                    if (!cell.InBounds(target.Map))
                    {
                        continue;
                    }

                    List<Thing> things = target.Map.thingGrid.ThingsListAtFast(cell);
                    for (int i = 0; i < things.Count; i++)
                    {
                        if (things[i] is not Pawn enemy || enemy == target)
                        {
                            continue;
                        }

                        if (enemy.Dead || enemy.Destroyed || enemy.Downed)
                        {
                            continue;
                        }

                        if (!enemy.HostileTo(target))
                        {
                            continue;
                        }

                        if (IsPawnActivelyMeleeAttacking(enemy, target))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool IsPawnActivelyMeleeAttacking(Pawn? enemy, Pawn? focus)
        {
            if (enemy == null || focus == null || enemy.stances == null)
            {
                return false;
            }

            if (enemy.stances.curStance is Stance_Busy busy
                && busy.verb != null
                && busy.verb.verbProps != null
                && busy.verb.verbProps.IsMeleeAttack)
            {
                if (busy.focusTarg.IsValid && busy.focusTarg.Thing == focus)
                {
                    return true;
                }
            }

            if (enemy.jobs?.curDriver is JobDriver_AttackMelee attackMeleeDriver
                && attackMeleeDriver.job?.targetA.Thing == focus)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 以下单位不受动态分配自动归类影响，始终保留通用调谐：
        /// 正义机械体、机械族机械师、隐者、恋人。
        /// 该规则同时作用于初始默认模式（DetermineInitialDefaultSpecialization）
        /// 与运行时节点的重新评估（EvaluateTarget 的 fallback 分支）。
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

        public static bool HasEnabledMechWorkTypes(Pawn target)
        {
            List<WorkTypeDef>? workTypes = target.RaceProps.mechEnabledWorkTypes;
            return workTypes != null && workTypes.Count > 0;
        }

        public static bool HasRangedAttackVerb(Pawn target)
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
                    || !verb.verbProps.ai_IsWeapon
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

        public static bool IsMeleeCombatMech(Pawn target)
        {
            PawnKindDef? kindDef = target.kindDef;
            if (kindDef == null
                || !kindDef.isFighter
                || !kindDef.canMeleeAttack)
            {
                return false;
            }

            if (HasMeleeAttackVerb(target.verbTracker?.AllVerbs))
            {
                return true;
            }

            return target.equipment != null
                && HasMeleeAttackVerb(
                    target.equipment.AllEquipmentVerbs);
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
                    && verb.verbProps.ai_IsWeapon
                    && verb.IsMeleeAttack)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
