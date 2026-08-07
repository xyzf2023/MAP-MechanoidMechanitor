using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class LordJob_SymbiosisCovenantTradeDelegation : LordJob_TradeWithColony
    {
        private Faction? leadFaction;
        private List<Faction> participatingFactions = new List<Faction>();
        private TraderKindDef? baseTraderKind;
        private List<ThingDef> extraTradeThingDefs = new List<ThingDef>();

        public Faction? LeadFaction => leadFaction;
        public IReadOnlyList<Faction> ParticipatingFactions => participatingFactions;
        public TraderKindDef? BaseTraderKind => baseTraderKind;

        public LordJob_SymbiosisCovenantTradeDelegation()
        {
        }

        public LordJob_SymbiosisCovenantTradeDelegation(
            Faction leadFaction,
            IntVec3 chillSpot,
            IEnumerable<Faction> participatingFactions,
            TraderKindDef baseTraderKind,
            IEnumerable<ThingDef> extraTradeThingDefs)
            : base(leadFaction, chillSpot)
        {
            this.leadFaction = leadFaction;
            this.participatingFactions = participatingFactions
                .Where(faction => faction != null)
                .Distinct()
                .ToList();
            if (!this.participatingFactions.Contains(leadFaction))
            {
                this.participatingFactions.Insert(0, leadFaction);
            }
            this.baseTraderKind = baseTraderKind;
            this.extraTradeThingDefs = extraTradeThingDefs
                .Where(def => def != null)
                .Distinct()
                .ToList();
        }

        public bool IsExtraTradeThingDef(ThingDef? def)
        {
            return def != null && extraTradeThingDefs.Contains(def);
        }

        public bool AnyParticipatingFactionHostileToPlayer()
        {
            for (int i = 0; i < participatingFactions.Count; i++)
            {
                Faction faction = participatingFactions[i];
                if (faction != null && faction.HostileTo(Faction.OfPlayer))
                {
                    return true;
                }
            }
            return false;
        }

        public override StateGraph CreateGraph()
        {
            StateGraph graph = base.CreateGraph();
            LordToil_ExitMapAndEscortCarriers? exitToil = graph.lordToils
                .OfType<LordToil_ExitMapAndEscortCarriers>()
                .FirstOrDefault();
            if (exitToil == null)
            {
                return graph;
            }

            List<LordToil> sources = graph.lordToils
                .Where(toil => toil != exitToil
                    && !(toil is LordToil_ExitMap)
                    && !(toil is LordToil_ExitMapTraderFighting))
                .ToList();
            if (sources.Count == 0)
            {
                return graph;
            }

            Transition hostileTransition = new Transition(sources[0], exitToil);
            if (sources.Count > 1)
            {
                hostileTransition.AddSources(sources.Skip(1).ToArray());
            }
            hostileTransition.AddTrigger(
                new Trigger_Custom(
                    signal => signal.type == TriggerSignalType.Tick
                        && AnyParticipatingFactionHostileToPlayer()));
            hostileTransition.AddPostAction(new TransitionAction_WakeAll());
            hostileTransition.AddPostAction(new TransitionAction_EndAllJobs());
            graph.AddTransition(hostileTransition, highPriority: true);
            return graph;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref leadFaction, "symbiosisCovenantLeadFaction");
            Scribe_Collections.Look(
                ref participatingFactions,
                "symbiosisCovenantParticipatingFactions",
                LookMode.Reference);
            Scribe_Defs.Look(ref baseTraderKind, "symbiosisCovenantBaseTraderKind");
            Scribe_Collections.Look(
                ref extraTradeThingDefs,
                "symbiosisCovenantExtraTradeThingDefs",
                LookMode.Def);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                participatingFactions ??= new List<Faction>();
                participatingFactions.RemoveAll(faction => faction == null);
                extraTradeThingDefs ??= new List<ThingDef>();
                extraTradeThingDefs.RemoveAll(def => def == null);
            }
        }
    }
}
