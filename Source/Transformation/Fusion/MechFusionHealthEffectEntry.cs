using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体健康状态的序列化记录。常驻规则在合体瞬间捕获；技能产生的
    /// 限时条目另外保存到期 tick，添加、读档补齐与解除使用同一管理层。
    /// </summary>
    public sealed class MechFusionHealthEffectEntry : IExposable
    {
        public string? ruleId;
        public HediffDef? hediffDef;
        public float severity = -1f;

        // -1 为常驻效果；限时条目到期后保留，读档不得重新补发。
        public int expiresAtTick = -1;

        // 仅供“灵能同调”规则使用。两个字段共同区分“合体瞬间明确判定为否”
        // 与旧存档/其他规则从未捕获过该资格；读档时禁止重新检查当前启灵神经。
        public bool psychicActivationAuthorizationCaptured;
        public bool psychicActivationAuthorized;

        public void ExposeData()
        {
            Scribe_Values.Look(ref ruleId, "ruleId");
            Scribe_Defs.Look(ref hediffDef, "hediffDef");
            Scribe_Values.Look(ref severity, "severity", -1f);
            Scribe_Values.Look(ref expiresAtTick, "expiresAtTick", -1);
            Scribe_Values.Look(
                ref psychicActivationAuthorizationCaptured,
                "psychicActivationAuthorizationCaptured");
            Scribe_Values.Look(
                ref psychicActivationAuthorized,
                "psychicActivationAuthorized");
        }
    }
}
