using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [HarmonyPatch(
        typeof(GoodwillSituationWorker_PermanentEnemy),
        nameof(GoodwillSituationWorker_PermanentEnemy.ArePermanentEnemies))]
    public static class MechanoidMechanitorPermanentEnemy_ArePermanentEnemies_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Faction a, Faction b, ref bool __result)
        {
            if (__result || a == null || b == null || a == b)
            {
                return;
            }

            if (!MechanoidMechanitorOrdinaryFactionUtility.TryGetPlayerAndOrdinary(
                    a,
                    b,
                    out _,
                    out Faction ordinary))
            {
                return;
            }

            if (MechanoidMechanitorQuestFactionPolicy
                    .IsPermanentlyHostileToPlayer(ordinary))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(
        typeof(GameComponent_MechanoidMechanitorStoryState),
        nameof(GameComponent_MechanoidMechanitorStoryState
            .MarkInitialOrdinaryFactionRelationsApplied))]
    public static class MechanoidMechanitorPermanentEnemyCache_InitialRelationsApplied_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (Current.Game == null || Find.FactionManager == null)
            {
                return;
            }

            Find.GoodwillSituationManager.RecalculateAll(
                canSendHostilityChangedLetter: false);
        }
    }
}
