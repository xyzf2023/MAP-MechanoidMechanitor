using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>将已注册的机械族机械师纳入原版无活动加速判定。</summary>
    [HarmonyPatch(typeof(TickManager), "NothingHappeningInGame")]
    public static class MechanoidMechanitorTimeSpeedPatches
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result, ref bool ___nothingHappeningCached)
        {
            if (!__result)
            {
                return;
            }

            IReadOnlyList<Pawn> mechanitors =
                GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors;
            for (int i = 0; i < mechanitors.Count; i++)
            {
                Pawn pawn = mechanitors[i];
                // 缓存中的状态仍需复核；只计入原版会检查的在图玩家 Pawn。
                if (!GameComponent_MechanoidMechanitorRegistry.IsPawnAliveAndInitialized(pawn)
                    || !pawn.Spawned
                    || pawn.Map == null
                    || !Find.Maps.Contains(pawn.Map)
                    || pawn.Faction != Faction.OfPlayer
                    || pawn.HostFaction != null
                    || pawn.IsGhoul
                    || !pawn.Awake())
                {
                    continue;
                }

                // 同步原版每 tick 缓存，避免同一 tick 再次进入时重复补查。
                ___nothingHappeningCached = false;
                __result = false;
                return;
            }
        }
    }
}
