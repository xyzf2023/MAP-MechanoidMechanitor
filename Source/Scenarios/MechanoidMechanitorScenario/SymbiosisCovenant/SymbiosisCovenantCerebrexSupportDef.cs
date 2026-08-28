using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 共生盟约「支援玩家进攻奥德赛机械主巢」的可读取配置入口。
    /// 所有 tick、点数、波次、落点搜索上限集中在此，避免散落硬编码。
    /// 该机制只在 Odyssey 激活、且当前进行中的 Gravcore_Mechhive 任务上附加生效。
    /// </summary>
    public sealed class SymbiosisCovenantCerebrexSupportDef : Def
    {
        // 询问与触发节奏（ticks）
        public int offerDelayTicks = 1200;                 // 玩家进入地图后等待多久发送询问信件
        public int offerTimeoutTicks = 5000;                // 信件限时：默认 5000 tick ≈ 游戏内 2 小时
        public int firstWaveDelayTicks = 900;               // 接受援助后到第一波援军之间的延迟
        public int maxWaves = 5;                            // 最大波次（含第一波），合法范围 1..8
        public float remainingCombatCapableThreshold = 0.30f; // 仍作战人数 <= Ceil(initial * 该值) 时允许调度下一波
        public int minimumWaveAgeTicks = 2500;              // 当前波至少存在这么久才允许下一波
        public int minimumWaveIntervalTicks = 3000;         // 距上一次成功部署至少这么久
        public int failedDeploymentRetryTicks = 600;        // 整波生成失败后的重试延迟

        // 每波点数（整波合计，不是每派系各一份）
        public int minWavePoints = 10000;
        public int maxWavePoints = 25000;
        public float level2ThreatOffsetFactor = 0.15f;
        public float level3ThreatOffsetFactor = 0.25f;
        public float level4ThreatOffsetFactor = 0.35f;
        public float level5ThreatOffsetFactor = 0.50f;

        // 每波最多参与派系数与空投仓开盖延迟
        public int maxParticipantsPerWave = 3;
        public int dropPodOpenDelayTicks = 140;

        // 主脑战斗结束后的威胁稳定判定
        public int threatCheckIntervalTicks = 120;
        public int threatClearStableTicks = 1000;

        // 撤离
        public int evacuationRetryTicks = 600;
        // 撤离装载超时：仅用于重新检查/安全回退，绝不删除未登机 Pawn
        public int evacuationLoadingTimeoutTicks = 15000;

        // 边缘空投落点搜索（全部有上限，禁止全图无限扫描）
        public float dropCellSearchRadius = 80f;             // 边缘锚点搜索半径上限
        public float dropCellRadialPatternSafetyMargin = 0.1f;
        public float dropCellZoneRadius = 18f;               // 围绕锚点二次枚举的半径
        public int dropCellMinimumSpacingSquared = 4;        // 同批落点最小间距平方（约 2 格）
        public int dropCellAnchorAttemptLimit = 300;         // 边缘锚点尝试上限
        public int dropCellEdgeCandidateStep = 7;            // 外缘候选格采样步长
        public int dropCellEdgeCandidateLimit = 400;         // 外缘候选格总数上限

        // 无皇权机械撤离仓所需数量估算：每仓按实际 MassCapacity 分配，但给一个软上限避免极端数量
        public int evacPodMassCapacityFallback = 200;        // 读取不到 CompTransporter.MassCapacity 时使用的估算质量

        // 半数登机后强制发射（每艘载具独立计时、独立发射）
        public int partialLoadDepartureDelayTicks = 30000;     // 半数登机后到强制发射的延迟（30000 tick ≈ 12 小时）

        // 首次路线分类与破墙（限制性能消耗）
        public int evacuationBreachCheckIntervalTicks = 120;   // 破墙分类 / 调度检查间隔（tick）
        public int evacuationBreachInitialChecksPerInterval = 8; // 每批初步分类的 Pawn 数量

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            if (offerDelayTicks < 0)
            {
                yield return $"{defName}: offerDelayTicks cannot be negative.";
            }

            if (offerTimeoutTicks <= 0)
            {
                yield return $"{defName}: offerTimeoutTicks must be positive.";
            }

            if (firstWaveDelayTicks < 0)
            {
                yield return $"{defName}: firstWaveDelayTicks cannot be negative.";
            }

            if (maxWaves < 1 || maxWaves > 8)
            {
                yield return $"{defName}: maxWaves must be in 1..8, got {maxWaves}.";
            }

            if (remainingCombatCapableThreshold <= 0f || remainingCombatCapableThreshold >= 1f)
            {
                yield return $"{defName}: remainingCombatCapableThreshold must be strictly between 0 and 1.";
            }

            if (minimumWaveAgeTicks < 0)
            {
                yield return $"{defName}: minimumWaveAgeTicks cannot be negative.";
            }

            if (minimumWaveIntervalTicks < 0)
            {
                yield return $"{defName}: minimumWaveIntervalTicks cannot be negative.";
            }

            if (failedDeploymentRetryTicks < 0)
            {
                yield return $"{defName}: failedDeploymentRetryTicks cannot be negative.";
            }

            if (minWavePoints <= 0)
            {
                yield return $"{defName}: minWavePoints must be positive.";
            }

            if (maxWavePoints < minWavePoints)
            {
                yield return $"{defName}: maxWavePoints must be >= minWavePoints.";
            }

            if (level2ThreatOffsetFactor < 0f
                || level3ThreatOffsetFactor < 0f
                || level4ThreatOffsetFactor < 0f
                || level5ThreatOffsetFactor < 0f)
            {
                yield return $"{defName}: threat offset factors (L2..L5) cannot be negative.";
            }

            if (maxParticipantsPerWave < 1)
            {
                yield return $"{defName}: maxParticipantsPerWave must be >= 1.";
            }

            if (dropPodOpenDelayTicks < 0)
            {
                yield return $"{defName}: dropPodOpenDelayTicks cannot be negative.";
            }

            if (threatCheckIntervalTicks <= 0)
            {
                yield return $"{defName}: threatCheckIntervalTicks must be positive.";
            }

            if (threatClearStableTicks < 0)
            {
                yield return $"{defName}: threatClearStableTicks cannot be negative.";
            }

            if (evacuationRetryTicks < 0)
            {
                yield return $"{defName}: evacuationRetryTicks cannot be negative.";
            }

            if (evacuationLoadingTimeoutTicks <= 0)
            {
                yield return $"{defName}: evacuationLoadingTimeoutTicks must be positive.";
            }

            if (dropCellSearchRadius <= 0f
                || dropCellZoneRadius <= 0f
                || dropCellMinimumSpacingSquared < 0
                || dropCellAnchorAttemptLimit <= 0
                || dropCellEdgeCandidateLimit <= 0
                || dropCellEdgeCandidateStep <= 0)
            {
                yield return $"{defName}: drop cell search bounds are invalid.";
            }

            if (evacPodMassCapacityFallback <= 0)
            {
                yield return $"{defName}: evacPodMassCapacityFallback must be positive.";
            }

            if (partialLoadDepartureDelayTicks <= 0)
            {
                yield return $"{defName}: partialLoadDepartureDelayTicks must be positive.";
            }

            if (evacuationBreachCheckIntervalTicks <= 0)
            {
                yield return $"{defName}: evacuationBreachCheckIntervalTicks must be positive.";
            }

            if (evacuationBreachInitialChecksPerInterval <= 0)
            {
                yield return $"{defName}: evacuationBreachInitialChecksPerInterval must be positive.";
            }
        }
    }

    [DefOf]
    public static class SymbiosisCovenantCerebrexSupportDefOf
    {
        public static SymbiosisCovenantCerebrexSupportDef MAP_SymbiosisCovenant_CerebrexSupportConfig = null!;

        static SymbiosisCovenantCerebrexSupportDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(SymbiosisCovenantCerebrexSupportDefOf));
        }
    }

    [DefOf]
    public static class MAP_SymbiosisCovenantCerebrexSupportThoughtDefOf
    {
        public static ThoughtDef MAP_SymbiosisCovenant_CerebrexSupport_SenseOfMission = null!;

        static MAP_SymbiosisCovenantCerebrexSupportThoughtDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(MAP_SymbiosisCovenantCerebrexSupportThoughtDefOf));
        }
    }

    /// <summary>
    /// 撤离模式：默认 None；根据当前激活 DLC 在运行时决定 Royalty 穿梭机或 Odyssey 机械空投仓。
    /// </summary>
    public enum CerebrexSupportEvacMode : byte
    {
        None = 0,
        RoyaltyShuttle = 1,
        OdysseyMechPod = 2
    }

    /// <summary>
    /// 单波援军记录，随 QuestPart 存档。用于存读档恢复、阈值语义与撤离名单来源。
    /// </summary>
    public sealed class SymbiosisCovenantCerebrexSupportWaveRecord : IExposable
    {
        public int waveIndex;
        public int deployedTick;
        public int initialPawnCount;
        public List<Pawn> pawns = new List<Pawn>();
        public List<SymbiosisCovenantCerebrexSupportFactionRecord> factionRecords = new List<SymbiosisCovenantCerebrexSupportFactionRecord>();
        public string? aidTag;
        public bool nextWaveTriggered;

        public SymbiosisCovenantCerebrexSupportWaveRecord()
        {
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref waveIndex, "waveIndex", 0);
            Scribe_Values.Look(ref deployedTick, "deployedTick", 0);
            Scribe_Values.Look(ref initialPawnCount, "initialPawnCount", 0);
            Scribe_Collections.Look(ref pawns, "pawns", LookMode.Reference);
            Scribe_Collections.Look(ref factionRecords, "factionRecords", LookMode.Deep);
            Scribe_Values.Look(ref aidTag, "aidTag");
            Scribe_Values.Look(ref nextWaveTriggered, "nextWaveTriggered", false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pawns ??= new List<Pawn>();
                factionRecords ??= new List<SymbiosisCovenantCerebrexSupportFactionRecord>();
                pawns.RemoveAll(p => p == null);
            }
        }
    }

    /// <summary>
    /// 单波中单个参与派系的支援记录，随波次记录存档。
    /// </summary>
    public sealed class SymbiosisCovenantCerebrexSupportFactionRecord : IExposable
    {
        public Faction? faction;
        public float points;
        public int pawnCount;
        public string? aidTag;

        public SymbiosisCovenantCerebrexSupportFactionRecord()
        {
        }

        public SymbiosisCovenantCerebrexSupportFactionRecord(
            Faction faction,
            float points,
            int pawnCount,
            string? aidTag)
        {
            this.faction = faction;
            this.points = points;
            this.pawnCount = pawnCount;
            this.aidTag = aidTag;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref points, "points", 0f);
            Scribe_Values.Look(ref pawnCount, "pawnCount", 0);
            Scribe_Values.Look(ref aidTag, "aidTag");
        }
    }

    /// <summary>
    /// 撤离载具的明确、可存档状态机。不得再用 vehicleThing == null 同时表达
    /// “未生成 / 正在到达 / 已发射 / 已离开”。
    /// </summary>
    public enum CerebrexSupportEvacVehicleStage : byte
    {
        NotRequested = 0,
        LandingRequested = 1,
        Landed = 2,
        Loading = 3,
        Departing = 4,
        Completed = 5,
        FailedRetryable = 6,
        LoadingRetryWaiting = 7,
        SpawnRetryWaiting = 8,
        Invalid = 9,
    }

    /// <summary>
    /// 撤离载具记录，随 QuestPart 存档。不同派系分别建立各自载具，避免混淆。
    /// </summary>
    public sealed class SymbiosisCovenantCerebrexSupportEvacVehicle : IExposable
    {
        public CerebrexSupportEvacMode mode = CerebrexSupportEvacMode.None;
        public Faction? faction;
        public List<Pawn> pawns = new List<Pawn>();
        public Thing? vehicleThing;   // 穿梭机 Thing / 机械撤离仓建筑 / 正在离开的 FlyShipLeaving
        public int groupID = -1;
        public IntVec3 landingCell = IntVec3.Invalid;

        // 新状态机字段（全部需存档）
        public CerebrexSupportEvacVehicleStage stage = CerebrexSupportEvacVehicleStage.NotRequested;
        public int stateChangedTick = -1;
        public int nextRetryTick = -1;

        // 旧存档兼容字段：读档后由 PostLoadInit 迁移并清除。
        public bool podRequested;

        // 半数登机后强制发射计时（不依赖 stateChangedTick，独立计时、独立发射）
        public int partialLoadDepartureTick = -1;
        public List<Pawn> releasedPawns = new List<Pawn>();
        public int launchedPawnCount;

        // 首次路线分类与破墙：三种状态（未分类 / 普通可达 / 破墙候选），全部需存档
        public List<Pawn> breachRouteCheckedPawns = new List<Pawn>();
        public List<Pawn> breachEligiblePawns = new List<Pawn>();
        public int nextBreachCheckTick = -1;
        public int breachClassificationCursor = 0;

        public bool IsCompleted => stage == CerebrexSupportEvacVehicleStage.Completed;


        // Pawn 进入载具后不再 Spawned，但仍存活；故此处只判断“已死亡/已销毁”，
        // 是否在地图由 IsTrackedPawnStillOnSupportMap 判定。
        public bool HasLivingTrackedPawns =>
            pawns != null && pawns.Any(p => p != null && !p.Dead && !p.Destroyed && !p.Discarded);

        public bool IsTrackedPawnStillOnSupportMap(Map map) =>
            pawns != null && pawns.Any(
                p => p != null
                     && !p.Dead
                     && !p.Destroyed
                     && p.Spawned
                     && p.Map == map);

        /// <summary>
        /// 仍停留在支援地图且未被释放（计时发射时已离开载具）的撤离对象。releasedPawns
        /// 不再阻塞本载具完成。
        /// </summary>
        public bool HasUnreleasedTrackedPawnStillOnSupportMap(Map map) =>
            pawns != null && pawns.Any(
                p => p != null
                     && !p.Dead
                     && !p.Destroyed
                     && !p.Discarded
                     && p.Spawned
                     && p.Map == map
                     && (releasedPawns == null || !releasedPawns.Contains(p)));

        /// <summary>
        /// 新载具生成时清空破墙分类与半数计时；同一载具只是装载重试时不得调用此方法。
        /// </summary>
        public void ResetEvacuationBreachClassification()
        {
            breachRouteCheckedPawns = new List<Pawn>();
            breachEligiblePawns = new List<Pawn>();
            breachClassificationCursor = 0;
            nextBreachCheckTick = -1;
            partialLoadDepartureTick = -1;
        }

        public SymbiosisCovenantCerebrexSupportEvacVehicle()
        {
        }

        /// <summary>
        /// 旧存档补装：修复空列表、移除空引用、将 podRequested/vehicleThing 推断为新状态，
        /// 并清除 podRequested，避免重复生成或重复发射。
        /// </summary>
        public void PostLoadInit()
        {
            if (pawns == null)
            {
                pawns = new List<Pawn>();
            }

            pawns.RemoveAll(p => p == null || p.Destroyed);

            releasedPawns ??= new List<Pawn>();
            releasedPawns.RemoveAll(p => p == null || p.Destroyed || p.Discarded);
            breachRouteCheckedPawns ??= new List<Pawn>();
            breachRouteCheckedPawns.RemoveAll(p => p == null || p.Destroyed || p.Discarded);
            breachEligiblePawns ??= new List<Pawn>();
            breachEligiblePawns.RemoveAll(p => p == null || p.Destroyed || p.Discarded);
            if (breachClassificationCursor < 0)
            {
                breachClassificationCursor = 0;
            }

            if (stage == CerebrexSupportEvacVehicleStage.NotRequested && podRequested)
            {
                // 旧存档只记录了“请求过”。若已有实体则推断为已落地/已离开，
                // 否则保持 NotRequested，由正常流程重新（生成）一次。
                if (vehicleThing != null)
                {
                    stage = (vehicleThing is FlyShipLeaving)
                        ? CerebrexSupportEvacVehicleStage.Departing
                        : CerebrexSupportEvacVehicleStage.Landed;
                    stateChangedTick = GenTicks.TicksGame;
                }
            }

            podRequested = false;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref mode, "mode", CerebrexSupportEvacMode.None);
            Scribe_References.Look(ref faction, "faction");
            Scribe_Collections.Look(ref pawns, "pawns", LookMode.Reference);
            Scribe_References.Look(ref vehicleThing, "vehicleThing");
            Scribe_Values.Look(ref groupID, "groupID", -1);
            Scribe_Values.Look(ref landingCell, "landingCell", IntVec3.Invalid);
            Scribe_Values.Look(ref podRequested, "podRequested", false);
            Scribe_Values.Look(ref stage, "stage", CerebrexSupportEvacVehicleStage.NotRequested);
            Scribe_Values.Look(ref stateChangedTick, "stateChangedTick", -1);
            Scribe_Values.Look(ref nextRetryTick, "nextRetryTick", -1);
            Scribe_Values.Look(ref partialLoadDepartureTick, "partialLoadDepartureTick", -1);
            Scribe_Collections.Look(ref releasedPawns, "releasedPawns", LookMode.Reference);
            Scribe_Values.Look(ref launchedPawnCount, "launchedPawnCount", 0);
            Scribe_Collections.Look(ref breachRouteCheckedPawns, "breachRouteCheckedPawns", LookMode.Reference);
            Scribe_Collections.Look(ref breachEligiblePawns, "breachEligiblePawns", LookMode.Reference);
            Scribe_Values.Look(ref nextBreachCheckTick, "nextBreachCheckTick", -1);
            Scribe_Values.Look(ref breachClassificationCursor, "breachClassificationCursor", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pawns ??= new List<Pawn>();
                PostLoadInit();
            }
        }
    }
}
