using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>剧情主动离场记录；不撤销机械师身份，也不把普通死亡视作结局。</summary>
    public sealed class GameComponent_MechanoidStoryDeparture : GameComponent
    {
        private List<Pawn> departedPawns = new List<Pawn>();
        private HashSet<Pawn> departedIndex = new HashSet<Pawn>();
        private bool releaseVanillaEnding;
        private bool reconcileAfterLoad;
        internal bool processing;

        public GameComponent_MechanoidStoryDeparture(Game game) { }

        internal static GameComponent_MechanoidStoryDeparture? Current =>
            CurrentGameComponentCache<GameComponent_MechanoidStoryDeparture>.Get();

        public static bool IsProcessing => Current?.processing == true;

        public static bool HasDeparted(Pawn pawn) => pawn.Faction != Faction.OfPlayerSilentFail
            && Current?.departedIndex.Contains(pawn) == true;

        public static bool UseVanillaEnding => Current?.releaseVanillaEnding == true
            && !AnyRemainingPlayerMechanitor();

        // 分批接人带走原机械意识载体后，其他地图的机械师仍可继续游戏。
        public static bool ProtectRemainingMechanitors => Current?.departedPawns.Count > 0
            && AnyRemainingPlayerMechanitor();

        private static bool AnyRemainingPlayerMechanitor()
        {
            foreach (Pawn pawn in GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors)
                if (pawn.Faction == Faction.OfPlayerSilentFail
                    && OrbitalBackupGameEndUtility.IsLivingMechanitor(pawn)) return true;
            return false;
        }

        internal void CompleteDeparture(IReadOnlyList<Pawn> pawns)
        {
            bool added = false;
            foreach (Pawn pawn in pawns)
            {
                if (pawn.Spawned || pawn.MapHeld != null || pawn.Destroyed
                    || pawn.Faction == Faction.OfPlayerSilentFail) continue;
                if (departedIndex.Add(pawn)) departedPawns.Add(pawn);
                added = true;
            }
            if (!added) return;
            // 只在本次离场真正带走最后一人时放行；留守者日后阵亡仍走原备份流程。
            releaseVanillaEnding = !AnyRemainingPlayerMechanitor();
            if (releaseVanillaEnding) ClearBackup();
        }

        internal static void NotifyPlayerReturned(Pawn pawn)
        {
            var state = Current;
            if (state == null || pawn.Faction != Faction.OfPlayerSilentFail
                || !OrbitalBackupGameEndUtility.IsLivingMechanitor(pawn)) return;
            if (state.departedIndex.Remove(pawn)) state.departedPawns.Remove(pawn);
            if (GameComponent_MechanoidMechanitorRegistry.HasPersistentRecord(pawn))
                state.releaseVanillaEnding = false;
        }

        private static void ClearBackup()
        {
            GameComponent_OrbitalDataNetworkState.CurrentState?.CancelBackupLetterDelay();
            OrbitalBackupGameEndUtility.RemoveBackupLetter();
        }

        public override void LoadedGame() => reconcileAfterLoad = true;

        public override void GameComponentTick()
        {
            if (processing) return;
            if (releaseVanillaEnding && AnyRemainingPlayerMechanitor()) releaseVanillaEnding = false;
            if (!reconcileAfterLoad) return;
            reconcileAfterLoad = false;
            if (UseVanillaEnding) ClearBackup();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref departedPawns, "storyDepartedPawns", LookMode.Reference);
            Scribe_Values.Look(ref releaseVanillaEnding, "storyDepartureReleaseVanillaEnding", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                departedPawns ??= new List<Pawn>();
                departedPawns.RemoveAll(p => p == null || p.Discarded);
                departedIndex = new HashSet<Pawn>(departedPawns);
                processing = false;
                reconcileAfterLoad = true;
            }
        }
    }
}
