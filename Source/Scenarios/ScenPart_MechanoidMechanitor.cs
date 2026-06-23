using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class ScenPart_MechanoidMechanitor : ScenPart
    {
        private const string DefaultMechKindDefName = "MAP_Mech_Justice";

        private PawnKindDef? mechKind;

        public PawnKindDef? SelectedMechKind => mechKind;

        private IEnumerable<PawnKindDef> PossibleMechs =>
            DefDatabase<PawnKindDef>.AllDefs.Where(kind =>
                kind.RaceProps.IsMechanoid
                && kind.race.GetCompProperties<CompProperties_OverseerSubject>() != null);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref mechKind, "mechKind");
        }

        public override void DoEditInterface(Listing_ScenEdit listing)
        {
            Rect rect = listing.GetScenPartRect(this, ScenPart.RowHeight);
            string label = mechKind != null
                ? mechKind.LabelCap
                : "RandomMech".Translate().CapitalizeFirst();

            if (!Widgets.ButtonText(rect, label))
            {
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption(
                    "RandomMech".Translate().CapitalizeFirst(),
                    () => mechKind = null)
            };

            foreach (PawnKindDef possibleMech in PossibleMechs.OrderBy(kind => kind.label))
            {
                PawnKindDef localKind = possibleMech;
                options.Add(new FloatMenuOption(
                    localKind.LabelCap,
                    () => mechKind = localKind));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        public override void Randomize()
        {
            mechKind = PossibleMechs.RandomElementWithFallback(GetDefaultMechKind());
        }

        public override string Summary(Scenario scen)
        {
            return ScenSummaryList.SummaryWithList(
                scen,
                "PlayerStartsWith",
                ScenPart_StartingThing_Defined.PlayerStartWithIntro);
        }

        public override IEnumerable<string> GetSummaryListEntries(string tag)
        {
            if (tag != "PlayerStartsWith")
            {
                yield break;
            }

            PawnKindDef? selected = ResolveSelectedMechKind();
            if (selected != null)
            {
                yield return "机械族机械师：" + selected.LabelCap;
            }
        }

        public override IEnumerable<Thing> PlayerStartingThings()
        {
            PawnKindDef? selected = ResolveSelectedMechKind();
            if (selected == null)
            {
                Log.Error("[MAP_MechanoidMechanitor] No valid mechanoid kind is available for the scenario mechanitor.");
                yield break;
            }

            PawnGenerationRequest request = new PawnGenerationRequest(
                selected,
                Faction.OfPlayer,
                PawnGenerationContext.NonPlayer,
                null,
                forceGenerateNewPawn: false,
                allowDead: false,
                allowDowned: false,
                canGeneratePawnRelations: true,
                mustBeCapableOfViolence: false,
                1f,
                forceAddFreeWarmLayerIfNeeded: false,
                allowGay: true,
                allowPregnant: false,
                allowFood: true,
                allowAddictions: true,
                inhabitant: false,
                certainlyBeenInCryptosleep: false,
                forceRedressWorldPawnIfFormerColonist: false,
                worldPawnFactionDoesntMatter: false,
                0f,
                0f,
                null,
                1f,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                forceNoIdeo: false,
                forceNoBackstory: false,
                forbidAnyTitle: false,
                forceDead: false,
                null,
                null,
                null,
                null,
                null,
                0f,
                DevelopmentalStage.Newborn);

            Pawn pawn = PawnGenerator.GeneratePawn(request);
            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn)
                && !MechanoidMechanitorRoleUtility.PromoteToAcquiredMechanoidMechanitor(pawn))
            {
                Log.Error(
                    "[MAP_MechanoidMechanitor] Failed to promote the selected scenario mechanoid to acquired mechanitor status.");
            }

            MechanoidMechanitorRoleUtility.EnsureRoleState(pawn);
            GameComponent_MechanoidMechanitorRegistry.RegisterScenarioPawn(pawn);
            yield return pawn;
        }

        public override int GetHashCode()
        {
            return base.GetHashCode() ^ (mechKind?.GetHashCode() ?? 0);
        }

        private PawnKindDef? ResolveSelectedMechKind()
        {
            if (mechKind != null)
            {
                return mechKind;
            }

            mechKind = GetDefaultMechKind() ?? PossibleMechs.RandomElementWithFallback();
            return mechKind;
        }

        private static PawnKindDef? GetDefaultMechKind()
        {
            return DefDatabase<PawnKindDef>.GetNamedSilentFail(DefaultMechKindDefName);
        }
    }
}
