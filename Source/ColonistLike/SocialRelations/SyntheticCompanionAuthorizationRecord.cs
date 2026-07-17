using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 动态仿生伴侣授权记录：独立于真实 Comp，保存 Lovin 开关与生育方式。
    /// </summary>
    public sealed class SyntheticCompanionAuthorizationRecord
        : IExposable, ISyntheticCompanionState
    {
        private Pawn? pawn;
        private bool lovinWithSpouseEnabled;
        private SyntheticPregnancyApproach pregnancyApproach =
            SyntheticPregnancyApproach.AvoidPregnancy;

        public Pawn? Pawn => pawn;

        public bool LovinWithSpouseEnabled => lovinWithSpouseEnabled;

        public SyntheticPregnancyApproach PregnancyApproach => pregnancyApproach;

        public SyntheticCompanionAuthorizationRecord()
        {
        }

        public SyntheticCompanionAuthorizationRecord(Pawn authorizedPawn)
        {
            pawn = authorizedPawn;
            lovinWithSpouseEnabled = false;
            pregnancyApproach = SyntheticPregnancyApproach.AvoidPregnancy;
        }

        public void ToggleLovinWithSpouse()
        {
            lovinWithSpouseEnabled = !lovinWithSpouseEnabled;
        }

        public void DisableLovinWithSpouse()
        {
            lovinWithSpouseEnabled = false;
        }

        public void SetPregnancyApproach(SyntheticPregnancyApproach approach)
        {
            pregnancyApproach = approach;
        }

        public void ResetPregnancyApproachToAvoid()
        {
            pregnancyApproach = SyntheticPregnancyApproach.AvoidPregnancy;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "authorizedPawn");
            Scribe_Values.Look(
                ref lovinWithSpouseEnabled,
                "syntheticCompanionLovinWithSpouseEnabled",
                false);
            Scribe_Values.Look(
                ref pregnancyApproach,
                "syntheticCompanionPregnancyApproach",
                SyntheticPregnancyApproach.AvoidPregnancy);
            // PostLoadInit：空 Pawn 由注册表 CleanupInvalidRecords 剔除；
            // 死亡、尸体与可复活对象保留授权，此处不删除记录。
        }
    }
}
