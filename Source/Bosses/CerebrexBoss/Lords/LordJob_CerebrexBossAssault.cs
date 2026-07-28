using RimWorld;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 主脑进攻 Lord：不绑架、不逃跑/超时、不用破墙手、使用聪明避障、不偷窃。
    /// 保存发起该 Lord 的主脑建筑 thingIDNumber，以便按主脑复用独立的 Lord。
    /// </summary>
    public sealed class LordJob_CerebrexBossAssault : LordJob_AssaultColony
    {
        public int coreThingId;

        public LordJob_CerebrexBossAssault()
        {
        }

        public LordJob_CerebrexBossAssault(Faction? faction, int coreThingId)
            : base(
                faction!,
                canKidnap: false,
                canTimeoutOrFlee: false,
                sappers: false,
                useAvoidGridSmart: true,
                canSteal: false)
        {
            this.coreThingId = coreThingId;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref coreThingId, "coreThingId", 0);
        }
    }
}
