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

        // 仅供“灵能同调”规则使用。两个字段共同区分“合体瞬间明确判定为否”
        // 与旧存档/其他规则从未捕获过该资格；读档时禁止重新检查当前启灵神经。
        public bool psychicActivationAuthorizationCaptured;
        public bool psychicActivationAuthorized;

        public void ExposeData()
        {
            Scribe_Values.Look(ref ruleId, "ruleId");
            Scribe_Defs.Look(ref hediffDef, "hediffDef");
            Scribe_Values.Look(ref severity, "severity", -1f);
            Scribe_Values.Look(
                ref psychicActivationAuthorizationCaptured,
                "psychicActivationAuthorizationCaptured");
            Scribe_Values.Look(
                ref psychicActivationAuthorized,
                "psychicActivationAuthorized");
        }
    }
}
