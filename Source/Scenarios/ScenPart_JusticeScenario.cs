using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class ScenPart_JusticeScenario : ScenPart
    {
        public PawnKindDef? startingPawnKind;
        public int startingPawnCount = 1;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref startingPawnKind, "startingPawnKind");
            Scribe_Values.Look(ref startingPawnCount, "startingPawnCount", 1);
        }

        public override void PostIdeoChosen()
        {
            base.PostIdeoChosen();
            GenerateStartingJusticePawns();
        }

        public override string Summary(Scenario scen)
        {
            return string.Empty;
        }

        public override bool HasNullDefs()
        {
            return base.HasNullDefs() || startingPawnKind == null;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            if (startingPawnKind == null)
            {
                yield return "startingPawnKind is null";
            }
            else if (!startingPawnKind.RaceProps.IsMechanoid)
            {
                yield return $"startingPawnKind {startingPawnKind.defName} is not a mechanoid";
            }

            if (startingPawnCount < 1)
            {
                yield return "startingPawnCount must be at least 1";
            }
        }

        private void GenerateStartingJusticePawns()
        {
            GameInitData? initData = Find.GameInitData;
            if (initData == null)
            {
                Log.Error("[MechanoidMechanitor] Cannot generate Justice starting pawn because GameInitData is null.");
                return;
            }

            if (startingPawnKind == null)
            {
                Log.Error("[MechanoidMechanitor] Cannot generate Justice starting pawn because startingPawnKind is null.");
                return;
            }

            int count = startingPawnCount < 1 ? 1 : startingPawnCount;

            StartingPawnUtility.ClearAllStartingPawns();
            initData.startingPawnKind = startingPawnKind;
            initData.startingPawnCount = count;
            initData.startingPawnsRequired = null;
            initData.startingXenotypesRequired = null;
            initData.startingMutantsRequired = null;

            for (int i = 0; i < count; i++)
            {
                PawnGenerationRequest request = CreateGenerationRequest(startingPawnKind);
                StartingPawnUtility.SetGenerationRequest(i, request);

                Pawn pawn = PawnGenerator.GeneratePawn(request);
                PrepareJusticeForStartingPawnFlow(pawn);

                initData.startingAndOptionalPawns.Add(pawn);
                StartingPawnUtility.GeneratePossessions(pawn);
            }
        }

        private static PawnGenerationRequest CreateGenerationRequest(PawnKindDef pawnKind)
        {
            return new PawnGenerationRequest(
                pawnKind,
                Faction.OfPlayer,
                PawnGenerationContext.NonPlayer,
                null,
                forceGenerateNewPawn: true,
                allowDead: false,
                allowDowned: false,
                canGeneratePawnRelations: false,
                mustBeCapableOfViolence: false,
                colonistRelationChanceFactor: 0f,
                forceAddFreeWarmLayerIfNeeded: false,
                allowGay: true,
                allowPregnant: false,
                allowFood: true,
                allowAddictions: false,
                inhabitant: false,
                certainlyBeenInCryptosleep: false,
                forceRedressWorldPawnIfFormerColonist: false,
                worldPawnFactionDoesntMatter: false,
                biocodeWeaponChance: 0f,
                biocodeApparelChance: 0f,
                extraPawnForExtraRelationChance: null,
                relationWithExtraPawnChanceFactor: 0f,
                validatorPreGear: null,
                validatorPostGear: null,
                forcedTraits: null,
                prohibitedTraits: null,
                minChanceToRedressWorldPawn: null,
                fixedBiologicalAge: null,
                fixedChronologicalAge: null,
                fixedGender: null,
                fixedMelanin: null,
                fixedLastName: null,
                fixedBirthName: null,
                fixedTitle: null,
                forceNoIdeo: false,
                forceNoBackstory: false,
                forbidAnyTitle: false,
                forceDead: false,
                forcedXenotype: null,
                forcedCustomXenotype: null,
                allowedXenotypes: null,
                forceBaselinerChance: 0f,
                developmentalStages: DevelopmentalStage.Newborn);
        }

        private static void PrepareJusticeForStartingPawnFlow(Pawn pawn)
        {
            if (pawn.relations == null)
            {
                pawn.relations = new Pawn_RelationsTracker(pawn);
            }

            pawn.relations.everSeenByPlayer = true;

            PawnComponentsUtility.AddComponentsForSpawn(pawn);

            pawn.GetComp<CompCommanderSkills>()?.PostSpawnSetup(respawningAfterLoad: false);
            VanillaRelayMechanitorUtility.EnsureVanillaRelayMechanitorState(pawn);
            CompWorkTabVisibleUser.EnsureWorkSettingsForWorkTab(pawn);

            if (pawn.skills == null)
            {
                pawn.skills = new Pawn_SkillTracker(pawn);
            }

            if (pawn.story == null)
            {
                pawn.story = new Pawn_StoryTracker(pawn);
            }

            if (pawn.workSettings == null)
            {
                pawn.workSettings = new Pawn_WorkSettings(pawn);
                pawn.workSettings.EnableAndInitialize();
                MechWorkSettingsUtility.RestrictToMechEnabledWorkTypes(pawn);
            }
        }
    }
}
