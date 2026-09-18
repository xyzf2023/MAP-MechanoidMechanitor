using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class BandwidthSupportAllocation : IExposable
    {
        public Pawn? Pawn;
        public int Amount;
        public long AddedOrder;

        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn");
            Scribe_Values.Look(ref Amount, "amount");
            Scribe_Values.Look(ref AddedOrder, "addedOrder");
        }
    }

    /// <summary>全局配额、账期与分配的唯一权威源。离图不终止支持，旧档默认未申请。</summary>
    public sealed class GameComponent_OvermindBandwidthSupport : GameComponent
    {
        private List<BandwidthSupportAllocation> allocations = new List<BandwidthSupportAllocation>();
        private bool activated;
        private bool takeoverMode;
        private int baseBandwidth;
        private int extension;
        private int requestedExtension;
        private int periodUnitCost;
        private int unrestrictedBandwidth;
        private long nextSettlement;
        private long nextOrder;
        private bool shortageWarned;
        private int observedLevel;
        private bool needsReconcile;
        private int transferDepth;

        public GameComponent_OvermindBandwidthSupport(Game game) { }
        public static GameComponent_OvermindBandwidthSupport? Current =>
            CurrentGameComponentCache<GameComponent_OvermindBandwidthSupport>.Get();
        public static bool TakenOver => GameComponent_CerebrexTakeoverState.IsActive;
        public static bool Available => ModsConfig.BiotechActive && (TakenOver
            || GameComponent_MechanoidMechanitorStoryState.IsPurgeDirectiveActive);
        private static PurgeDirectiveRatingConfigDef Config => PurgeDirectiveRatingUtility.Config;
        public static int Unit => Math.Max(1, Config.bandwidthSupportUnit);
        public static int PeriodDays => Math.Max(1, Config.bandwidthSupportPeriodDays);
        private static long PeriodTicks => (long)PeriodDays * GenDate.TicksPerDay;
        private static long Now => Find.TickManager.TicksGame;
        private static int Level => PurgeDirectiveRatingUtility.CurrentRatingLevel;
        private static int ConfigBase => Math.Max(0, Config.bandwidthSupportBaseByLevel[Level - 1]);
        private static int ConfigCost => Math.Max(0, Config.bandwidthSupportCostByLevel[Level - 1]);
        public bool Activated => activated;
        public int BaseBandwidth => baseBandwidth;
        public int Extension => extension;
        public int Requested => TakenOver ? unrestrictedBandwidth : requestedExtension;
        public int Total => !activated ? 0 : takeoverMode ? unrestrictedBandwidth : baseBandwidth + extension;
        public int Allocated => (int)allocations.Sum(a => (long)a.Amount);
        public long RemainingTicks => Math.Max(0, nextSettlement - Now);
        public long NextCost => (long)requestedExtension / Unit * ConfigCost;
        public int AllocatedTo(Pawn? pawn) => pawn == null ? 0
            : (int)allocations.Where(a => a.Pawn == pawn).Sum(a => (long)a.Amount);

        public static bool Eligible(Pawn? pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed || pawn.Discarded
                || pawn.Faction != Faction.OfPlayer || pawn.health == null) return false;
            // 机械族仅查正式身份注册表。禁止用控制节点能力或 mechanitor tracker 代替身份。
            if (pawn.RaceProps.IsMechanoid)
                return GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(pawn, out _);
            return pawn.RaceProps.Humanlike && MechanitorUtility.IsMechanitor(pawn);
        }

        public List<Pawn> Candidates()
        {
            return PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_OfPlayerFaction
                .Concat(GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors)
                .Concat(allocations.Where(a => a.Pawn != null).Select(a => a.Pawn!))
                .Where(Eligible).Distinct().OrderBy(p => p.LabelShort).ToList();
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref allocations, "allocations", LookMode.Deep);
            Scribe_Values.Look(ref activated, "activated");
            Scribe_Values.Look(ref takeoverMode, "takeoverMode");
            Scribe_Values.Look(ref baseBandwidth, "baseBandwidth");
            Scribe_Values.Look(ref extension, "extension");
            Scribe_Values.Look(ref requestedExtension, "requestedExtension");
            Scribe_Values.Look(ref periodUnitCost, "periodUnitCost");
            Scribe_Values.Look(ref unrestrictedBandwidth, "unrestrictedBandwidth");
            Scribe_Values.Look(ref nextSettlement, "nextSettlement");
            Scribe_Values.Look(ref nextOrder, "nextOrder");
            Scribe_Values.Look(ref shortageWarned, "shortageWarned");
            Scribe_Values.Look(ref observedLevel, "observedLevel");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                allocations ??= new List<BandwidthSupportAllocation>();
                allocations.RemoveAll(a => a == null || a.Pawn == null || a.Amount <= 0);
                nextOrder = Math.Max(nextOrder, allocations.Select(a => a.AddedOrder).DefaultIfEmpty(0).Max());
                needsReconcile = true; // 不在读档阶段增删健康状态或重算控制关系。
            }
        }

        public override void GameComponentTick()
        {
            if (needsReconcile || Now % 60 == 0) Reconcile();
        }

        public void Activate()
        {
            Reconcile();
            if (!Available || activated || MechanoidMechanitorPostLoadSafetyCoordinator.LoadInProgress) return;
            activated = true;
            takeoverMode = TakenOver;
            baseBandwidth = takeoverMode ? 0 : ConfigBase;
            periodUnitCost = takeoverMode ? 0 : ConfigCost;
            observedLevel = Level;
            nextSettlement = takeoverMode ? 0 : Now + PeriodTicks;
        }

        public long ChangeCost(int desired)
        {
            if (TakenOver || desired <= extension) return 0;
            if (extension == 0) return (long)desired / Unit * ConfigCost;
            long fullCost = (long)(desired - extension) / Unit * periodUnitCost;
            // 除法放在整体乘积之后：整笔补差价只向上取整一次。
            return (long)Math.Ceiling((decimal)fullCost * RemainingTicks / PeriodTicks);
        }

        public bool TryRequest(int desired)
        {
            Reconcile();
            if (!Available || !activated || desired < 0
                || MechanoidMechanitorPostLoadSafetyCoordinator.LoadInProgress) return false;
            if (TakenOver)
            {
                unrestrictedBandwidth = desired;
                TrimAllocations();
                return true;
            }
            if (desired % Unit != 0
                || (long)desired + Math.Max(baseBandwidth, Config.bandwidthSupportBaseByLevel.Max()) > int.MaxValue)
                return false;
            long cost = ChangeCost(desired);
            if (cost > int.MaxValue || (cost > 0
                && !GameComponent_MechanoidMechanitorStoryState.TrySpendPurgeDirectiveCredits((int)cost)))
                return false;
            if (extension == 0 && desired > 0)
            {
                nextSettlement = Now + PeriodTicks;
                periodUnitCost = ConfigCost;
                baseBandwidth = ConfigBase;
            }
            if (desired > extension) extension = desired;
            requestedExtension = desired;
            shortageWarned = false;
            TrimAllocations();
            return true;
        }

        public bool TryAllocate(Pawn pawn, int amount)
        {
            Reconcile();
            if (!Available || !activated || !Eligible(pawn) || amount < 0
                || MechanoidMechanitorPostLoadSafetyCoordinator.LoadInProgress) return false;
            int old = AllocatedTo(pawn);
            if ((long)Allocated - old + amount > Total) return false;
            SetAllocation(pawn, amount);
            SyncPawn(pawn);
            return true;
        }

        private void SetAllocation(Pawn pawn, int amount)
        {
            int old = AllocatedTo(pawn);
            var entries = allocations.Where(a => a.Pawn == pawn).OrderByDescending(a => a.AddedOrder).ToList();
            if (amount > old)
            {
                if (entries.Count == 0)
                    allocations.Add(new BandwidthSupportAllocation { Pawn = pawn, Amount = amount, AddedOrder = ++nextOrder });
                else entries[0].Amount += amount - old; // 调整容量不改变健康状态首次添加顺序。
            }
            else
            {
                int remaining = old - amount;
                foreach (var entry in entries)
                {
                    int remove = Math.Min(remaining, entry.Amount);
                    entry.Amount -= remove;
                    remaining -= remove;
                }
                allocations.RemoveAll(a => a.Amount == 0);
            }
        }

        public long OrderCapacity(int desired) => TakenOver ? desired
            : (long)(extension == 0 && desired > 0 ? ConfigBase : baseBandwidth) + Math.Max(extension, desired);

        public bool CanExecuteOrder(int desired, IReadOnlyDictionary<Pawn, int> changes)
        {
            if (!Available || !activated || desired < 0 || transferDepth > 0
                || MechanoidMechanitorPostLoadSafetyCoordinator.LoadInProgress) return false;
            if (!TakenOver && (desired % Unit != 0
                || (long)desired + Math.Max(baseBandwidth, Config.bandwidthSupportBaseByLevel.Max()) > int.MaxValue))
                return false;
            if (changes.Any(pair => pair.Value < 0 || !Eligible(pair.Key))) return false;
            long allocated = Allocated + changes.Sum(pair => (long)pair.Value - AllocatedTo(pair.Key));
            long cost = ChangeCost(desired);
            return allocated <= OrderCapacity(desired) && cost <= int.MaxValue
                && (TakenOver || cost <= GameComponent_MechanoidMechanitorStoryState.GetPurgeDirectiveRewardPoints());
        }

        public bool TryExecuteOrder(int desired, IReadOnlyDictionary<Pawn, int> changes)
        {
            Reconcile();
            if (!CanExecuteOrder(desired, changes)) return false;
            var snapshot = allocations.Select(a => new BandwidthSupportAllocation
                { Pawn = a.Pawn, Amount = a.Amount, AddedOrder = a.AddedOrder }).ToList();
            int oldBase = baseBandwidth, oldExtension = extension, oldRequested = requestedExtension;
            int oldPrice = periodUnitCost, oldUnrestricted = unrestrictedBandwidth;
            long oldSettlement = nextSettlement, oldOrder = nextOrder;
            bool oldWarning = shortageWarned;
            int cost = (int)ChangeCost(desired);
            if (cost > 0 && !GameComponent_MechanoidMechanitorStoryState.TrySpendPurgeDirectiveCredits(cost)) return false;
            // 全部数据一次提交，再通知健康系统，避免调拨中途回收或断连。
            try
            {
                if (TakenOver) unrestrictedBandwidth = desired;
                else
                {
                    if (extension == 0 && desired > 0)
                    {
                        nextSettlement = Now + PeriodTicks;
                        periodUnitCost = ConfigCost;
                        baseBandwidth = ConfigBase;
                    }
                    extension = Math.Max(extension, desired);
                    requestedExtension = desired;
                    shortageWarned = false;
                }
                foreach (var pair in changes) SetAllocation(pair.Key, pair.Value);
                foreach (Pawn pawn in changes.Keys) SyncPawn(pawn);
                return true;
            }
            catch (Exception ex)
            {
                allocations = snapshot;
                baseBandwidth = oldBase;
                extension = oldExtension;
                requestedExtension = oldRequested;
                periodUnitCost = oldPrice;
                unrestrictedBandwidth = oldUnrestricted;
                nextSettlement = oldSettlement;
                nextOrder = oldOrder;
                shortageWarned = oldWarning;
                if (cost > 0) GameComponent_MechanoidMechanitorStoryState.RefundPurgeDirectiveCredits(cost);
                foreach (Pawn pawn in changes.Keys)
                {
                    try { SyncPawn(pawn); }
                    catch (Exception rollbackError) { Log.Error("[MAP] 带宽调拨健康状态恢复失败：" + rollbackError); }
                }
                Log.Error("[MAP] 带宽调拨失败，已恢复配额：" + ex);
                return false;
            }
        }

        public void RemovePawn(Pawn pawn)
        {
            if (allocations.RemoveAll(a => a.Pawn == pawn) > 0) SyncPawn(pawn);
        }

        public void Reconcile()
        {
            if (Scribe.mode != LoadSaveMode.Inactive || transferDepth > 0
                || MechanoidMechanitorPostLoadSafetyCoordinator.LoadInProgress) return;
            if (!Available)
            {
                var pawns = allocations.Select(a => a.Pawn).Distinct().ToList();
                allocations.Clear();
                activated = takeoverMode = false;
                baseBandwidth = extension = requestedExtension = unrestrictedBandwidth = 0;
                nextSettlement = 0;
                foreach (var pawn in pawns) if (pawn != null) SyncPawn(pawn);
                needsReconcile = false;
                return;
            }
            if (!activated) { needsReconcile = false; return; }
            foreach (var pawn in allocations.Select(a => a.Pawn).Distinct().ToList())
                if (pawn != null && !Eligible(pawn)) RemovePawn(pawn);

            if (TakenOver && !takeoverMode)
            {
                unrestrictedBandwidth = Total;
                takeoverMode = true;
                baseBandwidth = extension = requestedExtension = periodUnitCost = 0;
                nextSettlement = 0;
                shortageWarned = false;
            }
            else if (!TakenOver && takeoverMode)
            {
                // 开发模式撤销接管时也不能遗留无额度限制的配额。
                takeoverMode = false;
                unrestrictedBandwidth = 0;
                baseBandwidth = ConfigBase;
                periodUnitCost = ConfigCost;
                nextSettlement = Now + PeriodTicks;
                TrimAllocations();
            }
            if (!takeoverMode)
            {
                if (ConfigBase > baseBandwidth)
                {
                    int previous = baseBandwidth;
                    baseBandwidth = ConfigBase;
                    Tell("BaseRaised", previous, baseBandwidth);
                }
                if (observedLevel != Level && ConfigBase < baseBandwidth)
                    Tell("BaseWillFall", ConfigBase);
                observedLevel = Level;
                if (nextSettlement <= 0) nextSettlement = Now + PeriodTicks;
                while (Now >= nextSettlement)
                {
                    baseBandwidth = ConfigBase;
                    periodUnitCost = ConfigCost;
                    long cost = NextCost;
                    if (cost > int.MaxValue || (cost > 0
                        && !GameComponent_MechanoidMechanitorStoryState.TrySpendPurgeDirectiveCredits((int)cost)))
                    {
                        int removed = extension;
                        extension = requestedExtension = 0;
                        Tell("Recalled", removed);
                    }
                    else extension = requestedExtension;
                    nextSettlement += PeriodTicks;
                    shortageWarned = false;
                    TrimAllocations();
                }
                if (!shortageWarned && requestedExtension > 0 && RemainingTicks <= GenDate.TicksPerDay
                    && NextCost > GameComponent_MechanoidMechanitorStoryState.GetPurgeDirectiveRewardPoints())
                {
                    Tell("Shortage", NextCost);
                    shortageWarned = true;
                }
            }
            if (needsReconcile) TrimAllocations();
            foreach (var pawn in allocations.Select(a => a.Pawn).Distinct().ToList())
                if (pawn != null) SyncPawn(pawn);
            needsReconcile = false;
        }

        private void TrimAllocations()
        {
            // 未分配容量自然先扣除；只有分配总量超限才改动健康状态。
            int excess = Math.Max(0, Allocated - Total);
            var ordered = allocations.OrderByDescending(a => a.AddedOrder).ToList();
            for (int pass = 0; pass < 2 && excess > 0; pass++)
            {
                foreach (var entry in ordered)
                {
                    Pawn? pawn = entry.Pawn;
                    if (pawn == null || entry.Amount == 0) continue;
                    int removable = entry.Amount;
                    if (pass == 0)
                    {
                        // 包含培育占用；只回收真正空闲的支援，不触碰自身/芯片提供的带宽。
                        int spare = pawn.mechanitor == null ? 0
                            : Math.Max(0, pawn.mechanitor.TotalBandwidth - pawn.mechanitor.UsedBandwidth);
                        removable = Math.Min(removable, spare);
                    }
                    int remove = Math.Min(excess, removable);
                    if (remove <= 0) continue;
                    entry.Amount -= remove;
                    excess -= remove;
                    SyncPawn(pawn);
                    if (excess == 0) break;
                }
            }
            allocations.RemoveAll(a => a.Amount <= 0);
        }

        private void SyncPawn(Pawn pawn)
        {
            if (pawn.health?.hediffSet == null) return;
            HediffDef def = DefDatabase<HediffDef>.GetNamed("MAP_OvermindBandwidthSupport");
            int amount = AllocatedTo(pawn);
            var effects = pawn.health.hediffSet.hediffs.Where(h => h.def == def).ToList();
            bool changed = false;
            for (int i = effects.Count - 1; i >= 0; i--)
                if (amount == 0 || i > 0)
                {
                    pawn.health.RemoveHediff(effects[i]);
                    changed = true;
                }
            if (amount > 0)
            {
                Hediff effect;
                if (effects.Count == 0)
                {
                    effect = pawn.health.AddHediff(def);
                    changed = true;
                }
                else effect = effects[0];
                if (effect is Hediff_OvermindBandwidthSupport support && support.Refresh()) changed = true;
            }
            if (changed && !pawn.Dead) pawn.mechanitor?.Notify_BandwidthChanged();
        }

        private static void Tell(string key, params NamedArgument[] args)
        {
            Messages.Message(("MAP_BandwidthSupport." + key).Translate(args), MessageTypeDefOf.NeutralEvent, false);
        }

        // 意识转移持有此事务至宿主替换提交。先提供目标带宽，失败时在监管关系回滚前恢复。
        public TransferScope BeginTransfer(Pawn source, Pawn target) => new TransferScope(this, source, target);

        public sealed class TransferScope : IDisposable
        {
            private readonly GameComponent_OvermindBandwidthSupport owner;
            private readonly Pawn source;
            private readonly Pawn target;
            private readonly List<BandwidthSupportAllocation> moved;
            private readonly List<BandwidthSupportAllocation> provisional;
            private bool committed;
            private bool disposed;

            internal TransferScope(GameComponent_OvermindBandwidthSupport owner, Pawn source, Pawn target)
            {
                this.owner = owner;
                this.source = source;
                this.target = target;
                moved = owner.allocations.Where(a => a.Pawn == source).ToList();
                provisional = moved.Select(a => new BandwidthSupportAllocation
                {
                    Pawn = target, Amount = a.Amount, AddedOrder = a.AddedOrder
                }).ToList();
                owner.transferDepth++;
                try
                {
                    // 提交前保留源带宽，避免提前触发原版断连；临时目标配额受事务 guard 保护。
                    owner.allocations.AddRange(provisional);
                    owner.SyncPawn(target);
                }
                catch
                {
                    foreach (var entry in provisional) owner.allocations.Remove(entry);
                    owner.transferDepth--;
                    owner.needsReconcile = true;
                    throw;
                }
            }

            public void Commit()
            {
                committed = true;
                foreach (var entry in moved) owner.allocations.Remove(entry);
                owner.SyncPawn(source);
            }
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                try
                {
                    if (!committed)
                    {
                        foreach (var entry in provisional) owner.allocations.Remove(entry);
                        owner.SyncPawn(target);
                    }
                }
                finally { owner.transferDepth--; }
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class BandwidthSupport_PawnDeathPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance)
        {
            if (__instance.Dead && !MechanoidMechanitorPostLoadSafetyCoordinator.LoadInProgress)
                GameComponent_OvermindBandwidthSupport.Current?.RemovePawn(__instance);
        }
    }
}
