using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 已销毁建筑的恢复凭据。只保存恢复所需数据，不深存已丢弃的建筑或复制源 Pawn。
    /// </summary>
    public sealed class MechBuildingEmergencyRestoreRecord : IExposable
    {
        public MechBuildingSourceState SourceState = new MechBuildingSourceState();
        public string? TransformationId;
        public int CarrierId;
        public Map? Map;
        public IntVec3 Position = IntVec3.Invalid;
        public Rot4 Rotation = Rot4.South;
        public bool HasDurabilitySnapshot;
        public float InitialHealthFraction;
        public float CurrentHealthFraction;

        // 重试节流属于运行态，读档后允许立即重试。
        internal int NextAttemptTick;
        public Pawn? SourcePawn => SourceState?.StoredSourcePawn;

        public void ExposeData()
        {
            Scribe_Deep.Look(ref SourceState, "sourceState");
            Scribe_Values.Look(ref TransformationId, "transformationId");
            Scribe_Values.Look(ref CarrierId, "carrierId");
            Scribe_References.Look(ref Map, "map");
            Scribe_Values.Look(ref Position, "position", IntVec3.Invalid);
            Scribe_Values.Look(ref Rotation, "rotation", Rot4.South);
            Scribe_Values.Look(ref HasDurabilitySnapshot, "hasDurabilitySnapshot");
            Scribe_Values.Look(ref InitialHealthFraction, "initialHealthFraction");
            Scribe_Values.Look(ref CurrentHealthFraction, "currentHealthFraction");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                SourceState ??= new MechBuildingSourceState();
                InitialHealthFraction = Mathf.Clamp01(InitialHealthFraction);
                CurrentHealthFraction = Mathf.Clamp01(CurrentHealthFraction);
            }
        }
    }
}
