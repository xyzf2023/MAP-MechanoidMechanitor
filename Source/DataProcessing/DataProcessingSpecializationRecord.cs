using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 轻量特化配置记录，仅保存 overseer/target 与特化选择，不保存分配档数。
    /// 由 GameComponent_DataProcessingAllocationRegistry 负责持久化与清理。
    /// </summary>
    public sealed class DataProcessingSpecializationRecord : IExposable
    {
        public Pawn? overseer;
        public Pawn? target;
        public DataProcessingSpecialization specialization =
            DataProcessingSpecialization.GeneralTuning;

        public DataProcessingSpecializationRecord()
        {
        }

        public DataProcessingSpecializationRecord(
            Pawn overseer,
            Pawn target,
            DataProcessingSpecialization specialization)
        {
            this.overseer = overseer;
            this.target = target;
            this.specialization =
                DataProcessingAllocationUtility.NormalizeSpecialization(specialization);
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref overseer, "overseer");
            Scribe_References.Look(ref target, "target");
            Scribe_Values.Look(
                ref specialization,
                "specialization",
                DataProcessingSpecialization.GeneralTuning);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                specialization =
                    DataProcessingAllocationUtility.NormalizeSpecialization(specialization);
            }
        }
    }
}
