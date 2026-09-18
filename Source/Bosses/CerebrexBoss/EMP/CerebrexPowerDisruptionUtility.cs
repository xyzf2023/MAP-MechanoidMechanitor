using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 静态辅助：追踪被主脑 EMP 冲击波瘫痪的用电建筑，按截止 tick 判定是否仍应断电。
    /// 该弱键表为当前 Game 的运行时缓存，不随存档保存；持久权威状态保存在各主脑组件的
    /// disabledPowerBuildings 中，各主脑组件会在读档后把尚未过期的记录重新注册进来。
    /// 静态缓存绝不能跨 Game 生存，必须在开始新游戏 / 开始加载另一个 Game 时显式清空。
    /// </summary>
    public static class CerebrexPowerDisruptionUtility
    {
        private sealed class DisruptionState
        {
            public int UntilTick;
        }

        // 建筑停止被查询后也不能由静态缓存保活；持久记录仍由主脑组件负责。
        private static ConditionalWeakTable<Thing, DisruptionState> disabledUntilByThing = new();

        /// <summary>
        /// 清空当前 Game 的运行时缓存。开始新游戏或开始加载另一个 Game 时必须调用，
        /// 防止上一局的静态状态污染新存档。本方法只清运行时缓存，不触碰任何持久数据。
        /// </summary>
        public static void ResetRuntimeState()
        {
            disabledUntilByThing = new ConditionalWeakTable<Thing, DisruptionState>();
        }

        /// <summary>
        /// 登记一座建筑被断电到指定 tick。同一建筑保留更晚的截止时间。
        /// </summary>
        public static void Register(Thing thing, int disabledUntilTick)
        {
            if (thing == null || thing.Destroyed || thing.Discarded)
            {
                return;
            }

            if (disabledUntilByThing.TryGetValue(thing, out DisruptionState existing))
            {
                if (disabledUntilTick > existing.UntilTick)
                {
                    existing.UntilTick = disabledUntilTick;
                }
            }
            else
            {
                disabledUntilByThing.Add(thing, new DisruptionState { UntilTick = disabledUntilTick });
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

            if (thing == null)
            {
                return false;
            }

            if (thing.Destroyed || thing.Discarded)
            {
                disabledUntilByThing.Remove(thing);
                return false;
            }

            TickManager? tickManager = Current.Game?.tickManager;
            if (tickManager == null
                || !disabledUntilByThing.TryGetValue(thing, out DisruptionState state))
            {
                return false;
            }

            if (tickManager.TicksGame >= state.UntilTick)
            {
                disabledUntilByThing.Remove(thing);
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// 加载另一个存档前清空静态断电缓存，确保不同 Game 之间不共享运行时状态。
    /// 之后各主脑组件会在自己读档恢复阶段从持久数据重新注册。
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.LoadGame))]
    internal static class CerebrexPowerDisruptionCacheResetOnLoadPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            CerebrexPowerDisruptionUtility.ResetRuntimeState();
        }
    }

    /// <summary>
    /// 开始新游戏前清空静态断电缓存，与读档路径保持同一语义。
    /// </summary>
    [HarmonyPatch(typeof(GameComponentUtility), nameof(GameComponentUtility.StartedNewGame))]
    internal static class CerebrexPowerDisruptionCacheResetOnNewGamePatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            CerebrexPowerDisruptionUtility.ResetRuntimeState();
        }
    }
}
