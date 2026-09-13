using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 可序列化的单条 Stat 快照。StatModifier 本身不是 IExposable，
    /// 因此合体快照使用自带 Scribe 的条目类保存。
    /// </summary>
    public sealed class MechFusionStatEntry : IExposable
    {
        public StatDef? stat;
        public float value;

        public MechFusionStatEntry()
        {
        }

        public MechFusionStatEntry(StatDef stat, float value)
        {
            this.stat = stat;
            this.value = value;
        }

        public void ExposeData()
        {
            Scribe_Defs.Look(ref stat, "stat");
            Scribe_Values.Look(ref value, "value");
        }
    }

    /// <summary>
    /// 白名单规则的序列化载荷。规则通过 ruleId 在注册表中查找，
    /// 找不到已安装规则时安全跳过并保留可恢复状态。
    /// </summary>
    public sealed class MechFusionWhitelistEntry : IExposable
    {
        public string ruleId = string.Empty;
        public string payload = string.Empty;

        public void ExposeData()
        {
            Scribe_Values.Look(ref ruleId, "ruleId");
            Scribe_Values.Look(ref payload, "payload");
        }
    }
}
