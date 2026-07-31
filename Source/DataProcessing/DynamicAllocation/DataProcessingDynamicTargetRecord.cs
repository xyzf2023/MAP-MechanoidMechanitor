using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 单体目标动态配置记录：常态额度、默认模式、最高额度、优先级、检查间隔与规则开关。
    /// 由 GameComponent_DataProcessingAllocationRegistry 负责持久化、规范化与清理。
    /// </summary>
    public sealed class DataProcessingDynamicTargetRecord : IExposable
    {
        public Pawn? overseer;
        public Pawn? target;

        public bool enabled = true;

        // 常态额度，同时也是手动额度。
        public int normalSteps;

        // 未启用高级设置时使用的统一最高额度。
        public int commonMaxSteps;

        // 是否分别设置四种特化的最高额度。
        public bool advancedMaxEnabled;

        public int generalMaxSteps;
        public int productionMaxSteps;
        public int fireControlMaxSteps;
        public int assaultMaxSteps;

        // 1最高，4最低，默认3。
        public int priority = 3;

        // 默认600 tick，即10秒；最低60 tick，即1秒。
        public int checkIntervalTicks = 600;

        // 玩家设置的默认模式。
        public DataProcessingSpecialization defaultSpecialization =
            DataProcessingSpecialization.GeneralTuning;

        // 动态规则开关。
        public bool switchForWork = true;
        public bool switchForDraftedWeapon = true;
        public bool switchForCloseMelee = true;
        public bool applyUndraftedFallback = true;

        public DataProcessingDynamicTargetRecord()
        {
        }

        public DataProcessingDynamicTargetRecord(
            Pawn? overseer,
            Pawn? target,
            int initialSteps,
            DataProcessingSpecialization initialDefaultSpecialization)
        {
            this.overseer = overseer;
            this.target = target;
            normalSteps = Mathf.Max(0, initialSteps);
            commonMaxSteps = normalSteps;
            generalMaxSteps = normalSteps;
            productionMaxSteps = normalSteps;
            fireControlMaxSteps = normalSteps;
            assaultMaxSteps = normalSteps;
            priority = 3;
            checkIntervalTicks = 600;
            defaultSpecialization =
                DataProcessingAllocationUtility.NormalizeSpecialization(
                    initialDefaultSpecialization);
            enabled = true;
            advancedMaxEnabled = false;
            switchForWork = true;
            switchForDraftedWeapon = true;
            switchForCloseMelee = true;
            applyUndraftedFallback = true;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref overseer, "overseer");
            Scribe_References.Look(ref target, "target");
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref normalSteps, "normalSteps", 0);
            Scribe_Values.Look(ref commonMaxSteps, "commonMaxSteps", 0);
            Scribe_Values.Look(ref advancedMaxEnabled, "advancedMaxEnabled", false);
            Scribe_Values.Look(ref generalMaxSteps, "generalMaxSteps", 0);
            Scribe_Values.Look(ref productionMaxSteps, "productionMaxSteps", 0);
            Scribe_Values.Look(ref fireControlMaxSteps, "fireControlMaxSteps", 0);
            Scribe_Values.Look(ref assaultMaxSteps, "assaultMaxSteps", 0);
            Scribe_Values.Look(ref priority, "priority", 3);
            Scribe_Values.Look(ref checkIntervalTicks, "checkIntervalTicks", 600);
            Scribe_Values.Look(
                ref defaultSpecialization,
                "defaultSpecialization",
                DataProcessingSpecialization.GeneralTuning);
            Scribe_Values.Look(ref switchForWork, "switchForWork", true);
            Scribe_Values.Look(ref switchForDraftedWeapon, "switchForDraftedWeapon", true);
            Scribe_Values.Look(ref switchForCloseMelee, "switchForCloseMelee", true);
            Scribe_Values.Look(ref applyUndraftedFallback, "applyUndraftedFallback", true);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Normalize();
            }
        }

        public void Normalize()
        {
            normalSteps = Mathf.Max(0, normalSteps);
            commonMaxSteps = Mathf.Max(normalSteps, commonMaxSteps);
            generalMaxSteps = Mathf.Max(normalSteps, generalMaxSteps);
            productionMaxSteps = Mathf.Max(normalSteps, productionMaxSteps);
            fireControlMaxSteps = Mathf.Max(normalSteps, fireControlMaxSteps);
            assaultMaxSteps = Mathf.Max(normalSteps, assaultMaxSteps);
            priority = Mathf.Clamp(priority, 1, 4);
            checkIntervalTicks = Mathf.Max(60, checkIntervalTicks);
            defaultSpecialization =
                DataProcessingAllocationUtility.NormalizeSpecialization(
                    defaultSpecialization);
        }

        public int GetMaxStepsForSpecialization(
            DataProcessingSpecialization specialization)
        {
            Normalize();

            if (!advancedMaxEnabled)
            {
                return commonMaxSteps;
            }

            switch (DataProcessingAllocationUtility.NormalizeSpecialization(
                        specialization))
            {
                case DataProcessingSpecialization.GeneralTuning:
                    return generalMaxSteps;

                case DataProcessingSpecialization.ProductionCoordination:
                    return productionMaxSteps;

                case DataProcessingSpecialization.FireControlCalculation:
                    return fireControlMaxSteps;

                case DataProcessingSpecialization.AssaultProtocol:
                    return assaultMaxSteps;

                default:
                    return commonMaxSteps;
            }
        }
    }
}
