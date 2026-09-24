using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_AntimatterAnalysis : CompProperties
    {
        public CompProperties_AntimatterAnalysis() => compClass = typeof(CompAntimatterAnalysis);
    }

    /// <summary>不接入原版 AnalysisManager；物品不消耗，累计有效工作 tick。</summary>
    public sealed class CompAntimatterAnalysis : ThingComp
    {
        internal const int RequiredTicks = 60000;
        // 界面按 0/6000 的无单位刻度显示，实际计时与存档仍使用 tick。
        private const int DisplayProgressTotal = 6000;
        private int completedTicks;
        private bool requested;

        internal bool Requested => requested && !GameComponent_AntiparticleResearch.Discovered;
        internal float Progress => (float)completedTicks / RequiredTicks;
        internal void Request() => requested = true;

        internal bool Work(int ticks)
        {
            if (GameComponent_AntiparticleResearch.Discovered) return true;
            completedTicks = Math.Min(RequiredTicks, completedTicks + Math.Max(0, ticks));
            if (completedTicks < RequiredTicks) return false;
            GameComponent_AntiparticleResearch.Current?.Discover();
            requested = false;
            return true;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref completedTicks, "antimatterAnalysisTicks");
            Scribe_Values.Look(ref requested, "antimatterAnalysisRequested");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                completedTicks = Math.Max(0, Math.Min(RequiredTicks, completedTicks));
        }

        public override string CompInspectStringExtra()
        {
            string hint = "MAP_Antiparticle.AnalysisHint".Translate();
            if (GameComponent_AntiparticleResearch.Discovered) return hint;
            return hint + "\n" + "MAP_Antiparticle.Progress".Translate(
                completedTicks * DisplayProgressTotal / RequiredTicks, DisplayProgressTotal);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (DebugSettings.ShowDevGizmos && !GameComponent_AntiparticleResearch.Discovered)
                yield return new Command_Action
                {
                    defaultLabel = "MAP_Antiparticle.DevComplete".Translate(),
                    defaultDesc = "MAP_Antiparticle.DevCompleteDesc".Translate(),
                    action = () => Work(RequiredTicks)
                };
        }
    }
}
