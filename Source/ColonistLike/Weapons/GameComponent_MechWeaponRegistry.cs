using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>专武部件与武器使用模式独立存档；适用于能力层授权的所有自由持械机械体。</summary>
    public sealed class MechWeaponRecord : IExposable
    {
        public Pawn? Pawn;
        public ThingDef? WeaponDef;
        public bool Missing;
        public bool ExternalMode;
        // 合体期间仍引用借出的真实武器，防止修理/切换重复生成。
        public ThingWithComps? ActiveWeapon;
        internal int ControlledChanges;

        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn");
            Scribe_Defs.Look(ref WeaponDef, "weaponDef");
            Scribe_Values.Look(ref Missing, "missing", false);
            Scribe_Values.Look(ref ExternalMode, "externalMode", false);
            Scribe_References.Look(ref ActiveWeapon, "activeWeapon");
        }
    }

    public sealed class GameComponent_MechWeaponRegistry : GameComponent
    {
        private List<MechWeaponRecord> records = new List<MechWeaponRecord>();
        private readonly Dictionary<Pawn, MechWeaponRecord> byPawn = new Dictionary<Pawn, MechWeaponRecord>();
        private static GameComponent_MechWeaponRegistry? Registry =>
            CurrentGameComponentCache<GameComponent_MechWeaponRegistry>.Get();

        public GameComponent_MechWeaponRegistry(Game game) { }

        internal static MechWeaponRecord? Get(Pawn? pawn)
        {
            if (pawn == null || Registry == null) return null;
            return Registry.byPawn.TryGetValue(pawn, out MechWeaponRecord record) ? record : null;
        }

        internal static MechWeaponRecord? OwnerOf(Thing weapon)
        {
            if (Registry == null) return null;
            foreach (MechWeaponRecord record in Registry.records)
                if (ReferenceEquals(record.ActiveWeapon, weapon)) return record;
            return null;
        }

        // 只在生命周期/装备变更入口初始化；查询和绘制不写入存档状态。
        internal static void Ensure(Pawn pawn)
        {
            if (Scribe.mode != LoadSaveMode.Inactive || Registry == null || Get(pawn) != null
                || !MechWeaponUtility.IsManaged(pawn) || pawn.equipment == null) return;
            ThingDef? def = MechWeaponUtility.ResolveBuiltInWeapon(pawn);
            if (def == null) return;
            ThingWithComps? primary = pawn.equipment.Primary;
            bool equipped = primary != null && MechWeaponUtility.IsBuiltInFor(pawn, primary.def);
            var record = new MechWeaponRecord
            {
                Pawn = pawn,
                WeaponDef = equipped ? primary!.def : def,
                ActiveWeapon = equipped ? primary : null,
                // 旧档无法还原移除原因：已有外部武器视为正常换装，空手视为部件缺失。
                ExternalMode = primary != null && !equipped,
                Missing = primary == null
            };
            Registry.records.Add(record);
            Registry.byPawn.Add(pawn, record);
        }

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
                records.RemoveAll(r => r?.Pawn == null || r.Pawn.Discarded);
            Scribe_Collections.Look(ref records, "mechWeaponRecords", LookMode.Deep);
            if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
            records ??= new List<MechWeaponRecord>();
            byPawn.Clear();
            records.RemoveAll(r => r?.Pawn == null || r.Pawn.Discarded || r.WeaponDef == null);
            foreach (MechWeaponRecord record in records)
            {
                byPawn[record.Pawn!] = record;
                record.ControlledChanges = 0;
                // 不把正常收起的专武当成损坏；只修正本应存在、读档后却已失去的实体。
                if (!record.ExternalMode && record.ActiveWeapon == null)
                    record.Missing = true;
            }
        }

        public override void LoadedGame()
        {
            // 一次性旧档迁移，覆盖远行队/世界 Pawn；后续只走生命周期事件，不逐 tick 扫描。
            foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead)
                Ensure(pawn);
            foreach (MechFusionSession session in GameComponent_MechFusionSessionRegistry.GetSessionsForReading())
            {
                Pawn? source = session.SourcePawn;
                ThingWithComps? weapon = session.SourceWeapon;
                if (source == null || weapon == null || weapon.Destroyed || !MechWeaponUtility.IsBuiltInFor(source, weapon.def)
                    || (session.WearerPawn?.equipment?.Primary != weapon
                        && session.WearerPawn?.inventory?.innerContainer.Contains(weapon) != true)) continue;
                Ensure(source);
                MechWeaponRecord? record = Get(source);
                if (record == null || record.ActiveWeapon != null || record.ExternalMode) continue;
                record.WeaponDef = weapon.def;
                record.ActiveWeapon = weapon;
                record.Missing = false;
            }
        }
    }
}
