using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MAP_MechanoidMechanitor;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class MechanoidMechanitorScenarioUtility
    {
        public const string ScenarioPartDefName = "MAP_MechanoidMechanitorScenario";
        public const string MechanitorStartPartDefName = "MAP_MechanoidMechanitorStart";
        public const string DefaultMechKindDefName = "MAP_Mech_Justice";

        private static readonly AccessTools.FieldRef<Scenario, List<ScenPart>> ScenarioParts =
            AccessTools.FieldRefAccess<Scenario, List<ScenPart>>("parts");

        private static bool syncingBoundParts;

        public static bool IsSyncingBoundParts => syncingBoundParts;

        public static ScenPart_MechanoidMechanitorScenario? ActiveScenarioPart =>
            Find.Scenario?.AllParts.OfType<ScenPart_MechanoidMechanitorScenario>().FirstOrDefault();

        public static ScenPart_MechanoidMechanitor? MechanitorStartPart =>
            Find.Scenario?.AllParts.OfType<ScenPart_MechanoidMechanitor>().FirstOrDefault();

        public static bool IsScenarioActive => ActiveScenarioPart != null;

        public static bool ScenarioContainsMarker(Scenario? scen) =>
            scen != null
            && scen.AllParts.OfType<ScenPart_MechanoidMechanitorScenario>().Any();

        public static bool HasLivingMechanicalConsciousnessHost
        {
            get
            {
                if (!IsScenarioActive)
                {
                    return false;
                }

                Pawn? host = GameComponent_MechanoidMechanitorRegistry.CurrentMechanicalConsciousnessHost;
                return host != null
                    && !host.Dead
                    && !host.Destroyed
                    && host.Faction == Faction.OfPlayer;
            }
        }

        public static bool ShouldPreventGameOver =>
            IsScenarioActive && HasLivingMechanicalConsciousnessHost;

        public static bool HasGameEndedLetter
        {
            get
            {
                if (Current.Game == null || Find.LetterStack == null)
                {
                    return false;
                }

                return Find.LetterStack.LettersListForReading.Any(letter =>
                    letter.def == LetterDefOf.GameEnded);
            }
        }

        public static void CancelGameOverState(GameEnder gameEnder)
        {
            gameEnder.gameEnding = false;

            if (Current.Game == null || Find.LetterStack == null)
            {
                return;
            }

            for (int i = Find.LetterStack.LettersListForReading.Count - 1; i >= 0; i--)
            {
                Letter letter = Find.LetterStack.LettersListForReading[i];
                if (letter.def == LetterDefOf.GameEnded)
                {
                    Find.LetterStack.RemoveLetter(letter);
                }
            }
        }

        public static void ClearOrdinaryStartingPawnData()
        {
            GameInitData? initData = Find.GameInitData;
            if (initData == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] GameInitData 为 null，无法清理普通开局角色数据。");
                return;
            }

            // ClearAllStartingPawns 已处理 startingAndOptionalPawns、startingPossessions、
            // 关系清理、组件移除与世界 Pawn 丢弃。
            StartingPawnUtility.ClearAllStartingPawns();
            initData.startingPawnCount = 0;
            initData.startingPawnKind = null;
            initData.startingPawnsRequired = null;
            initData.startingXenotypesRequired = null;
            initData.startingMutantsRequired = null;
            initData.startingSkillsRequired = null;
            initData.allowedDevelopmentalStages =
                DevelopmentalStage.Baby
                | DevelopmentalStage.Child
                | DevelopmentalStage.Adult;
        }

        public static void NormalizeScenarioParts(Scenario? scen)
        {
            if (scen == null || syncingBoundParts)
            {
                return;
            }

            syncingBoundParts = true;
            try
            {
                DeduplicatePartsByType<ScenPart_MechanoidMechanitorScenario>(scen);
                DeduplicatePartsByType<ScenPart_MechanoidMechanitor>(scen);

                bool hasScenarioMarker = scen.AllParts
                    .OfType<ScenPart_MechanoidMechanitorScenario>()
                    .Any();

                if (hasScenarioMarker)
                {
                    EnsureMechanitorStartPartInternal(scen, resetExistingSelection: false);
                }
                else
                {
                    RemoveAllMechanitorStartPartsInternal(scen);
                }
            }
            finally
            {
                syncingBoundParts = false;
            }
        }

        public static void EnsureMechanitorStartPartAfterScenarioAdded(Scenario? scen)
        {
            if (scen == null || syncingBoundParts)
            {
                return;
            }

            syncingBoundParts = true;
            try
            {
                EnsureMechanitorStartPartInternal(scen, resetExistingSelection: false);
            }
            finally
            {
                syncingBoundParts = false;
            }
        }

        public static void RemoveBoundMechanitorStartPartsAfterScenarioRemoved(Scenario? scen)
        {
            if (scen == null || syncingBoundParts)
            {
                return;
            }

            syncingBoundParts = true;
            try
            {
                RemoveAllMechanitorStartPartsInternal(scen);
            }
            finally
            {
                syncingBoundParts = false;
            }
        }

        public static PawnKindDef? GetDefaultMechKind() =>
            DefDatabase<PawnKindDef>.GetNamedSilentFail(DefaultMechKindDefName);

        private static void EnsureMechanitorStartPartInternal(
            Scenario scen,
            bool resetExistingSelection)
        {
            List<ScenPart_MechanoidMechanitor> existing = scen.AllParts
                .OfType<ScenPart_MechanoidMechanitor>()
                .ToList();

            if (existing.Count > 0)
            {
                for (int i = 1; i < existing.Count; i++)
                {
                    scen.RemovePart(existing[i]);
                }

                if (resetExistingSelection)
                {
                    existing[0].ApplyDefaultMechKind();
                }
                else
                {
                    existing[0].EnsureValidOrDefaultMechKind();
                }

                return;
            }

            ScenPartDef? startDef = DefDatabase<ScenPartDef>.GetNamedSilentFail(
                MechanitorStartPartDefName);
            if (startDef == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 缺少 ScenPartDef " +
                    MechanitorStartPartDefName +
                    "，无法自动追加机械族机械师词条。");
                return;
            }

            ScenPart created = ScenarioMaker.MakeScenPart(startDef);
            if (created is not ScenPart_MechanoidMechanitor mechanitorPart)
            {
                Log.Error(
                    "[MAP-机械族机械师] 无法创建 ScenPart_MechanoidMechanitor 实例。");
                return;
            }

            mechanitorPart.ApplyDefaultMechKind();
            ScenarioParts(scen).Add(mechanitorPart);
        }

        private static void RemoveAllMechanitorStartPartsInternal(Scenario scen)
        {
            List<ScenPart_MechanoidMechanitor> toRemove = scen.AllParts
                .OfType<ScenPart_MechanoidMechanitor>()
                .ToList();

            for (int i = 0; i < toRemove.Count; i++)
            {
                scen.RemovePart(toRemove[i]);
            }
        }

        private static void DeduplicatePartsByType<T>(Scenario scen) where T : ScenPart
        {
            List<T> parts = scen.AllParts.OfType<T>().ToList();
            for (int i = 1; i < parts.Count; i++)
            {
                scen.RemovePart(parts[i]);
            }
        }
    }
}
