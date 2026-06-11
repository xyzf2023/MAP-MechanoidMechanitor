using HarmonyLib;
using RimWorld;
using Verse;
using System;
using System.Reflection;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(FloatMenuOptionProvider), nameof(FloatMenuOptionProvider.SelectedPawnValid))]
    public static class ColonistLikeFloatMenuPatches
    {
        private static readonly MethodInfo? DraftedGetter =
            AccessTools.PropertyGetter(typeof(FloatMenuOptionProvider), "Drafted");

        private static readonly MethodInfo? UndraftedGetter =
            AccessTools.PropertyGetter(typeof(FloatMenuOptionProvider), "Undrafted");

        private static readonly MethodInfo? RequiresManipulationGetter =
            AccessTools.PropertyGetter(typeof(FloatMenuOptionProvider), "RequiresManipulation");

        private static readonly MethodInfo? MechanoidCanDoGetter =
            AccessTools.PropertyGetter(typeof(FloatMenuOptionProvider), "MechanoidCanDo");

        private static bool reflectionFailureLogged;

        [HarmonyPostfix]
        public static void Postfix(
            FloatMenuOptionProvider __instance,
            Pawn pawn,
            FloatMenuContext context,
            ref bool __result)
        {
            if (__result)
            {
                return;
            }

            if (!CompColonistLikeFloatMenuUser.PawnCanUseColonistLikeFloatMenu(pawn))
            {
                return;
            }

            if (!pawn.RaceProps.IsMechanoid)
            {
                return;
            }

            if (!TryGetMechanoidCanDo(__instance, out bool mechanoidCanDo))
            {
                return;
            }

            if (mechanoidCanDo)
            {
                return;
            }

            if (WouldPassSelectedPawnValidIgnoringMechanoid(__instance, pawn, context))
            {
                __result = true;
            }
        }

        private static bool WouldPassSelectedPawnValidIgnoringMechanoid(
            FloatMenuOptionProvider provider,
            Pawn pawn,
            FloatMenuContext context)
        {
            if (pawn.IsMutant
                && pawn.mutant.Def.whitelistedFloatMenuProviders != null
                && !pawn.mutant.Def.whitelistedFloatMenuProviders.Contains(FloatMenuMakerMap.currentProvider.GetType()))
            {
                return false;
            }

            if (!TryGetDrafted(provider, out bool drafted)
                || !TryGetUndrafted(provider, out bool undrafted)
                || !TryGetRequiresManipulation(provider, out bool requiresManipulation))
            {
                return false;
            }

            if (!drafted && pawn.Drafted)
            {
                return false;
            }

            if (!undrafted && !pawn.Drafted)
            {
                return false;
            }

            if (requiresManipulation && !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
            {
                return false;
            }

            return true;
        }

        private static bool TryGetMechanoidCanDo(FloatMenuOptionProvider provider, out bool value)
        {
            return TryGetProviderBoolProperty(provider, MechanoidCanDoGetter, "MechanoidCanDo", out value);
        }

        private static bool TryGetDrafted(FloatMenuOptionProvider provider, out bool value)
        {
            return TryGetProviderBoolProperty(provider, DraftedGetter, "Drafted", out value);
        }

        private static bool TryGetUndrafted(FloatMenuOptionProvider provider, out bool value)
        {
            return TryGetProviderBoolProperty(provider, UndraftedGetter, "Undrafted", out value);
        }

        private static bool TryGetRequiresManipulation(FloatMenuOptionProvider provider, out bool value)
        {
            return TryGetProviderBoolProperty(provider, RequiresManipulationGetter, "RequiresManipulation", out value);
        }

        private static bool TryGetProviderBoolProperty(
            FloatMenuOptionProvider provider,
            MethodInfo? getter,
            string propertyName,
            out bool value)
        {
            value = false;

            if (getter == null)
            {
                LogReflectionFailureOnce(propertyName);
                return false;
            }

            try
            {
                value = (bool)getter.Invoke(provider, null)!;
                return true;
            }
            catch (Exception)
            {
                LogReflectionFailureOnce(propertyName);
                return false;
            }
        }

        private static void LogReflectionFailureOnce(string propertyName)
        {
            if (!Prefs.DevMode || reflectionFailureLogged)
            {
                return;
            }

            reflectionFailureLogged = true;
            Log.Message(
                $"[MAP] ColonistLikeFloatMenu: failed to read FloatMenuOptionProvider.{propertyName} via reflection");
        }
    }
}
