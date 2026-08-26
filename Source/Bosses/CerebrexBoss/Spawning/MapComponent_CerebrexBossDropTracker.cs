using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 空投舱落地后的处理记录：仅记录正在空投舱中的 Pawn，并在其实际生成到地图后
    /// 确保派系、按待落地记录决定是否补充机动作战、加入对应主脑的进攻 Lord 并强制终止等待 Job。
    /// 机动作战是否补充由记录中的 applyMobileCombat 决定（来自本场战斗快照），
    /// 关闭开关时不再补加，但不会移除 Pawn 通过其他来源已经获得的机动作战。
    /// 该组件不保存任何主脑技能冷却、带宽干扰、EMP 或自动战斗状态。
    /// </summary>
    public sealed class CerebrexPendingDropRecord : IExposable
    {
        public Pawn? pawn;

        public int coreThingId;

        public Faction? faction;

        public int registeredTick;

        // 落地追踪器按待落地记录决定是否补充机动作战。
        // 默认 true：保证旧存档中已存在的待落地空投继续旧版行为（由主脑 BOSS 添加）。
        // 实际值由主脑控制器在本场战斗快照中捕获后写入；关闭开关的记录不再补加。
        public bool applyMobileCombat = true;

        public CerebrexPendingDropRecord()
        {
        }

        public CerebrexPendingDropRecord(
            Pawn pawn,
            int coreThingId,
            Faction faction,
            int registeredTick,
            bool applyMobileCombat = true)
        {
            this.pawn = pawn;
            this.coreThingId = coreThingId;
            this.faction = faction;
            this.registeredTick = registeredTick;
            this.applyMobileCombat = applyMobileCombat;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref coreThingId, "coreThingId", 0);
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref registeredTick, "registeredTick", 0);
            Scribe_Values.Look(ref applyMobileCombat, "applyMobileCombat", true);
        }
    }

    public sealed class MapComponent_CerebrexBossDropTracker : MapComponent
    {
        private List<CerebrexPendingDropRecord> pendingDrops = new List<CerebrexPendingDropRecord>();

        public MapComponent_CerebrexBossDropTracker(Map map)
            : base(map)
        {
        }

        public static MapComponent_CerebrexBossDropTracker For(Map map)
        {
            MapComponent_CerebrexBossDropTracker? existing =
                map.GetComponent<MapComponent_CerebrexBossDropTracker>();
            if (existing != null)
            {
                return existing;
            }

            MapComponent_CerebrexBossDropTracker created =
                new MapComponent_CerebrexBossDropTracker(map);
            map.components.Add(created);
            return created;
        }

        public void RegisterDrop(
            Pawn pawn,
            int coreThingId,
            Faction faction,
            bool applyMobileCombat)
        {
            if (pawn == null)
            {
                return;
            }

            pendingDrops.Add(
                new CerebrexPendingDropRecord(
                    pawn,
                    coreThingId,
                    faction,
                    Find.TickManager.TicksGame,
                    applyMobileCombat));
        }

        public override void MapComponentTick()
        {
            if (!ModsConfig.OdysseyActive)
            {
                return;
            }

            if (pendingDrops.Count == 0 || !map.IsHashIntervalTick(10))
            {
                return;
            }

            ProcessDrops();
        }

        private void ProcessDrops()
        {
            for (int i = pendingDrops.Count - 1; i >= 0; i--)
            {
                CerebrexPendingDropRecord? entry = pendingDrops[i];
                if (entry == null)
                {
                    pendingDrops.RemoveAt(i);
                    continue;
                }

                Pawn? pawn = entry.pawn;
                if (pawn == null || pawn.Destroyed || pawn.Dead || pawn.Discarded)
                {
                    pendingDrops.RemoveAt(i);
                    continue;
                }

                if (IsInTransit(pawn))
                {
                    continue;
                }

                if (pawn.Spawned && pawn.Map == map)
                {
                    CompleteLanding(entry);
                    pendingDrops.RemoveAt(i);
                }
            }
        }

        private static bool IsInTransit(Pawn pawn)
        {
            IThingHolder? holder = pawn.ParentHolder;
            while (holder != null)
            {
                if (holder is ActiveTransporter || holder is ActiveTransporterInfo || holder is Skyfaller)
                {
                    return true;
                }

                holder = holder.ParentHolder;
            }

            return false;
        }

        private void CompleteLanding(CerebrexPendingDropRecord entry)
        {
            Pawn pawn = entry.pawn!;
            Faction? faction = entry.faction ?? pawn.Faction ?? Faction.OfMechanoids;
            if (faction != null && pawn.Faction != faction)
            {
                pawn.SetFaction(faction);
            }

            // 落地阶段是否补充机动作战由待落地记录决定：关闭主脑机动作战开关的记录不再补加，
            // 但不会移除该 Pawn 通过其他来源已经获得的机动作战。
            if (entry.applyMobileCombat)
            {
                MechanoidMechanitorWorkModeUtility.EnsureMobileCombatHediff(pawn);
            }

            CompCerebrexBossController? controller = FindController(entry.coreThingId);
            Lord? lord = controller?.GetAssaultLord();
            if (lord != null)
            {
                if (!lord.ownedPawns.Contains(pawn))
                {
                    lord.AddPawn(pawn);
                }

                pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced);
                lord.CurLordToil?.UpdateAllDuties();
            }
        }

        private CompCerebrexBossController? FindController(int coreThingId)
        {
            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (thing is ThingWithComps thingWithComps)
                {
                    CompCerebrexBossController? controller =
                        thingWithComps.GetComp<CompCerebrexBossController>();
                    if (controller != null && controller.parent.thingIDNumber == coreThingId)
                    {
                        return controller;
                    }
                }
            }

            return null;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref pendingDrops, "cerebrexPendingDrops", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pendingDrops ??= new List<CerebrexPendingDropRecord>();
                pendingDrops.RemoveAll(p => p == null || p.pawn == null);
            }
        }
    }
}
