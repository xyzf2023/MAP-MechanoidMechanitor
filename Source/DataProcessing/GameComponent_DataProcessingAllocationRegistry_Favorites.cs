using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed partial class GameComponent_DataProcessingAllocationRegistry
    {
        // 复用目标/监管者/顺序记录格式，收藏列表与预算调度使用的 pinRecords 完全独立。
        private List<DataProcessingAllocationPinRecord> favoriteRecords = new List<DataProcessingAllocationPinRecord>();
        private bool favoritesInitialized;

        private void ExposeFavorites()
        {
            if (Scribe.mode == LoadSaveMode.Saving) EnsureFavoritesInitialized();
            Scribe_Collections.Look(ref favoriteRecords, "uiFavoriteRecords", LookMode.Deep);
            Scribe_Values.Look(ref favoritesInitialized, "uiFavoritesInitialized", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureFavoritesInitialized();
                // 暂时失去监管关系、死亡/休眠不抹掉偏好；只清理永久失效的引用。
                favoriteRecords.RemoveAll(r => r == null || r.overseer == null || r.target == null
                    || r.overseer.Discarded || r.target.Discarded || ReferenceEquals(r.overseer, r.target));
            }
        }

        private void EnsureFavoritesInitialized()
        {
            favoriteRecords ??= new List<DataProcessingAllocationPinRecord>();
            if (favoritesInitialized) return;
            favoriteRecords.RemoveAll(r => r == null);
            favoritesInitialized = true;
            if (pinRecords == null) return;
            foreach (var pin in pinRecords)
            {
                if (pin?.overseer == null || pin.target == null || ReferenceEquals(pin.overseer, pin.target)) continue;
                if (favoriteRecords.Exists(r => ReferenceEquals(r.overseer, pin.overseer) && ReferenceEquals(r.target, pin.target))) continue;
                favoriteRecords.Add(new DataProcessingAllocationPinRecord(pin.overseer, pin.target, pin.pinOrder));
            }
        }

        public int GetFavoriteOrder(Pawn overseer, Pawn target)
        {
            EnsureFavoritesInitialized();
            var record = favoriteRecords.Find(r => ReferenceEquals(r.overseer, overseer) && ReferenceEquals(r.target, target));
            return record?.pinOrder ?? int.MaxValue;
        }

        public bool IsFavorite(Pawn overseer, Pawn target) => GetFavoriteOrder(overseer, target) != int.MaxValue;

        public bool ToggleFavorite(Pawn overseer, Pawn target)
        {
            if (overseer == null || target == null || target.Dead || target.Destroyed || target.Discarded
                || ReferenceEquals(overseer, target) || !IsValidAllocationPairForList(overseer, target)) return false;
            EnsureFavoritesInitialized();
            int index = favoriteRecords.FindIndex(r => ReferenceEquals(r.overseer, overseer) && ReferenceEquals(r.target, target));
            if (index >= 0) favoriteRecords.RemoveAt(index);
            else
            {
                // 按原顺序重编号，避免长期使用后序号溢出；新收藏放在最后。
                var owned = favoriteRecords.FindAll(r => ReferenceEquals(r.overseer, overseer));
                owned.Sort((a, b) => a.pinOrder.CompareTo(b.pinOrder));
                for (int i = 0; i < owned.Count; i++) owned[i].pinOrder = i;
                favoriteRecords.Add(new DataProcessingAllocationPinRecord(overseer, target, owned.Count));
            }
            return true;
        }
    }
}
