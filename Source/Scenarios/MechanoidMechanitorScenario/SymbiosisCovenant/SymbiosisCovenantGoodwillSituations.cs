using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class GoodwillSituationWorker_SymbiosisTrust
        : GoodwillSituationWorker
    {
        public override int GetNaturalGoodwillOffset(Faction other)
        {
            // 第一阶段后，高信任阶段仅作为叙事与团结度计算依据，
            // 不再单独提供自然好感奖励。保留 Worker 与 Def 以兼容旧存档和引用。
            return 0;
        }
    }

    public sealed class GoodwillSituationWorker_SymbiosisMember
        : GoodwillSituationWorker
    {
        public override int GetNaturalGoodwillOffset(Faction other)
        {
            if (!GameComponent_SymbiosisCovenantState.IsActive || other == null)
            {
                return 0;
            }

            GameComponent_SymbiosisCovenantState? state =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            SymbiosisCovenantFactionRecord? record = state?.GetRecord(other);

            return record != null && record.CovenantMember
                ? def.naturalGoodwillOffset
                : 0;
        }
    }
}
