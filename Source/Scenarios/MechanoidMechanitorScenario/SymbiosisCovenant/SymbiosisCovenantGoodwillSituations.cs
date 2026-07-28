using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class GoodwillSituationWorker_SymbiosisTrust
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

            return record != null && record.Trust >= 50
                ? def.naturalGoodwillOffset
                : 0;
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
