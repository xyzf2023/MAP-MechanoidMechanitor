using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 仿生伴侣可变状态的统一抽象；业务代码不依赖状态的具体存储来源。
    /// </summary>
    public interface ISyntheticCompanionState
    {
        bool LovinWithSpouseEnabled { get; }

        SyntheticPregnancyApproach PregnancyApproach { get; }

        void ToggleLovinWithSpouse();

        void DisableLovinWithSpouse();

        void SetPregnancyApproach(SyntheticPregnancyApproach approach);

        void ResetPregnancyApproachToAvoid();
    }

    public static class SyntheticCompanionStateUtility
    {
        public static bool TryGetState(
            Pawn? pawn,
            out ISyntheticCompanionState? state)
        {
            state = null;
            if (pawn == null)
            {
                return false;
            }

            // 真实组件优先；同时存在动态授权时也以 Comp 为状态来源。
            CompSyntheticCompanionUser? comp = pawn.GetComp<CompSyntheticCompanionUser>();
            if (comp != null)
            {
                state = comp;
                return true;
            }

            return GameComponent_SyntheticCompanionRegistry.TryGetRecord(pawn, out state);
        }

        public static bool HasState(Pawn? pawn)
        {
            return TryGetState(pawn, out _);
        }

        public static bool IsLovinWithSpouseEnabled(Pawn? pawn)
        {
            return TryGetState(pawn, out ISyntheticCompanionState? state)
                && state!.LovinWithSpouseEnabled;
        }

        public static void ToggleLovinWithSpouse(Pawn? pawn)
        {
            if (TryGetState(pawn, out ISyntheticCompanionState? state))
            {
                state!.ToggleLovinWithSpouse();
            }
        }

        public static void DisableLovinWithSpouse(Pawn? pawn)
        {
            if (TryGetState(pawn, out ISyntheticCompanionState? state))
            {
                state!.DisableLovinWithSpouse();
            }
        }

        public static SyntheticPregnancyApproach GetPregnancyApproach(Pawn? pawn)
        {
            return TryGetState(pawn, out ISyntheticCompanionState? state)
                ? state!.PregnancyApproach
                : SyntheticPregnancyApproach.AvoidPregnancy;
        }

        public static void SetPregnancyApproach(
            Pawn? pawn,
            SyntheticPregnancyApproach approach)
        {
            if (TryGetState(pawn, out ISyntheticCompanionState? state))
            {
                state!.SetPregnancyApproach(approach);
            }
        }

        public static void ResetPregnancyApproachToAvoid(Pawn? pawn)
        {
            if (TryGetState(pawn, out ISyntheticCompanionState? state))
            {
                state!.ResetPregnancyApproachToAvoid();
            }
        }
    }
}
