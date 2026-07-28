using RimWorld;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor
{
    public static class CerebrexBossLordUtility
    {
        /// <summary>
        /// 按“同一地图 + 同一派系 + CerebrexBossAssault + 同一主脑 coreThingId”复用或创建进攻 Lord。
        /// 多座主脑的 Lord 不会互相混用。
        /// </summary>
        public static Lord? EnsureAssaultLord(Map map, Faction? faction, int coreThingId)
        {
            if (map == null || faction == null)
            {
                return null;
            }

            foreach (Lord lord in map.lordManager.lords)
            {
                if (lord.LordJob is LordJob_CerebrexBossAssault assault
                    && assault.coreThingId == coreThingId
                    && lord.faction == faction)
                {
                    return lord;
                }
            }

            LordJob_CerebrexBossAssault job = new LordJob_CerebrexBossAssault(faction, coreThingId);
            return LordMaker.MakeNewLord(faction, job, map);
        }
    }
}
