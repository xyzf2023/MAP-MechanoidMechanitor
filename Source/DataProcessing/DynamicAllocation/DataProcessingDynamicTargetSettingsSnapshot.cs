using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 动态分配设置复制模式。
    /// </summary>
    public enum DataProcessingTargetCopyMode
    {
        QuotaOnly,
        AllSettings
    }

    /// <summary>
    /// 运行时动态分配设置快照，用于同一监管者名下机械族之间的复制/粘贴。
    /// 不实现 IExposable，绝对不会写入存档。
    /// 不包含 defaultSpecialization、overseer、target、当前实际额度、
    /// 当前实际特化、顶置或任何运行时状态。
    /// </summary>
    public sealed class DataProcessingDynamicTargetSettingsSnapshot
    {
        public bool enabled;

        public int normalSteps;
        public int commonMaxSteps;

        public bool advancedMaxEnabled;

        public int generalMaxSteps;
        public int productionMaxSteps;
        public int fireControlMaxSteps;
        public int assaultMaxSteps;

        public int priority;
        public int checkIntervalTicks;

        public bool switchForWork;
        public bool switchForDraftedWeapon;
        public bool switchForCloseMelee;
        public bool applyUndraftedFallback;

        /// <summary>
        /// 从现有单体配置捕获可复制字段（不含默认特化）。
        /// </summary>
        public static DataProcessingDynamicTargetSettingsSnapshot Capture(
            DataProcessingDynamicTargetRecord record)
        {
            DataProcessingDynamicTargetSettingsSnapshot snapshot = new DataProcessingDynamicTargetSettingsSnapshot
            {
                enabled = record.enabled,

                normalSteps = record.normalSteps,
                commonMaxSteps = record.commonMaxSteps,

                advancedMaxEnabled = record.advancedMaxEnabled,

                generalMaxSteps = record.generalMaxSteps,
                productionMaxSteps = record.productionMaxSteps,
                fireControlMaxSteps = record.fireControlMaxSteps,
                assaultMaxSteps = record.assaultMaxSteps,

                priority = record.priority,
                checkIntervalTicks = record.checkIntervalTicks,

                switchForWork = record.switchForWork,
                switchForDraftedWeapon = record.switchForDraftedWeapon,
                switchForCloseMelee = record.switchForCloseMelee,
                applyUndraftedFallback = record.applyUndraftedFallback
            };

            return snapshot;
        }

        /// <summary>
        /// 将快照写入目标配置。
        /// 严禁修改 record.defaultSpecialization。
        /// </summary>
        public void ApplyTo(
            DataProcessingDynamicTargetRecord record,
            DataProcessingTargetCopyMode mode)
        {
            if (record == null)
            {
                return;
            }

            // 无论哪种模式，都复制完整额度块：
            // 包含高级独立上限开关与四种特化独立上限，否则来源启用了高级上限时复制结果会失效。
            record.normalSteps = normalSteps;
            record.commonMaxSteps = commonMaxSteps;

            record.advancedMaxEnabled = advancedMaxEnabled;

            record.generalMaxSteps = generalMaxSteps;
            record.productionMaxSteps = productionMaxSteps;
            record.fireControlMaxSteps = fireControlMaxSteps;
            record.assaultMaxSteps = assaultMaxSteps;

            if (mode == DataProcessingTargetCopyMode.AllSettings)
            {
                record.enabled = enabled;
                record.priority = priority;
                record.checkIntervalTicks = checkIntervalTicks;

                record.switchForWork = switchForWork;
                record.switchForDraftedWeapon = switchForDraftedWeapon;
                record.switchForCloseMelee = switchForCloseMelee;
                record.applyUndraftedFallback = applyUndraftedFallback;
            }

            record.Normalize();
        }
    }
}
