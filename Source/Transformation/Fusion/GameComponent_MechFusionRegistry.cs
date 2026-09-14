using System;
using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体资格的唯一权威注册表。CompMechFusionInnate 负责为新 Pawn 自动登记，
    /// 已登记记录（包括 DEV 授权）则作为持久化事实来源；PawnKindDef 与 Hediff
    /// 不参与 Fusion 授予。形态与本次合体实例分别由
    /// GameComponent_MechTransformationRegistry 和
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
                || pawn.Discarded)
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

        /// <summary>
        /// 只读资格记录快照：新集合披露，不暴露内部可变列表；
        /// 过滤 null、Discarded 与重复记录。DEV 窗口只读取快照。
        /// </summary>
        public static IReadOnlyList<MechFusionEligibilityRecord>
            GetEligibilityRecordSnapshot()
        {
            GameComponent_MechFusionRegistry? registry = CurrentRegistry;
            if (registry == null)
            {
                return Array.Empty<MechFusionEligibilityRecord>();
            }

            List<MechFusionEligibilityRecord> snapshot =
                new List<MechFusionEligibilityRecord>();
            HashSet<Pawn> seen = new HashSet<Pawn>();
            List<MechFusionEligibilityRecord> records =
                registry.eligibilityRecords;
            for (int i = 0; i < records.Count; i++)
            {
                MechFusionEligibilityRecord? record = records[i];
                Pawn? pawn = record?.Pawn;
                if (record == null
                    || pawn == null
                    || pawn.Discarded
                    || !seen.Add(pawn))
                {
                    continue;
                }

                snapshot.Add(record);
            }

            return snapshot;
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

        /// <summary>
        /// 开发者模式持久注册入口：允许把任意有效、未死亡、未 Destroyed、
        /// 未 Discarded 的机械族 Pawn 加入合体资格注册表，不要求其带
        /// CompMechFusionInnate；记录会随存档保存并在读档后继续生效。
        /// </summary>
        public static bool TryRegisterFromDebug(Pawn? pawn)
        {
            GameComponent_MechFusionRegistry? registry = CurrentRegistry;
            if (registry == null
                || pawn == null
                || pawn.Dead
                || pawn.Destroyed
                || pawn.Discarded
                || pawn.RaceProps == null
                || !pawn.RaceProps.IsMechanoid)
            {
                return false;
            }

            registry.EnsureIndexes();
            if (registry.recordByPawn!.ContainsKey(pawn))
            {
                return false;
            }

            MechFusionEligibilityRecord created =
                new MechFusionEligibilityRecord(pawn);
            registry.eligibilityRecords.Add(created);
            registry.recordByPawn[pawn] = created;
            return true;
        }

        public static bool TryUnregisterFromDebug(Pawn? pawn)
        {
            return RemoveEligibility(pawn);
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
            RegisterEligiblePawnsInWorld();
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
