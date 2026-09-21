using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 建筑形态的源 Pawn 身份与能源快照。建筑存活时由组件持有，
    /// 销毁后由持久恢复请求接管；两条恢复路径共用同一写回逻辑。
    /// </summary>
    public sealed class MechBuildingSourceState : IExposable
    {
        private Pawn? storedSourcePawn;
        private Faction? originalSourceFaction;
        private Pawn? originalOverseer;
        private int originalControlGroupIndex = -1;
        private bool sourceStateCaptured;
        private float storedEnergy;
        private float storedMaxEnergy;
        private bool energyCaptured;

        internal Pawn? StoredSourcePawn => storedSourcePawn;

        internal void CaptureSourceState(Pawn source)
        {
            storedSourcePawn = source;
            originalSourceFaction = source.Faction;
            originalOverseer = source.GetOverseer()
                ?? MAPOverseerRelationDirectionUtility.FindActualOverseer(source);
            originalControlGroupIndex =
                MechControlGroupPositionUtility.Capture(
                    originalOverseer,
                    source);
            sourceStateCaptured = true;
            CaptureEnergy(source);
        }

        internal void EnsureSourceStateForRecovery(Pawn source, Faction? carrierFaction = null)
        {
            storedSourcePawn ??= source;
            if (!sourceStateCaptured)
            {
                Faction? candidate = carrierFaction;
                Faction? player = Faction.OfPlayerSilentFail;
                if (candidate == null
                    || (player != null && candidate.HostileTo(player)))
                {
                    candidate = source.Faction;
                }

                if (player != null
                    && (candidate == null || candidate.HostileTo(player)))
                {
                    // 旧存档中的建筑转换只允许玩家安全阵营来源。
                    candidate = player;
                }

                originalSourceFaction = candidate;
                originalOverseer = source.GetOverseer()
                    ?? MAPOverseerRelationDirectionUtility.FindActualOverseer(
                        source);
                originalControlGroupIndex =
                    MechControlGroupPositionUtility.Capture(
                        originalOverseer,
                        source);
                sourceStateCaptured = true;
            }

            if (!energyCaptured)
            {
                // 旧存档无法追溯转换瞬间的电量，只能以当前真实 Pawn 值迁移；
                // 新转换始终在离开地图前精确捕获。
                CaptureEnergy(source);
            }
        }

        internal bool RestoreSourceIdentity(Pawn source)
        {
            EnsureSourceStateForRecovery(source);
            if (source.Faction != originalSourceFaction)
            {
                source.SetFactionDirect(originalSourceFaction);
            }

            Pawn? overseer = originalOverseer;
            GameComponent_AutonomousMechRegistry.NotifyPawnLifecycle(source);
            if (source.Dead
                || overseer == null
                || overseer.Dead
                || overseer.Destroyed
                || overseer.Discarded
                || AutonomousMechUtility.IsAutonomousMech(source)
                || MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(source))
            {
                return true;
            }

            return MechControlGroupPositionUtility.TryRestore(
                overseer,
                source,
                originalControlGroupIndex);
        }

        internal bool TryWriteBackEnergy(Pawn source)
        {
            EnsureSourceStateForRecovery(source);
            if (!energyCaptured || source.Dead)
            {
                return true;
            }

            Pawn_NeedsTracker? needs = source.needs;
            if (needs == null)
            {
                return false;
            }

            Need_MechEnergy? energy = needs.energy;
            if (energy == null)
            {
                needs.AddOrRemoveNeedsAsAppropriate();
                energy = needs.energy;
            }

            if (energy == null)
            {
                return false;
            }

            energy.CurLevel = Mathf.Clamp(storedEnergy, 0f, energy.MaxLevel);
            return true;
        }

        private void CaptureEnergy(Pawn source)
        {
            Need_MechEnergy? energy = source.needs?.energy;
            float fallbackMax = source.RaceProps?.maxMechEnergy ?? 100f;
            storedMaxEnergy = energy?.MaxLevel ?? fallbackMax;
            storedEnergy = energy?.CurLevel ?? storedMaxEnergy;
            if (storedMaxEnergy > 0f)
            {
                storedEnergy = Mathf.Clamp(storedEnergy, 0f, storedMaxEnergy);
            }

            energyCaptured = true;
        }

        internal MechBuildingSourceState CreateCopy()
        {
            // 只有值类型及权威对象引用，不复制 Pawn 或派系实例。
            return (MechBuildingSourceState)MemberwiseClone();
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref storedSourcePawn, "storedSourcePawn");
            Scribe_References.Look(
                ref originalSourceFaction,
                "originalSourceFaction");
            Scribe_References.Look(ref originalOverseer, "originalOverseer");
            Scribe_Values.Look(
                ref originalControlGroupIndex,
                "originalControlGroupIndex",
                -1);
            Scribe_Values.Look(
                ref sourceStateCaptured,
                "sourceStateCaptured",
                defaultValue: false);
            Scribe_Values.Look(ref storedEnergy, "storedEnergy");
            Scribe_Values.Look(ref storedMaxEnergy, "storedMaxEnergy");
            Scribe_Values.Look(
                ref energyCaptured,
                "energyCaptured",
                defaultValue: false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                storedEnergy = Mathf.Max(0f, storedEnergy);
                storedMaxEnergy = Mathf.Max(0f, storedMaxEnergy);
            }
        }
    }
}
