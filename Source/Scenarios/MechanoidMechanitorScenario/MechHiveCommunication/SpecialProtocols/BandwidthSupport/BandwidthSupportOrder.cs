using System.Collections.Generic;
using System.Linq;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    // 通讯窗口草稿，不写入存档；失焦只更新这里，确认调拨才修改游戏状态。
    public sealed class BandwidthSupportOrder
    {
        private readonly GameComponent_OvermindBandwidthSupport state;
        public int Requested { get; private set; }
        private readonly Dictionary<Pawn, int> allocations = new Dictionary<Pawn, int>();
        public IReadOnlyDictionary<Pawn, int> Allocations => allocations;
        public int MaxFor(Pawn pawn) => (int)System.Math.Max(0L,
            System.Math.Min(int.MaxValue, Total - Allocated + AmountFor(pawn)));
        public void SetAllocation(Pawn pawn, int amount) =>
            allocations[pawn] = System.Math.Max(0, System.Math.Min(amount, MaxFor(pawn)));
        public bool TrySetRequested(int amount)
        {
            if (amount < 0 || state.OrderCapacity(amount) < Allocated) return false;
            Requested = amount;
            return true;
        }
        public BandwidthSupportOrder(GameComponent_OvermindBandwidthSupport state)
        {
            this.state = state;
            Reset();
        }
        public int AmountFor(Pawn pawn) => Allocations.TryGetValue(pawn, out int amount) ? amount : state.AllocatedTo(pawn);
        public bool HasChanges => Requested != state.Requested || Allocations.Any(pair => pair.Value != state.AllocatedTo(pair.Key));
        public long Cost => state.ChangeCost(Requested);
        public long Total => state.OrderCapacity(Requested);
        public long Allocated => state.Allocated + Allocations.Sum(pair => (long)pair.Value - state.AllocatedTo(pair.Key));
        public bool CanExecute => HasChanges && state.CanExecuteOrder(Requested, Allocations);
        public bool Execute()
        {
            if (!HasChanges || !state.TryExecuteOrder(Requested, Allocations)) return false;
            Reset();
            return true;
        }
        public void Reset()
        {
            Requested = state.Requested;
            allocations.Clear();
        }
    }
}
