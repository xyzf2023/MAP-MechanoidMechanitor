using RimWorld;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor
{
    public class LordJob_JusticeBossGuards : LordJob
    {
        private Faction? faction;

        private IntVec3 defendPoint = IntVec3.Invalid;

        private float defendRadius = 18f;

        private float wanderRadius = 12f;

        public LordJob_JusticeBossGuards()
        {
        }

        public LordJob_JusticeBossGuards(Faction? faction, IntVec3 defendPoint)
        {
            this.faction = faction;
            this.defendPoint = defendPoint;
        }

        public override StateGraph CreateGraph()
        {
            StateGraph graph = new StateGraph();
            graph.StartingToil = new LordToil_DefendPoint(
                defendPoint,
                defendRadius,
                wanderRadius);
            return graph;
        }

        public override void ExposeData()
        {
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref defendPoint, "defendPoint");
            Scribe_Values.Look(ref defendRadius, "defendRadius", 18f);
            Scribe_Values.Look(ref wanderRadius, "wanderRadius", 12f);
        }
    }
}