using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_MechFusionShell : CompProperties
    {
        public CompProperties_MechFusionShell()
        {
            compClass = typeof(CompMechFusionShell);
        }
    }

    /// <summary>
    /// 合体服装连接组件。这里只保存与权威合体记录连接所需的稳定 ID，
    /// 不保存第二份能源、结构稳定值或属性数据；脱下与销毁通知在第二轮接入
    /// 统一解除合体服务。
    /// </summary>
    public sealed class CompMechFusionShell : ThingComp
    {
        private string? sessionId;

        public string? SessionId => sessionId;

        internal void AssignSession(string value)
        {
            sessionId = value;
        }

        internal void ClearSession()
        {
            sessionId = null;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref sessionId, "sessionId");
        }
    }
}
