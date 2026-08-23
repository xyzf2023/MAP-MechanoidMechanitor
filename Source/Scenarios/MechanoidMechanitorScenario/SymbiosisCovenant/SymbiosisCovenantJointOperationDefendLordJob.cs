using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// MAP 派系前哨的守军 LordJob。
    ///
    /// 该类型仅作为可存档的前哨专用类型保留；行为完全继承文化 DLC Work Site
    /// 使用的原版 LordJob_DefendBase，不额外覆盖状态机，也不在联合行动中迁移 Pawn。
    /// 守军会先保卫基地，再由原版的受伤、减员、建筑受损、随机触发、
    /// 紧急饥饿或 delayBeforeAssault 到期等条件统一转入主动进攻。
    /// </summary>
    public class LordJob_MAPFactionOutpostDefendBase : LordJob_DefendBase
    {
        // 无参构造函数用于读取现有存档，必须保留。
        public LordJob_MAPFactionOutpostDefendBase()
            : base()
        {
        }

        // 与原版 LordJob_DefendBase 构造函数完全对应。
        public LordJob_MAPFactionOutpostDefendBase(
            Faction faction,
            IntVec3 baseCenter,
            int delayBeforeAssault)
            : base(faction, baseCenter, delayBeforeAssault)
        {
        }
    }
}
