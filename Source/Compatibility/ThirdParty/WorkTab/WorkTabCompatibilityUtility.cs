using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.WorkTab
{
    internal static class WorkTabCompatibilityUtility
    {
        internal const string PackageId = "Fluffy.WorkTab";

        // 只保存已校验的反射元数据；不缓存 Game、Map、Pawn 或 WorkTab 组件实例。
        private static Type? priorityManagerType;
        private static Type? favouriteManagerType;
        private static FieldInfo? priorityManagerInstance;
        private static FieldInfo? workSettingsPawn;
        private static Func<Pawn, bool>? originalHumanlike;
        private static bool enabled;

        internal static MethodInfo? HumanlikeMethod { get; private set; }
        internal static MethodInfo? SelectedHoursGetter { get; private set; }

        internal static void Configure(Type priorityType, Type favouriteType,
            FieldInfo instanceField, FieldInfo pawnField, MethodInfo humanlike, MethodInfo selectedHours)
        {
            enabled = false;
            priorityManagerType = priorityType;
            favouriteManagerType = favouriteType;
            priorityManagerInstance = instanceField;
            workSettingsPawn = pawnField;
            HumanlikeMethod = humanlike;
            SelectedHoursGetter = selectedHours;
            originalHumanlike = (Func<Pawn, bool>)Delegate.CreateDelegate(typeof(Func<Pawn, bool>), humanlike);
        }

        internal static void Enable() => enabled = true;

        // 回滚失败时残留的 helper 仍可执行原有判断，但不得继续扩展机械师资格。
        internal static void Disable() => enabled = false;

        internal static bool IsPlayerMechanitor(Pawn? pawn) =>
            pawn != null && !pawn.Destroyed && !pawn.Discarded && !pawn.Dead
            && pawn.RaceProps?.IsMechanoid == true
            && pawn.Faction?.IsPlayerSafe() == true
            && !pawn.IsPrisoner && !pawn.IsSlave && pawn.HostFaction == null
            && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn);

        internal static bool CanUseDetailedPriorities(Pawn? pawn)
        {
            if (!enabled || !IsPlayerMechanitor(pawn) || pawn!.workSettings?.Initialized != true
                || Scribe.mode != LoadSaveMode.Inactive
                || MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferPositiveRestore)
                return false;

            Game? game = Current.Game;
            if (game == null || priorityManagerType == null || favouriteManagerType == null
                || priorityManagerInstance == null)
                return false;

            // WorkTab.Get 在组件尚未构造时会抛异常；同时排除上一局残留的静态实例。
            GameComponent? manager = game.GetComponent(priorityManagerType);
            return manager != null
                && ReferenceEquals(manager, priorityManagerInstance.GetValue(null))
                && game.GetComponent(favouriteManagerType) != null;
        }

        internal static bool HumanlikeOrMechanitor(Pawn pawn) =>
            (originalHumanlike?.Invoke(pawn) ?? pawn?.RaceProps?.Humanlike == true)
            || CanUseDetailedPriorities(pawn);

        internal static Pawn? GetPawn(Pawn_WorkSettings settings) =>
            workSettingsPawn?.GetValue(settings) as Pawn;

        internal static List<Pawn> GetHourlyRefreshPawns(MapPawns mapPawns)
        {
            List<Pawn> original = mapPawns.FreeColonistsSpawned;
            if (!enabled)
                return original;

            List<Pawn>? combined = null;
            IReadOnlyList<Pawn> mechanitors = GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors;
            for (int i = 0; i < mechanitors.Count; i++)
            {
                Pawn pawn = mechanitors[i];
                if (!pawn.Spawned || pawn.Map?.mapPawns != mapPawns
                    || !CanUseDetailedPriorities(pawn) || original.Contains(pawn))
                    continue;

                // 原名单属于 MapPawns 的共享缓存，只向本次调用的副本补充机械师。
                combined ??= new List<Pawn>(original);
                if (!combined.Contains(pawn))
                    combined.Add(pawn);
            }
            return combined ?? original;
        }
    }
}
