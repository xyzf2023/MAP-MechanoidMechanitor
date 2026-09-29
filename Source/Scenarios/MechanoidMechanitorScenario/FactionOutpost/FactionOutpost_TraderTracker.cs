#nullable disable
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 机械族前哨的交易库存追踪器，复制原版 Settlement_TraderTracker 的库存/存读档逻辑，
    /// 但 owner 类型为 MAPFactionOutpost。库存规模与同派系 Settlement 使用的 TraderStock 一致，本轮不缩减。
    /// </summary>
    public class FactionOutpost_TraderTracker : IThingHolderTickable, IThingHolder, IExposable
    {
        public MAPFactionOutpost owner;

        private ThingOwner<Thing> stock;

        private int lastStockGenerationTicks = -1;

        private bool everGeneratedStock;

        private const float DefaultTradePriceImprovement = 0.02f;

        private List<Pawn> tmpSavedPawns = new List<Pawn>();

        protected virtual int RegenerateStockEveryDays => 30;

        public IThingHolder ParentHolder => owner;

        public bool ShouldTickContents => false;

        public List<Thing> StockListForReading
        {
            get
            {
                if (stock == null)
                {
                    RegenerateStock();
                }

                return stock.InnerListForReading;
            }
        }

        public TraderKindDef TraderKind
        {
            get
            {
                if (owner.Faction == null)
                {
                    return null;
                }

                List<TraderKindDef> baseTraderKinds = owner.Faction.def.baseTraderKinds;
                if (baseTraderKinds.NullOrEmpty())
                {
                    return null;
                }

                int index = Mathf.Abs(owner.HashOffset()) % baseTraderKinds.Count;
                return baseTraderKinds[index];
            }
        }

        public int RandomPriceFactorSeed => Gen.HashCombineInt(owner.ID, 1933327354);

        public bool EverVisited => everGeneratedStock;

        public bool RestockedSinceLastVisit
        {
            get
            {
                if (everGeneratedStock)
                {
                    return stock == null;
                }

                return false;
            }
        }

        public int NextRestockTick
        {
            get
            {
                if (stock == null || !everGeneratedStock)
                {
                    return -1;
                }

                return ((lastStockGenerationTicks != -1) ? lastStockGenerationTicks : 0)
                    + RegenerateStockEveryDays * 60000;
            }
        }

        public virtual string TraderName
        {
            get
            {
                if (owner.Faction == null)
                {
                    return owner.LabelCap;
                }

                return "SettlementTrader".Translate(owner.LabelCap, owner.Faction.Name);
            }
        }

        public bool CanTradeNow => TraderKind != null;

        public virtual float TradePriceImprovementOffsetForPlayer => DefaultTradePriceImprovement;

        public FactionOutpost_TraderTracker(MAPFactionOutpost owner)
        {
            this.owner = owner;
        }

        public virtual void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                tmpSavedPawns.Clear();
                if (stock != null)
                {
                    for (int num = stock.Count - 1; num >= 0; num--)
                    {
                        if (stock[num] is Pawn item)
                        {
                            stock.Remove(item);
                            tmpSavedPawns.Add(item);
                        }
                    }
                }
            }

            Scribe_Collections.Look(ref tmpSavedPawns, "tmpSavedPawns", LookMode.Reference);
            Scribe_Deep.Look(ref stock, "stock");
            Scribe_Values.Look(ref lastStockGenerationTicks, "lastStockGenerationTicks", 0);
            Scribe_Values.Look(ref everGeneratedStock, "wasStockGeneratedYet", defaultValue: false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit || Scribe.mode == LoadSaveMode.Saving)
            {
                tmpSavedPawns ??= new List<Pawn>();
                tmpSavedPawns.RemoveAll(pawn => pawn == null || pawn.Discarded);
                if (tmpSavedPawns.Count > 0 && stock == null)
                {
                    // 仅恢复已有库存引用，不生成一批新货物覆盖存档。
                    stock = new ThingOwner<Thing>(this) { dontTickContents = true };
                }

                for (int i = 0; i < tmpSavedPawns.Count; i++)
                {
                    stock.TryAdd(tmpSavedPawns[i], canMergeWithExistingStacks: false);
                }

                tmpSavedPawns.Clear();
            }
        }

        public virtual IEnumerable<Thing> ColonyThingsWillingToBuy(Pawn playerNegotiator)
        {
            Caravan caravan = playerNegotiator.GetCaravan();
            foreach (Thing item in CaravanInventoryUtility.AllInventoryItems(caravan))
            {
                yield return item;
            }

            List<Pawn> pawns = caravan.PawnsListForReading;
            for (int i = 0; i < pawns.Count; i++)
            {
                if (!caravan.IsOwner(pawns[i]))
                {
                    yield return pawns[i];
                }
            }
        }

        public virtual void GiveSoldThingToTrader(Thing toGive, int countToGive, Pawn playerNegotiator)
        {
            if (stock == null)
            {
                RegenerateStock();
            }

            Caravan caravan = playerNegotiator.GetCaravan();
            Thing thing = toGive.SplitOff(countToGive);
            thing.PreTraded(TradeAction.PlayerSells, playerNegotiator, owner);
            if (toGive is Pawn pawn)
            {
                CaravanInventoryUtility.MoveAllInventoryToSomeoneElse(pawn, caravan.PawnsListForReading);
                if (!pawn.RaceProps.Humanlike && !stock.TryAdd(pawn, canMergeWithExistingStacks: false))
                {
                    pawn.Destroy();
                }
            }
            else if (!stock.TryAdd(thing, canMergeWithExistingStacks: false))
            {
                thing.Destroy();
            }
        }

        public virtual void GiveSoldThingToPlayer(Thing toGive, int countToGive, Pawn playerNegotiator)
        {
            Caravan caravan = playerNegotiator.GetCaravan();
            Thing thing = toGive.SplitOff(countToGive);
            thing.PreTraded(TradeAction.PlayerBuys, playerNegotiator, owner);
            if (thing is Pawn p)
            {
                caravan.AddPawn(p, addCarriedPawnToWorldPawnsIfAny: true);
                return;
            }

            Pawn pawn = CaravanInventoryUtility.FindPawnToMoveInventoryTo(
                thing,
                caravan.PawnsListForReading,
                null);
            if (pawn == null)
            {
                Log.Error("[MAP-机械族机械师] 未找到可接收售出物品的角色。");
                thing.Destroy();
            }
            else if (!pawn.inventory.innerContainer.TryAdd(thing))
            {
                Log.Error("[MAP-机械族机械师] 无法将售出物品加入物品栏。");
                thing.Destroy();
            }
        }

        public virtual void TraderTrackerTick()
        {
            if (stock == null)
            {
                return;
            }

            if (Find.TickManager.TicksGame - lastStockGenerationTicks > RegenerateStockEveryDays * 60000)
            {
                TryDestroyStock();
                return;
            }

            for (int num = stock.Count - 1; num >= 0; num--)
            {
                if (stock[num] is Pawn { Destroyed: not false } pawn)
                {
                    stock.Remove(pawn);
                }
            }

            for (int num2 = stock.Count - 1; num2 >= 0; num2--)
            {
                if (stock[num2] is Pawn pawn2 && !pawn2.IsWorldPawn())
                {
                    Log.Error("[MAP-机械族机械师] 派系前哨库存中存在未登记为世界角色的角色，正在移除。");
                    stock.Remove(pawn2);
                }
            }
        }

        public void TryDestroyStock()
        {
            if (stock == null)
            {
                return;
            }

            for (int num = stock.Count - 1; num >= 0; num--)
            {
                Thing thing = stock[num];
                stock.Remove(thing);
                if (!(thing is Pawn) && !thing.Destroyed)
                {
                    thing.Destroy();
                }
            }

            stock = null;
        }

        public bool ContainsPawn(Pawn p)
        {
            if (stock != null)
            {
                return stock.Contains(p);
            }

            return false;
        }

        protected virtual void RegenerateStock()
        {
            TryDestroyStock();
            stock = new ThingOwner<Thing>(this)
            {
                dontTickContents = true
            };
            everGeneratedStock = true;

            if (owner.Faction != null)
            {
                ThingSetMakerParams parms = new ThingSetMakerParams
                {
                    traderDef = TraderKind,
                    tile = owner.Tile,
                    makingFaction = owner.Faction
                };
                stock.TryAddRangeOrTransfer(ThingSetMakerDefOf.TraderStock.root.Generate(parms));
            }

            for (int i = 0; i < stock.Count; i++)
            {
                if (stock[i] is Pawn pawn)
                {
                    Find.WorldPawns.PassToWorld(pawn);
                }
            }

            lastStockGenerationTicks = Find.TickManager.TicksGame;
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            return stock;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }
    }
}
