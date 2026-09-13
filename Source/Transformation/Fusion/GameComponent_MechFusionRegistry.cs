using System;
using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 先天合体资格的唯一权威注册表。资格只能来自机械族 ThingDef 上的
    /// CompMechFusionInnate 标记；PawnKindDef 与 Hediff 不再参与 Fusion 授予。
    /// 形态与本次合体实例分别由 GameComponent_MechTransformationRegistry 和
    /// GameComponent_MechFusionSessionRegistry 管理，三者不混用。
    /// </summary>
    public sealed class GameComponent_MechFusionRegistry : GameComponent
    {
        private List<MechFusionEligibilityRecord> eligibilityRecords =
            new List<MechFusionEligibilityRecord>();

        private Dictionary<Pawn, MechFusionEligibilityRecord>? recordByPawn;

        private static Game? cachedRegistryGame;
        private static GameComponent_MechFusionRegistry? cachedRegistry;

        public GameComponent_MechFusionRegistry(Game game)
        {
        }

        private static GameComponent_MechFusionRegistry? CurrentRegistry
        {
            get
            {
                Game? game = Current.Game;
                if (game == null)
                {
                    cachedRegistryGame = null;
                    cachedRegistry = null;
                    return null;
                }

                if (!ReferenceEquals(cachedRegistryGame, game))
                {
                    cachedRegistryGame = game;
                    cachedRegistry = game.GetComponent<GameComponent_MechFusionRegistry>();
                }
                else if (cachedRegistry == null)
                {
                    cachedRegistry = game.GetComponent<GameComponent_MechFusionRegistry>();
                }

                return cachedRegistry;
            }
        }

        public static bool HasEligibility(Pawn? pawn)
        {
            if (pawn == null
                || pawn.Destroyed
                || pawn.Discarded
                || !MechFusionEligibilityUtility.HasInnateFusionMarker(pawn))
            {
                return false;
            }

            GameComponent_MechFusionRegistry? registry = CurrentRegistry;
            if (registry == null)
            {
                return false;
            }

            registry.EnsureIndexes();
            return registry.recordByPawn!.ContainsKey(pawn);
        }

        public static bool TryGetRecord(
            Pawn? pawn,
            out MechFusionEligibilityRecord? record)
        {
            record = null;
            GameComponent_MechFusionRegistry? registry = CurrentRegistry;
            if (registry == null || pawn == null)
            {
                return false;
            }

            registry.EnsureIndexes();
            return registry.recordByPawn!.TryGetValue(pawn, out record);
        }

        internal static bool EnsureEligibilityRecord(Pawn? pawn)
        {
            if (pawn == null
                || pawn.Destroyed
                || pawn.Discarded
                || !MechFusionEligibilityUtility.HasInnateFusionMarker(pawn))
            {
                return false;
            }

            GameComponent_MechFusionRegistry? registry = CurrentRegistry;
            if (registry == null)
            {
                return false;
            }

            registry.EnsureIndexes();
            if (registry.recordByPawn!.ContainsKey(pawn))
            {
                return true;
            }

            MechFusionEligibilityRecord created =
                new MechFusionEligibilityRecord(pawn);
            registry.eligibilityRecords.Add(created);
            registry.recordByPawn[pawn] = created;
            return true;
        }

        internal static bool RemoveEligibility(Pawn? pawn)
        {
            GameComponent_MechFusionRegistry? registry = CurrentRegistry;
            if (registry == null || pawn == null)
            {
                return false;
            }

            registry.EnsureIndexes();
            if (!registry.recordByPawn!.TryGetValue(
                    pawn,
                    out MechFusionEligibilityRecord? record)
                || record == null)
            {
                return false;
            }

            registry.eligibilityRecords.Remove(record);
            registry.recordByPawn.Remove(pawn);
            return true;
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(
                ref eligibilityRecords,
                "mechFusionEligibilityRecords",
                LookMode.Deep);
            eligibilityRecords ??= new List<MechFusionEligibilityRecord>();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                recordByPawn = null;
            }
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            cachedRegistryGame = Current.Game;
            cachedRegistry = this;
            eligibilityRecords = new List<MechFusionEligibilityRecord>();
            recordByPawn = new Dictionary<Pawn, MechFusionEligibilityRecord>();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            cachedRegistryGame = Current.Game;
            cachedRegistry = this;
            RebuildIndexes();
            ValidateAfterLoad();
        }

        private void EnsureIndexes()
        {
            if (recordByPawn == null)
            {
                RebuildIndexes();
            }
        }

        private void RebuildIndexes()
        {
            eligibilityRecords ??= new List<MechFusionEligibilityRecord>();
            recordByPawn = new Dictionary<Pawn, MechFusionEligibilityRecord>();

            for (int i = eligibilityRecords.Count - 1; i >= 0; i--)
            {
                MechFusionEligibilityRecord? record = eligibilityRecords[i];
                Pawn? pawn = record?.Pawn;
                if (record == null || pawn == null || pawn.Discarded)
                {
                    eligibilityRecords.RemoveAt(i);
                    continue;
                }

                if (recordByPawn.ContainsKey(pawn))
                {
                    eligibilityRecords.RemoveAt(i);
                    continue;
                }

                recordByPawn[pawn] = record;
            }
        }

        /// <summary>
        /// 读档后的统一校验：真实 ThingDef 已不含先天标记的 Pawn 一律移除资格；
        /// 仍含标记但旧存档缺少记录的 Pawn 补登记。
        /// </summary>
        private void ValidateAfterLoad()
        {
            for (int i = eligibilityRecords.Count - 1; i >= 0; i--)
            {
                Pawn? pawn = eligibilityRecords[i]?.Pawn;
                if (pawn == null
                    || pawn.Destroyed
                    || pawn.Discarded
                    || !MechFusionEligibilityUtility.HasInnateFusionMarker(pawn))
                {
                    if (pawn != null)
                    {
                        recordByPawn?.Remove(pawn);
                    }

                    eligibilityRecords.RemoveAt(i);
                }
            }

            RegisterEligiblePawnsInWorld();
        }

        private void RegisterEligiblePawnsInWorld()
        {
            if (Current.Game == null)
            {
                return;
            }

            WorldPawns? worldPawns = Find.WorldPawns;
            if (worldPawns != null)
            {
                List<Pawn> allWorldPawns = worldPawns.AllPawnsAliveOrDead;
                for (int i = 0; i < allWorldPawns.Count; i++)
                {
                    EnsureEligibilityRecord(allWorldPawns[i]);
                }
            }

            List<Map> maps = Find.Maps;
            if (maps == null)
            {
                return;
            }

            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (map?.mapPawns == null)
                {
                    continue;
                }

                IReadOnlyList<Pawn> spawned = map.mapPawns.AllPawnsSpawned;
                for (int j = 0; j < spawned.Count; j++)
                {
                    EnsureEligibilityRecord(spawned[j]);
                }
            }
        }
    }
}
