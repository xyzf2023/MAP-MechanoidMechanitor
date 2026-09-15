using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体健康状态规则的序列化结果。命中条件只在合体瞬间计算；
    /// 后续添加、读档补齐与解除均使用这里保存的专属 HediffDef。
    /// </summary>
    public sealed class MechFusionHealthEffectEntry : IExposable
    {
        public string? ruleId;
        public HediffDef? hediffDef;
        public float severity = -1f;

        public void ExposeData()
        {
            Scribe_Values.Look(ref ruleId, "ruleId");
            Scribe_Defs.Look(ref hediffDef, "hediffDef");
            Scribe_Values.Look(ref severity, "severity", -1f);
        }
    }
}
