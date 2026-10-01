using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>准备完成的存档保持暂停；读取其他存档后状态自然隔离。</summary>
    public sealed class GameComponent_ModUninstallPreparation : GameComponent
    {
        private bool prepared;
        internal List<Pawn> AffectedMechs = new List<Pawn>();
        internal List<Pawn> AffectedControllers = new List<Pawn>();
        internal List<MapParent> OriginalParents = new List<MapParent>();
        internal List<MapParent> ReplacementParents = new List<MapParent>();
        internal int UnassignedMechs;
        internal static bool IsPrepared => CurrentGameComponentCache<GameComponent_ModUninstallPreparation>.Get()?.prepared == true;
        internal static bool IsExecuting { get; set; }

        public GameComponent_ModUninstallPreparation(Game game) { }

        internal void BeginFinalization() => prepared = true;
        internal void CancelPrepared() => prepared = false;

        internal void MarkPrepared()
        {
            prepared = true;
            RemoveModComponents();
            AffectedMechs.Clear();
            AffectedControllers.Clear();
            OriginalParents.Clear();
            ReplacementParents.Clear();
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref prepared, "modUninstallPrepared");
            Scribe_Collections.Look(ref AffectedMechs, "uninstallAffectedMechs", LookMode.Reference);
            Scribe_Collections.Look(ref AffectedControllers, "uninstallAffectedControllers", LookMode.Reference);
            Scribe_Collections.Look(ref OriginalParents, "uninstallOriginalParents", LookMode.Reference);
            Scribe_Collections.Look(ref ReplacementParents, "uninstallReplacementParents", LookMode.Reference);
            Scribe_Values.Look(ref UnassignedMechs, "uninstallUnassignedMechs");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                AffectedMechs ??= new List<Pawn>();
                AffectedControllers ??= new List<Pawn>();
                OriginalParents ??= new List<MapParent>();
                ReplacementParents ??= new List<MapParent>();
            }
        }

        public override void LoadedGame()
        {
            if (prepared)
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    RemoveModComponents();
                    Find.TickManager.Pause();
                    ModUninstallPreparation.ShowCompletion();
                });
        }

        internal static bool OwnType(Type? type) => type != null
            && (type.Assembly == typeof(GameComponent_ModUninstallPreparation).Assembly
                || type.Assembly.GetName().Name == "MAP-MechanoidMechanitor.GD5");

        internal static void RemoveModComponents()
        {
            Current.Game?.components.RemoveAll(c => c != null && OwnType(c.GetType())
                && !(c is GameComponent_ModUninstallPreparation));
            Find.World?.components.RemoveAll(c => c != null && OwnType(c.GetType()));
            foreach (Map map in Find.Maps)
                map.components.RemoveAll(c => c != null && OwnType(c.GetType()));
        }
    }

    // 原版在 GameComponentUpdate 前推进 Tick；必须提前暂停，避免准备后的存档重新生成状态。
    [HarmonyPatch(typeof(Game), nameof(Game.UpdatePlay))]
    internal static class ModUninstallPausePatch
    {
        private static void Prefix()
        {
            if (GameComponent_ModUninstallPreparation.IsExecuting || GameComponent_ModUninstallPreparation.IsPrepared)
                Find.TickManager.Pause();
        }
    }

    // PauseOnLoad 和 DEV 单步也会直接调用 DoSingleTick，不能只拦截常规帧更新。
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.DoSingleTick))]
    internal static class ModUninstallSingleTickPatch
    {
        private static bool Prefix() => !GameComponent_ModUninstallPreparation.IsPrepared
            && !GameComponent_ModUninstallPreparation.IsExecuting;
    }

    // 原版保存 World 时也会 FillComponents；禁止准备完成后重新加入已清理的组件。
    [HarmonyPatch]
    internal static class ModUninstallFillComponentsPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Game), "FillComponents");
            yield return AccessTools.Method(typeof(World), "FillComponents");
            yield return AccessTools.Method(typeof(Map), "FillComponents");
        }

        private static void Postfix(object __instance)
        {
            // LoadGame 会在同一 Game 中以读入的列表替换构造阶段组件，不能沿用旧准备状态。
            if (__instance is Game)
                CurrentGameComponentCache<GameComponent_ModUninstallPreparation>.Invalidate();
            if (!GameComponent_ModUninstallPreparation.IsPrepared) return;
            // 深度读取时新的 Map 可能尚未加入 Find.Maps，直接处理当前实例。
            if (__instance is Game game)
                game.components.RemoveAll(c => c != null && GameComponent_ModUninstallPreparation.OwnType(c.GetType())
                    && !(c is GameComponent_ModUninstallPreparation));
            else if (__instance is World world)
                world.components.RemoveAll(c => c != null && GameComponent_ModUninstallPreparation.OwnType(c.GetType()));
            else if (__instance is Map map)
                map.components.RemoveAll(c => c != null && GameComponent_ModUninstallPreparation.OwnType(c.GetType()));
        }
    }

    // 读档时组件可能先被重新实例化。阻止其 LoadedGame/FinalizeInit 回调补回卸载前的身份和任务。
    [HarmonyPatch]
    internal static class ModUninstallComponentInitializationPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() =>
            typeof(GameComponent_ModUninstallPreparation).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && t != typeof(GameComponent_ModUninstallPreparation)
                    && (typeof(GameComponent).IsAssignableFrom(t) || typeof(WorldComponent).IsAssignableFrom(t)
                        || typeof(MapComponent).IsAssignableFrom(t)))
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                .Where(m => m.Name == "LoadedGame" || m.Name == "FinalizeInit");

        private static bool Prefix() => !GameComponent_ModUninstallPreparation.IsPrepared;
    }
}