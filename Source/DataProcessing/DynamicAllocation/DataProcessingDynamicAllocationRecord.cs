using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 监管者级动态分配开关与阈值记录。
    /// 由 GameComponent_DataProcessingAllocationRegistry 负责持久化与清理。
    /// </summary>
    public sealed class DataProcessingDynamicAllocationRecord : IExposable
    {
        public Pawn? overseer;
        public bool enabled;
        public int minConsciousnessPercent = 100;

        public DataProcessingDynamicAllocationRecord()
        {
        }

        public DataProcessingDynamicAllocationRecord(Pawn? overseer, bool enabled)
        {
            this.overseer = overseer;
            this.enabled = enabled;
            minConsciousnessPercent = 100;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref overseer, "overseer");
            Scribe_Values.Look(ref enabled, "enabled", false);
            Scribe_Values.Look(
                ref minConsciousnessPercent,
                "minConsciousnessPercent",
                100);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                minConsciousnessPercent =
                    Mathf.Clamp(minConsciousnessPercent, 55, 1000);
            }
        }
    }
}
