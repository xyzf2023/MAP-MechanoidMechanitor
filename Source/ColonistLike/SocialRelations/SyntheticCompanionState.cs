using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 仿生伴侣可变状态的统一抽象；当前由动态授权记录实现。
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
        /// <summary>
        /// 窄范围仿生伴侣身份查询：仅查询动态授权注册表。
        /// 不走完整能力汇总，供高频路径使用。
        /// </summary>
        public static bool IsSyntheticCompanion(Pawn? pawn)
        {
            return pawn != null
                && GameComponent_SyntheticCompanionRegistry.IsAuthorized(pawn);
        }

        public static bool TryGetState(
            Pawn? pawn,
            out ISyntheticCompanionState? state)
        {
            state = null;
            if (pawn == null)
            {
                return false;
            }

            return GameComponent_SyntheticCompanionRegistry.TryGetRecord(pawn, out state);
        }

        public static bool HasState(Pawn? pawn)
        {
            return IsSyntheticCompanion(pawn);
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
