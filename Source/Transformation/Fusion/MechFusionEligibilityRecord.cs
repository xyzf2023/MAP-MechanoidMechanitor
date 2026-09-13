using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 先天合体资格记录。注册表中存在有效记录即表示该 Pawn 有合体资格，
    /// 这里不保存“资格来源”列表，也不与形态记录或本次合体实例混用。
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
