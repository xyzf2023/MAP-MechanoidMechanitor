using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(Projectile), "CheckForFreeInterceptBetween")]
    internal static class GravityFieldProjectileInterceptPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Projectile __instance, Vector3 lastExactPos,
            Vector3 newExactPos, ref bool __result)
        {
            if (__instance.Spawned && __instance.Map
                .GetComponent<MapComponent_GravityFieldTracker>()
                .TryIntercept(__instance, lastExactPos, newExactPos))
            {
                __result = true;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Projectile), "ImpactSomething")]
    internal static class GravityFieldProjectileImpactPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Projectile __instance, Vector3 ___origin)
        {
            if (!__instance.Spawned)
                return true;
            Vector3 end = __instance.ExactPosition;
            // 原版 Beam 在 Launch 内立即命中，不经过逐 tick 的飞行拦截。
            // 不在基类 Launch 中销毁，否则 Beam 的后续代码仍会访问已失效的 Map。
            Vector3 start = __instance is Beam ? ___origin : end;
            return !__instance.Map.GetComponent<MapComponent_GravityFieldTracker>()
                .TryIntercept(__instance, start, end);
        }
    }

    [HarmonyPatch(typeof(Explosion), nameof(Explosion.StartExplosion))]
    internal static class GravityFieldExplosionStartPatch
    {
        private static bool Prefix(Explosion __instance)
        {
            // 比仅拦 GenExplosion.DoExplosion 更低一层，也覆盖直接创建 Explosion 的调用者。
            return !GravityFieldExplosionSuppression.TrySuppress(__instance);
        }
    }

    [HarmonyPatch(typeof(Explosion), "Tick")]
    internal static class GravityFieldExplosionTickPatch
    {
        private static bool Prefix(Explosion __instance)
        {
            // 展开前已经产生、仍在传播的爆炸，也按其爆心判断并停止后续传播。
            return !GravityFieldExplosionSuppression.TrySuppress(__instance);
        }
    }

    internal static class GravityFieldExplosionSuppression
    {
        internal static bool TrySuppress(Explosion explosion)
        {
            if (!explosion.Spawned || !explosion.Map
                .GetComponent<MapComponent_GravityFieldTracker>()
                .SuppressesExplosion(explosion.Position))
                return false;
            explosion.Destroy(DestroyMode.Vanish);
            return true;
        }
    }
}
