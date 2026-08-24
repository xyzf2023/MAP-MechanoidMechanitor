using System;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 主脑战斗期间专用的援军 LordJob。
    /// 行为被刻意限制为「猎杀敌人」一种 toil，不包含任何离图/逃跑/温度撤退分支。
    /// 援军离场完全由 QuestPart 在主脑战斗结束后主动转交给撤离 Lord，不依赖本 Lord 自己离场。
    /// 这修复了太空地图上援军因找不到道路而进入逃跑分支的问题，同时不影响普通援军与访客。
    /// </summary>
    public sealed class LordJob_SymbiosisCovenantCerebrexSupport : LordJob
    {
        private IntVec3 fallbackLocation = IntVec3.Invalid;

        public LordJob_SymbiosisCovenantCerebrexSupport()
        {
        }

        public LordJob_SymbiosisCovenantCerebrexSupport(IntVec3 fallbackLocation)
        {
            this.fallbackLocation = fallbackLocation;
        }

        public override bool AddFleeToil => false;

        public override StateGraph CreateGraph()
        {
            StateGraph stateGraph = new StateGraph();

            LordToil_HuntEnemies huntToil = new LordToil_HuntEnemies(fallbackLocation);
            stateGraph.AddToil(huntToil);

            stateGraph.StartingToil = huntToil;

            return stateGraph;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref fallbackLocation, "fallbackLocation", IntVec3.Invalid);
        }
    }
}
