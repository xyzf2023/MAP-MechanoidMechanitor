using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 持久合体资格记录。记录可由先天组件自动建立，也可由 DEV 明确授权；
    /// 注册表中存在有效记录即表示该 Pawn 有合体资格。这里不与形态记录
    /// 或本次合体实例混用。
    /// </summary>
    public sealed class MechFusionEligibilityRecord : IExposable
    {
        private Pawn? pawn;

        public Pawn? Pawn => pawn;

        public MechFusionEligibilityRecord()
        {
        }

        internal MechFusionEligibilityRecord(Pawn pawn)
        {
            this.pawn = pawn;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
        }
    }
}
