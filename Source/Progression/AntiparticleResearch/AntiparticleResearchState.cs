using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [DefOf]
    public static class AntiparticleResearchDefOf
    {
        public static ResearchProjectDef MAP_AntiparticleApplications = null!;
        public static ThingDef MAP_AntimatterContainmentDevice = null!;
        public static JobDef MAP_AnalyzeAntimatterContainmentDevice = null!;

        static AntiparticleResearchDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(AntiparticleResearchDefOf));
    }

    /// <summary>发现状态属于整个存档；分析时间属于具体约束器。</summary>
    public sealed class GameComponent_AntiparticleResearch : GameComponent
    {
        private bool discovered;
        private bool completionLetterSent;

        public GameComponent_AntiparticleResearch(Game game) { }

        internal static GameComponent_AntiparticleResearch? Current =>
            CurrentGameComponentCache<GameComponent_AntiparticleResearch>.Get();

        internal static bool Discovered => Current?.discovered == true
            || (Verse.Current.Game != null && Find.ResearchManager != null
                && AntiparticleResearchDefOf.MAP_AntiparticleApplications?.IsFinished == true);

        internal static bool IsUndiscovered(ResearchProjectDef? project) =>
            project != null && project == AntiparticleResearchDefOf.MAP_AntiparticleApplications && !Discovered;

        internal void Discover()
        {
            if (Discovered) return;
            discovered = true;
            // 可见列表过滤不修改原版缓存；刷新已打开窗口的搜索和选中状态即可。
            if (MainButtonDefOf.Research.TabWindow is MainTabWindow_Research window && window.IsOpen)
                window.PreOpen();
            SendAnalysisCompletedLetter();
        }

        private void SendAnalysisCompletedLetter()
        {
            if (completionLetterSent) return;
            completionLetterSent = true;
            Find.LetterStack.ReceiveLetter(
                "MAP_Antiparticle.CompletedTitle".Translate(),
                "MAP_Antiparticle.CompletedText".Translate(),
                LetterDefOf.PositiveEvent);
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref discovered, "antiparticleResearchDiscovered");
            Scribe_Values.Look(ref completionLetterSent, "antiparticleResearchCompletionLetterSent");
        }
    }

    [HarmonyPatch(typeof(MainTabWindow_Research), nameof(MainTabWindow_Research.VisibleResearchProjects), MethodType.Getter)]
    internal static class AntiparticleResearchVisibilityPatch
    {
        private static void Postfix(ref List<ResearchProjectDef> __result)
        {
            if (GameComponent_AntiparticleResearch.Discovered) return;
            // 不改共享列表，否则发现后或切换存档时项目可能仍被永久移除。
            __result = __result.FindAll(project => project != AntiparticleResearchDefOf.MAP_AntiparticleApplications);
        }
    }

    [HarmonyPatch(typeof(MainTabWindow_Research), nameof(MainTabWindow_Research.Select))]
    internal static class AntiparticleResearchInfoSelectionPatch
    {
        private static bool Prefix(ResearchProjectDef project) => !GameComponent_AntiparticleResearch.IsUndiscovered(project);
    }

    [HarmonyPatch(typeof(ResearchProjectDef), nameof(ResearchProjectDef.CanStartNow), MethodType.Getter)]
    internal static class AntiparticleResearchStartPatch
    {
        private static void Postfix(ResearchProjectDef __instance, ref bool __result)
        {
            if (GameComponent_AntiparticleResearch.IsUndiscovered(__instance)) __result = false;
        }
    }

    [HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.SetCurrentProject))]
    internal static class AntiparticleResearchSelectionPatch
    {
        private static bool Prefix(ResearchProjectDef proj) => !GameComponent_AntiparticleResearch.IsUndiscovered(proj);
    }

}
