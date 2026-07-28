using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 静态辅助：追踪被主脑 EMP 冲击波瘫痪的用电建筑，按截止 tick 判定是否仍应断电。
    /// 该字典为运行时状态，不随存档保存；各主脑组件会在读档后把尚未过期的记录重新注册进来。
    /// </summary>
    public static class CerebrexPowerDisruptionUtility
    {
        private static readonly Dictionary<Thing, int> disabledUntilByThing =
            new Dictionary<Thing, int>();

        /// <summary>
        /// 登记一座建筑被断电到指定 tick。同一建筑保留更晚的截止时间。
        /// </summary>
        public static void Register(Thing thing, int disabledUntilTick)
        {
            if (thing == null)
            {
                return;
            }

            if (disabledUntilByThing.TryGetValue(thing, out int existing))
            {
                if (disabledUntilTick > existing)
                {
                    disabledUntilByThing[thing] = disabledUntilTick;
                }
            }
            else
            {
                disabledUntilByThing[thing] = disabledUntilTick;
            }
        }

        /// <summary>
        /// 判断目标建筑当前是否应处于断电状态。必须仅在奥德赛 DLC 启用时返回 true。
        /// </summary>
        public static bool IsDisabled(Thing thing)
        {
            if (!ModsConfig.OdysseyActive)
            {
                return false;
            }

            if (thing == null || thing.Destroyed)
            {
                return false;
            }

            if (!disabledUntilByThing.TryGetValue(thing, out int until))
            {
                return false;
            }

            if (Find.TickManager.TicksGame >= until)
            {
                disabledUntilByThing.Remove(thing);
                return false;
            }

            return true;
        }
    }
}
