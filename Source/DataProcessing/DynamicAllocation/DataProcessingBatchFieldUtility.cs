namespace MAP_MechanoidMechanitor
{
    public enum DataProcessingBatchField { Normal, CommonMax, Enabled, Priority, Specialization, Interval, Melee, Draft, Work, Fallback, Separate, GeneralMax, ProductionMax, FireMax, AssaultMax }

    public static class DataProcessingBatchFieldUtility
    {
        public static string LabelKey(DataProcessingBatchField field)
        {
            switch (field)
            {
                case DataProcessingBatchField.Normal: return "NormalRequest";
                case DataProcessingBatchField.CommonMax: return "TaskMaximum";
                case DataProcessingBatchField.Enabled: return "TargetAutomatic";
                case DataProcessingBatchField.Priority: return "Priority";
                case DataProcessingBatchField.Specialization: return "DefaultMode";
                case DataProcessingBatchField.Interval: return "Interval";
                case DataProcessingBatchField.Melee: return "RuleMelee";
                case DataProcessingBatchField.Draft: return "RuleDraft";
                case DataProcessingBatchField.Work: return "RuleWork";
                case DataProcessingBatchField.Fallback: return "RuleFallback";
                case DataProcessingBatchField.Separate: return "SeparateMaximum";
                case DataProcessingBatchField.GeneralMax: return "SingleGeneralMax";
                case DataProcessingBatchField.ProductionMax: return "SingleProductionMax";
                case DataProcessingBatchField.FireMax: return "SingleFireMax";
                case DataProcessingBatchField.AssaultMax: return "SingleAssaultMax";
                default: throw new System.ArgumentOutOfRangeException(nameof(field));
            }
        }
        public static int Read(DataProcessingDynamicTargetRecord record, DataProcessingBatchField field)
        {
            switch (field)
            {
                case DataProcessingBatchField.Normal: return record.normalSteps;
                case DataProcessingBatchField.CommonMax: return record.commonMaxSteps;
                case DataProcessingBatchField.Enabled: return record.enabled ? 1 : 0;
                case DataProcessingBatchField.Priority: return record.priority;
                case DataProcessingBatchField.Specialization: return (int)record.defaultSpecialization;
                case DataProcessingBatchField.Interval: return record.checkIntervalTicks;
                case DataProcessingBatchField.Melee: return record.switchForCloseMelee ? 1 : 0;
                case DataProcessingBatchField.Draft: return record.switchForDraftedWeapon ? 1 : 0;
                case DataProcessingBatchField.Work: return record.switchForWork ? 1 : 0;
                case DataProcessingBatchField.Fallback: return record.applyUndraftedFallback ? 1 : 0;
                case DataProcessingBatchField.Separate: return record.advancedMaxEnabled ? 1 : 0;
                case DataProcessingBatchField.GeneralMax: return record.generalMaxSteps;
                case DataProcessingBatchField.ProductionMax: return record.productionMaxSteps;
                case DataProcessingBatchField.FireMax: return record.fireControlMaxSteps;
                case DataProcessingBatchField.AssaultMax: return record.assaultMaxSteps;
                default: throw new System.ArgumentOutOfRangeException(nameof(field));
            }
        }
        public static void Write(DataProcessingDynamicTargetRecord record, DataProcessingBatchField field, int value)
        {
            switch (field)
            {
                case DataProcessingBatchField.Normal: record.normalSteps = value; break;
                case DataProcessingBatchField.CommonMax: record.commonMaxSteps = value; break;
                case DataProcessingBatchField.Enabled: record.enabled = value != 0; break;
                case DataProcessingBatchField.Priority: record.priority = value; break;
                case DataProcessingBatchField.Specialization: record.defaultSpecialization = (DataProcessingSpecialization)value; break;
                case DataProcessingBatchField.Interval: record.checkIntervalTicks = value; break;
                case DataProcessingBatchField.Melee: record.switchForCloseMelee = value != 0; break;
                case DataProcessingBatchField.Draft: record.switchForDraftedWeapon = value != 0; break;
                case DataProcessingBatchField.Work: record.switchForWork = value != 0; break;
                case DataProcessingBatchField.Fallback: record.applyUndraftedFallback = value != 0; break;
                case DataProcessingBatchField.Separate: record.advancedMaxEnabled = value != 0; break;
                case DataProcessingBatchField.GeneralMax: record.generalMaxSteps = value; break;
                case DataProcessingBatchField.ProductionMax: record.productionMaxSteps = value; break;
                case DataProcessingBatchField.FireMax: record.fireControlMaxSteps = value; break;
                case DataProcessingBatchField.AssaultMax: record.assaultMaxSteps = value; break;
                default: throw new System.ArgumentOutOfRangeException(nameof(field));
            }
        }
    }
}

