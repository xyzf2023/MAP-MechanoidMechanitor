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
            IsScenarioActive && !GameComponent_MechanoidStoryDeparture.UseVanillaEnding
            && (HasLivingMechanicalConsciousnessHost
                || GameComponent_MechanoidStoryDeparture.ProtectRemainingMechanitors);

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
                    EnsureBoundGroupOrderInternal(scen, placeAtStart: false);
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
                EnsureBoundGroupOrderInternal(scen, placeAtStart: true);
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

        public static void EnsureEditorBoundGroupInvariant(Scenario? scen)
        {
            if (scen == null || syncingBoundParts)
            {
                return;
            }

            List<ScenPart> parts = ScenarioParts(scen);
            int markerCount = 0;
            int startCount = 0;
            int markerIndex = -1;
            int startIndex = -1;

            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] is ScenPart_MechanoidMechanitorScenario)
                {
                    markerCount++;
                    if (markerIndex < 0)
                    {
                        markerIndex = i;
                    }
                }
                else if (parts[i] is ScenPart_MechanoidMechanitor)
                {
                    startCount++;
                    if (startIndex < 0)
                    {
                        startIndex = i;
                    }
                }
            }

            if (markerCount == 0)
            {
                return;
            }

            if (markerCount == 1
                && startCount == 1
                && startIndex == markerIndex + 1)
            {
                return;
            }

            NormalizeScenarioParts(scen);
        }

        public static bool TryGetBoundGroupCanReorder(
            Scenario scen,
            ScenPart part,
            ReorderDirection dir,
            out bool canReorder)
        {
            canReorder = false;

            if (!TryGetAdjacentBoundGroup(
                    scen,
                    out _,
                    out _,
                    out int markerIndex,
                    out int startIndex))
            {
                return false;
            }

            List<ScenPart> parts = ScenarioParts(scen);

            if (part is ScenPart_MechanoidMechanitor)
            {
                canReorder = false;
                return true;
            }

            if (part is ScenPart_MechanoidMechanitorScenario)
            {
                if (dir == ReorderDirection.Up)
                {
                    if (markerIndex == 0)
                    {
                        canReorder = false;
                    }
                    else if (!parts[markerIndex - 1].def.PlayerAddRemovable)
                    {
                        canReorder = false;
                    }
                    else
                    {
                        canReorder = true;
                    }
                }
                else
                {
                    canReorder = startIndex < parts.Count - 1;
                }

                return true;
            }

            if (!part.def.PlayerAddRemovable)
            {
                return false;
            }

            int partIndex = parts.IndexOf(part);
            if (partIndex < 0)
            {
                return false;
            }

            if (dir == ReorderDirection.Down && partIndex == markerIndex - 1)
            {
                canReorder = true;
                return true;
            }

            if (dir == ReorderDirection.Up && partIndex == startIndex + 1)
            {
                canReorder = true;
                return true;
            }

            return false;
        }

        public static bool TryReorderBoundGroup(
            Scenario scen,
            ScenPart part,
            ReorderDirection dir)
        {
            if (syncingBoundParts)
            {
                return false;
            }

            if (!TryGetAdjacentBoundGroup(
                    scen,
                    out _,
                    out _,
                    out int markerIndex,
                    out int startIndex))
            {
                if (part is ScenPart_MechanoidMechanitor
                    && ScenarioContainsMarker(scen))
                {
                    return true;
                }

                return false;
            }

            List<ScenPart> parts = ScenarioParts(scen);

            if (part is ScenPart_MechanoidMechanitor)
            {
                return true;
            }

            if (part is ScenPart_MechanoidMechanitorScenario)
            {
                if (dir == ReorderDirection.Up)
                {
                    if (markerIndex <= 0
                        || !parts[markerIndex - 1].def.PlayerAddRemovable)
                    {
                        return true;
                    }

                    MoveBoundGroupUp(parts, markerIndex);
                    return true;
                }

                if (startIndex >= parts.Count - 1)
                {
                    return true;
                }

                MoveBoundGroupDown(parts, markerIndex);
                return true;
            }

            int partIndex = parts.IndexOf(part);
            if (partIndex < 0)
            {
                return false;
            }

            if (dir == ReorderDirection.Down && partIndex == markerIndex - 1)
            {
                MoveBoundGroupUp(parts, markerIndex);
                return true;
            }

            if (dir == ReorderDirection.Up && partIndex == startIndex + 1)
            {
                MoveBoundGroupDown(parts, markerIndex);
                return true;
            }

            return false;
        }

        public static void EnsureBoundGroupAfterReorder(Scenario? scen)
        {
            if (scen == null || syncingBoundParts || !ScenarioContainsMarker(scen))
            {
                return;
            }

            if (TryGetAdjacentBoundGroup(scen, out _, out _, out _, out _))
            {
                return;
            }

            syncingBoundParts = true;
            try
            {
                EnsureBoundGroupOrderInternal(scen, placeAtStart: false);
            }
            finally
            {
                syncingBoundParts = false;
            }
        }

        public static PawnKindDef? GetDefaultMechKind() =>
            DefDatabase<PawnKindDef>.GetNamedSilentFail(DefaultMechKindDefName);

        private static void EnsureBoundGroupOrderInternal(Scenario scen, bool placeAtStart)
        {
            List<ScenPart> parts = ScenarioParts(scen);
            if (!TryGetBoundParts(
                    parts,
                    out ScenPart_MechanoidMechanitorScenario marker,
                    out ScenPart_MechanoidMechanitor start,
                    out int markerIndex,
                    out int startIndex))
            {
                return;
            }

            if (placeAtStart)
            {
                parts.Remove(marker);
                parts.Remove(start);
                parts.Insert(0, marker);
                parts.Insert(1, start);
                return;
            }

            if (startIndex == markerIndex + 1)
            {
                return;
            }

            parts.Remove(start);
            markerIndex = parts.IndexOf(marker);
            if (markerIndex < 0)
            {
                return;
            }

            parts.Insert(markerIndex + 1, start);
        }

        private static bool TryGetAdjacentBoundGroup(
            Scenario scen,
            out ScenPart_MechanoidMechanitorScenario marker,
            out ScenPart_MechanoidMechanitor start,
            out int markerIndex,
            out int startIndex)
        {
            if (!TryGetBoundParts(
                    ScenarioParts(scen),
                    out marker,
                    out start,
                    out markerIndex,
                    out startIndex))
            {
                return false;
            }

            return startIndex == markerIndex + 1;
        }

        private static bool TryGetBoundParts(
            List<ScenPart> parts,
            out ScenPart_MechanoidMechanitorScenario marker,
            out ScenPart_MechanoidMechanitor start,
            out int markerIndex,
            out int startIndex)
        {
            marker = null!;
            start = null!;
            markerIndex = -1;
            startIndex = -1;

            for (int i = 0; i < parts.Count; i++)
            {
                if (markerIndex < 0 && parts[i] is ScenPart_MechanoidMechanitorScenario markerPart)
                {
                    marker = markerPart;
                    markerIndex = i;
                }
                else if (startIndex < 0 && parts[i] is ScenPart_MechanoidMechanitor startPart)
                {
                    start = startPart;
                    startIndex = i;
                }

                if (markerIndex >= 0 && startIndex >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void MoveBoundGroupUp(List<ScenPart> parts, int markerIndex)
        {
            ScenPart above = parts[markerIndex - 1];
            ScenPart marker = parts[markerIndex];
            ScenPart start = parts[markerIndex + 1];
            parts[markerIndex - 1] = marker;
            parts[markerIndex] = start;
            parts[markerIndex + 1] = above;
        }

        private static void MoveBoundGroupDown(List<ScenPart> parts, int markerIndex)
        {
            ScenPart marker = parts[markerIndex];
            ScenPart start = parts[markerIndex + 1];
            ScenPart below = parts[markerIndex + 2];
            parts[markerIndex] = below;
            parts[markerIndex + 1] = marker;
            parts[markerIndex + 2] = start;
        }

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
