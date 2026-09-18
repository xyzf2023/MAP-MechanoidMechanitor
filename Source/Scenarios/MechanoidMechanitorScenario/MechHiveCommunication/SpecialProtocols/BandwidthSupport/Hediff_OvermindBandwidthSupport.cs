using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    // 动态阶段是实例数据，绝不修改共享 Def；配额与添加顺序仅由全局组件保存。
    public sealed class Hediff_OvermindBandwidthSupport : HediffWithComps
    {
        private int cachedAmount = -1;
        private HediffStage? cachedStage;
        private int notifiedAmount = -1;

        public override bool ShouldRemove => Scribe.mode == LoadSaveMode.Inactive
            && !MechanoidMechanitorPostLoadSafetyCoordinator.LoadInProgress
            && GameComponent_OvermindBandwidthSupport.Current is GameComponent_OvermindBandwidthSupport state
            && state.AllocatedTo(pawn) == 0;

        public bool Refresh()
        {
            int amount = GameComponent_OvermindBandwidthSupport.Current?.AllocatedTo(pawn) ?? 0;
            if (amount == notifiedAmount) return false;
            notifiedAmount = amount;
            cachedStage = null;
            pawn.health.Notify_HediffChanged(this);
            return true;
        }

        public override HediffStage CurStage
        {
            get
            {
                int amount = GameComponent_OvermindBandwidthSupport.Current?.AllocatedTo(pawn) ?? 0;
                if (cachedStage == null || amount != cachedAmount)
                {
                    cachedAmount = amount;
                    cachedStage = new HediffStage
                    {
                        statOffsets = new List<StatModifier>
                        {
                            new StatModifier { stat = StatDefOf.MechBandwidth, value = amount }
                        }
                    };
                }
                return cachedStage;
            }
        }
    }
}
