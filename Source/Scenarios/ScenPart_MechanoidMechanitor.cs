using System.Collections.Generic;
using System.Linq;
using MAP_MechanoidMechanitor;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class ScenPart_MechanoidMechanitor : ScenPart
    {
        private const string DefaultMechKindDefName = "MAP_Mech_Justice";
        private const int TooltipId = 684272;

        private PawnKindDef? mechKind;

        public PawnKindDef? SelectedMechKind => mechKind;

        public bool IsSelectedMechKindValid =>
            mechKind != null && PossibleMechs.Contains(mechKind);

        private IEnumerable<PawnKindDef> PossibleMechs =>
            DefDatabase<PawnKindDef>.AllDefs.Where(kind =>
                kind.RaceProps.IsMechanoid
                && kind.race.GetCompProperties<CompProperties_OverseerSubject>() != null);

        public void ApplyDefaultMechKind()
        {
            mechKind = GetDefaultMechKind();
        }

        public void EnsureValidOrDefaultMechKind()
        {
            if (mechKind == null || !PossibleMechs.Contains(mechKind))
            {
                ApplyDefaultMechKind();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref mechKind, "mechKind");
        }

        public override void DoEditInterface(Listing_ScenEdit listing)
        {
            EnsureValidOrDefaultMechKind();

            Rect rightRect = listing.GetScenPartRect(this, ScenPart.RowHeight);

            Rect fullRowRect = new Rect(
                rightRect.x - rightRect.width,
                rightRect.y,
                rightRect.width * 2f,
                rightRect.height);

            TooltipHandler.TipRegion(
                fullRowRect,
                new TipSignal(
                    "MAP_MechanoidMechanitor.Scenario.MechanitorEditor.Tooltip".Translate(),
                    TooltipId));

            string label = mechKind != null
                ? mechKind.LabelCap
                : GetDefaultMechKind()?.LabelCap ?? DefaultMechKindDefName;

            if (!Widgets.ButtonText(rightRect, label))
            {
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>();
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
            // 原版 API 要求保留；自动绑定流程会随后调用 ApplyDefaultMechKind。
            ApplyDefaultMechKind();
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

            EnsureValidOrDefaultMechKind();
            string selectedLabel = mechKind != null
                ? mechKind.LabelCap
                : GetDefaultMechKind()?.LabelCap ?? DefaultMechKindDefName;
            yield return "机械族机械师：" + selectedLabel;
        }

        public override IEnumerable<Thing> PlayerStartingThings()
        {
            PawnKindDef? selected = ResolveSelectedMechKindForGeneration();
            if (selected == null)
            {
                Log.Error("[MAP-机械族机械师] 剧本无可用机械体种类用于生成机械师。");
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
                developmentalStages: DevelopmentalStage.Adult);

            Pawn pawn = PawnGenerator.GeneratePawn(request);
            if (!MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn)
                && !MechanoidMechanitorRoleUtility.PromoteToAcquiredMechanoidMechanitor(pawn))
            {
                Log.Error(
                    "[MAP-机械族机械师] 所选剧本机械体升格为获得机械师身份失败。");
            }

            MechanoidMechanitorRoleUtility.EnsureRoleState(pawn);
            GameComponent_MechanoidMechanitorRegistry.RegisterScenarioPawn(pawn);
            yield return pawn;
        }

        public override int GetHashCode()
        {
            return base.GetHashCode() ^ (mechKind?.GetHashCode() ?? 0);
        }

        private PawnKindDef? ResolveSelectedMechKindForGeneration()
        {
            EnsureValidOrDefaultMechKind();
            return mechKind ?? GetDefaultMechKind();
        }

        private static PawnKindDef? GetDefaultMechKind()
        {
            return DefDatabase<PawnKindDef>.GetNamedSilentFail(DefaultMechKindDefName);
        }
    }
}
