using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 当前监管者为新机械族保存的动态分配默认模板。
    /// 不包含目标引用、监管者引用、默认特化或任何运行时状态。
    /// </summary>
    public sealed class DataProcessingDynamicTargetDefaults : IExposable
    {
        public bool enabled = true;

        public int normalSteps;
        public int commonMaxSteps;

        public bool advancedMaxEnabled;

        public int generalMaxSteps;
        public int productionMaxSteps;
        public int fireControlMaxSteps;
        public int assaultMaxSteps;

        public int priority = 3;
        public int checkIntervalTicks = 600;

        public bool switchForWork = true;
        public bool switchForDraftedWeapon = true;
        public bool switchForCloseMelee = true;
        public bool applyUndraftedFallback = true;

        public void ExposeData()
        {
            Scribe_Values.Look(ref enabled, "enabled", true);

            Scribe_Values.Look(ref normalSteps, "normalSteps", 0);
            Scribe_Values.Look(ref commonMaxSteps, "commonMaxSteps", 0);

            Scribe_Values.Look(
                ref advancedMaxEnabled,
                "advancedMaxEnabled",
                false);

            Scribe_Values.Look(ref generalMaxSteps, "generalMaxSteps", 0);
            Scribe_Values.Look(ref productionMaxSteps, "productionMaxSteps", 0);
            Scribe_Values.Look(ref fireControlMaxSteps, "fireControlMaxSteps", 0);
            Scribe_Values.Look(ref assaultMaxSteps, "assaultMaxSteps", 0);

            Scribe_Values.Look(ref priority, "priority", 3);
            Scribe_Values.Look(
                ref checkIntervalTicks,
                "checkIntervalTicks",
                600);

            Scribe_Values.Look(ref switchForWork, "switchForWork", true);
            Scribe_Values.Look(
                ref switchForDraftedWeapon,
                "switchForDraftedWeapon",
                true);
            Scribe_Values.Look(
                ref switchForCloseMelee,
                "switchForCloseMelee",
                true);
            Scribe_Values.Look(
                ref applyUndraftedFallback,
                "applyUndraftedFallback",
                true);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Normalize();
            }
        }

        public void Normalize()
        {
            normalSteps = Mathf.Max(0, normalSteps);

            commonMaxSteps =
                Mathf.Max(normalSteps, commonMaxSteps);

            generalMaxSteps =
                Mathf.Max(normalSteps, generalMaxSteps);
            productionMaxSteps =
                Mathf.Max(normalSteps, productionMaxSteps);
            fireControlMaxSteps =
                Mathf.Max(normalSteps, fireControlMaxSteps);
            assaultMaxSteps =
                Mathf.Max(normalSteps, assaultMaxSteps);

            priority = Mathf.Clamp(priority, 1, 4);
            checkIntervalTicks = Mathf.Max(60, checkIntervalTicks);
        }

        /// <summary>
        /// 将模板写入目标配置。
        /// 严禁修改 record.overseer、record.target 或
        /// record.defaultSpecialization。
        /// </summary>
        public void ApplyTo(DataProcessingDynamicTargetRecord record)
        {
            if (record == null)
            {
                return;
            }

            Normalize();

            record.enabled = enabled;

            record.normalSteps = normalSteps;
            record.commonMaxSteps = commonMaxSteps;

            record.advancedMaxEnabled = advancedMaxEnabled;

            record.generalMaxSteps = generalMaxSteps;
            record.productionMaxSteps = productionMaxSteps;
            record.fireControlMaxSteps = fireControlMaxSteps;
            record.assaultMaxSteps = assaultMaxSteps;

            record.priority = priority;
            record.checkIntervalTicks = checkIntervalTicks;

            record.switchForWork = switchForWork;
            record.switchForDraftedWeapon = switchForDraftedWeapon;
            record.switchForCloseMelee = switchForCloseMelee;
            record.applyUndraftedFallback = applyUndraftedFallback;

            record.Normalize();
        }
    }
}
