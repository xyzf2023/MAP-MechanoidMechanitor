using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class QuestNode_Root_JusticeBossGroup : QuestNode
    {
        protected override bool TestRunInt(Slate slate)
        {
            return slate.Exists("bossgroup")
                && slate.Exists("map")
                && slate.Exists("reward");
        }

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            Quest quest = QuestGen.quest;
            Map map = slate.Get<Map>("map");
            BossgroupDef bossgroupDef = slate.Get<BossgroupDef>("bossgroup");
            ThingDef rewardDef = slate.Get<ThingDef>("reward");
            if (map == null
                || bossgroupDef?.boss?.kindDef == null
                || rewardDef == null)
            {
                GameComponent_JusticeBossCallTracker.Current?.Clear();
                return;
            }

            Faction? faction = Faction.OfMechanoids;
            if (faction == null)
            {
                List<FactionRelation> relations = new List<FactionRelation>();
                foreach (Faction other in Find.FactionManager.AllFactionsListForReading)
                {
                    relations.Add(
                        new FactionRelation
                        {
                            other = other,
                            kind = FactionRelationKind.Hostile,
                        });
                }

                faction = FactionGenerator.NewGeneratedFactionWithRelations(
                    new FactionGeneratorParms(
                        FactionDefOf.Mechanoid,
                        default(IdeoGenerationParms),
                        hidden: true),
                    relations);
                faction.temporary = true;
                Find.FactionManager.Add(faction);
            }

            PawnGenerationRequest request = new PawnGenerationRequest(
                bossgroupDef.boss.kindDef,
                faction,
                PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true);
            Pawn justice = PawnGenerator.GeneratePawn(request);
            Find.WorldPawns.PassToWorld(justice);

            GameComponent_JusticeBossCallTracker? tracker =
                GameComponent_JusticeBossCallTracker.Current;
            tracker?.SetJusticePawn(justice);

            slate.Set("mapParent", map.Parent);
            slate.Set("justiceBoss", justice);
            List<Pawn> bosses = new List<Pawn> { justice };
            slate.Set("escortees", bosses);

            IntVec3 dropCenter = DropCellFinder.FindRaidDropCenterDistant(map);
            map.attackTargetsCache.UpdateTarget(justice);

            string arriveSignal = QuestGen.GenerateNewSignal("BossgroupArrives");
            QuestPart_BossgroupArrives arrives = new QuestPart_BossgroupArrives
            {
                mapParent = map.Parent,
                bossgroupDef = bossgroupDef,
                minDelay = JusticeBossCallUtility.ArrivalMinDelayTicksRange.RandomInRange,
                maxDelay = JusticeBossCallUtility.ArrivalMaxDelayTicksRange.RandomInRange,
                inSignalEnable = QuestGen.slate.Get<string>("inSignal"),
            };
            arrives.outSignalsCompleted.Add(arriveSignal);
            quest.AddPart(arrives);

            quest.DropPods(
                map.Parent,
                bosses,
                customLetterLabel: null,
                customLetterLabelRules: null,
                customLetterText: null,
                customLetterTextRules: null,
                sendStandardLetter: false,
                useTradeDropSpot: false,
                joinPlayer: false,
                makePrisoners: false,
                inSignal: arriveSignal,
                thingsToExcludeFromHyperlinks: null,
                signalListenMode: QuestPart.SignalListenMode.OngoingOnly,
                dropSpot: dropCenter,
                destroyItemsOnCleanup: true,
                dropAllInSamePod: false,
                allowFogged: false,
                canRetargetAnyMap: false,
                faction: faction);

            quest.Letter(
                LetterDefOf.NeutralEvent,
                null,
                null,
                label: "LetterLabelBossgroupSummoned".Translate(bossgroupDef.boss.kindDef.LabelCap),
                text: "LetterBossgroupSummoned".Translate(faction.NameColored.ToString()).ToString(),
                relatedFaction: faction);

            quest.Letter(
                LetterDefOf.Bossgroup,
                label: "MAP_MechanoidMechanitor.JusticeBoss.Letter.ArrivedLabel".Translate(),
                inSignal: arriveSignal,
                chosenPawnSignal: null,
                text: "MAP_MechanoidMechanitor.JusticeBoss.Letter.ArrivedText".Translate(),
                relatedFaction: faction,
                useColonistsOnMap: null,
                useColonistsFromCaravanArg: false,
                signalListenMode: QuestPart.SignalListenMode.OngoingOnly,
                lookTargets: bosses);

            QuestPart_JusticeBossGroup part = new QuestPart_JusticeBossGroup
            {
                inSignal = arriveSignal,
                faction = faction,
                mapParent = map.Parent,
                stageLocation = dropCenter,
            };
            part.pawns.Add(justice);
            part.bosses.Add(justice);
            quest.AddPart(part);

            quest.Alert(
                "AlertBossgroupIncoming".Translate(bossgroupDef.boss.kindDef.LabelCap),
                "AlertBossgroupIncomingDesc".Translate(bossgroupDef.boss.kindDef.label),
                null,
                critical: true,
                getLookTargetsFromSignal: false,
                null,
                arriveSignal);

            string killedLeavingsSignal =
                QuestGenUtility.HardcodedSignalWithQuestID("escortees.KilledLeavingsLeft");
            quest.ThingAnalyzed(
                rewardDef,
                delegate
                {
                    quest.Letter(
                        LetterDefOf.PositiveEvent,
                        null,
                        null,
                        null,
                        null,
                        useColonistsFromCaravanArg: false,
                        QuestPart.SignalListenMode.OngoingOnly,
                        null,
                        filterDeadPawnsFromLookTargets: false,
                        "[bossDefeatedLetterText]",
                        null,
                        "[bossDefeatedLetterLabel]");
                },
                delegate
                {
                    quest.Letter(
                        LetterDefOf.PositiveEvent,
                        null,
                        null,
                        null,
                        null,
                        useColonistsFromCaravanArg: false,
                        QuestPart.SignalListenMode.OngoingOnly,
                        null,
                        filterDeadPawnsFromLookTargets: false,
                        "[bossDefeatedStudyChipLetterText]",
                        null,
                        "[bossDefeatedLetterLabel]");
                },
                killedLeavingsSignal);

            quest.AnyPawnAlive(
                bosses,
                null,
                delegate
                {
                    QuestGen_End.End(quest, QuestEndOutcome.Unknown);
                },
                QuestGenUtility.HardcodedSignalWithQuestID("escortees.Killed"));

            quest.End(
                QuestEndOutcome.Unknown,
                0,
                null,
                QuestGenUtility.HardcodedSignalWithQuestID("mapParent.Destroyed"));
        }
    }
}
