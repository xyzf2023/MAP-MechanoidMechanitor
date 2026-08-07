using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 旧存档加载后，为已注册的机械族机械师补齐 Royalty Tracker。
    /// </summary>
    [HarmonyPatch(
        typeof(GameComponent_MechanoidMechanitorRegistry),
        nameof(GameComponent_MechanoidMechanitorRegistry.LoadedGame))]
    public static class Patch_MechanoidMechanitorRegistry_LoadedGame_Royalty
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            MechanoidMechanitorRoyaltyUtility.SynchronizeAllRegisteredMechanitors();
        }
    }

    /// <summary>
    /// 开发者模式撤销机械族机械师身份时清理本 MOD 注入的爵位 Tracker，
    /// 避免普通机械族继续被原版 Royalty Tick 当作贵族处理。
    /// </summary>
    [HarmonyPatch(
        typeof(GameComponent_MechanoidMechanitorRegistry),
        nameof(GameComponent_MechanoidMechanitorRegistry.TryUnregisterFromDebug))]
    public static class Patch_MechanoidMechanitorRegistry_TryUnregisterFromDebug_Royalty
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn? pawn, bool __result)
        {
            if (!__result
                || pawn == null
                || pawn.royalty == null
                || MechanoidMechanitorRoyaltyUtility.IsRoyaltyEligibleMechanitor(pawn))
            {
                return;
            }

            pawn.royalty = null;
            pawn.abilities?.Notify_TemporaryAbilitiesChanged();
            pawn.Notify_DisabledWorkTypesChanged();
            pawn.needs?.AddOrRemoveNeedsAsAppropriate();
            pawn.apparel?.Notify_TitleChanged();
        }
    }
}
