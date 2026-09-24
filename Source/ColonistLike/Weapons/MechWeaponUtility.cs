using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>第三方未设置 destroyOnDrop 的专武可显式使用此扩展；普通生成标签不代表专武。</summary>
    public sealed class MechBuiltInWeaponExtension : DefModExtension { }

    internal static class MechWeaponUtility
    {
        private static readonly HashSet<ThingDef> CompatibilityWeapons = new HashSet<ThingDef>();
        internal static void RegisterBuiltInWeapon(ThingDef def) => CompatibilityWeapons.Add(def);

        internal static bool IsManaged(Pawn? pawn) => pawn != null && !pawn.Discarded
            && pawn.kindDef != null && pawn.RaceProps?.IsMechanoid == true
            && MechanoidMechanitorCapabilityUtility.HasCapability(pawn, MechanoidMechanitorCapability.HumanWeapons);

        internal static bool IsBuiltIn(ThingDef def) => def.IsWeapon
            && def.equipmentType == EquipmentType.Primary
            && (def.destroyOnDrop || def.HasModExtension<MechBuiltInWeaponExtension>() || CompatibilityWeapons.Contains(def));

        internal static bool IsBuiltInFor(Pawn pawn, ThingDef def) => IsBuiltIn(def)
            && (GameComponent_MechWeaponRegistry.Get(pawn)?.WeaponDef == def
                || (!pawn.kindDef.weaponTags.NullOrEmpty() && !def.weaponTags.NullOrEmpty()
                    && def.weaponTags.Any(pawn.kindDef.weaponTags.Contains)));

        internal static ThingDef? ResolveBuiltInWeapon(Pawn pawn)
        {
            ThingDef? current = pawn.equipment?.Primary?.def;
            if (current != null && IsBuiltInFor(pawn, current)) return current;
            if (pawn.kindDef.weaponTags.NullOrEmpty()) return null;
            // 确定性迁移：优先实际武器；无实体时按 defName 选定一次并存档，不在切换时随机抽取。
            return DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => IsBuiltInFor(pawn, d)).OrderBy(d => d.defName, StringComparer.Ordinal).FirstOrDefault();
        }

        internal static IDisposable ControlledChange(Pawn pawn)
        {
            GameComponent_MechWeaponRegistry.Ensure(pawn);
            return new ChangeScope(GameComponent_MechWeaponRegistry.Get(pawn));
        }

        private sealed class ChangeScope : IDisposable
        {
            private readonly MechWeaponRecord? record;
            public ChangeScope(MechWeaponRecord? record)
            {
                this.record = record;
                if (record != null) record.ControlledChanges++;
            }
            public void Dispose() { if (record != null) record.ControlledChanges--; }
        }

        internal static void EquipmentRemoved(Pawn pawn, ThingWithComps weapon)
        {
            if (Scribe.mode != LoadSaveMode.Inactive) return;
            GameComponent_MechWeaponRegistry.Ensure(pawn);
            MechWeaponRecord? record = GameComponent_MechWeaponRegistry.OwnerOf(weapon)
                ?? GameComponent_MechWeaponRegistry.Get(pawn);
            if (record == null || record.ControlledChanges > 0 || (record.WeaponDef != weapon.def
                && !ReferenceEquals(record.ActiveWeapon, weapon))) return;
            record.Missing = true;
            record.ActiveWeapon = null;
        }

        internal static void EquipmentAdded(Pawn pawn, ThingWithComps weapon)
        {
            if (Scribe.mode != LoadSaveMode.Inactive || !IsManaged(pawn)) return;
            GameComponent_MechWeaponRegistry.Ensure(pawn);
            MechWeaponRecord? record = GameComponent_MechWeaponRegistry.Get(pawn);
            if (record == null || record.ControlledChanges > 0) return;
            if (IsBuiltInFor(pawn, weapon.def))
            {
                record.WeaponDef = weapon.def;
                record.ActiveWeapon = weapon;
                record.Missing = false;
                record.ExternalMode = false;
            }
            else if (weapon.def.equipmentType == EquipmentType.Primary)
                record.ExternalMode = true;
        }

        internal static bool Missing(Pawn pawn)
        {
            MechWeaponRecord? record = GameComponent_MechWeaponRegistry.Get(pawn);
            return record != null && (record.Missing || record.ActiveWeapon?.Destroyed == true);
        }

        internal static bool Borrowed(MechWeaponRecord record) => record.ActiveWeapon != null
            && !record.ActiveWeapon.Destroyed
            && !ReferenceEquals(record.Pawn?.equipment?.Primary, record.ActiveWeapon)
            && record.Pawn?.inventory?.innerContainer.Contains(record.ActiveWeapon) != true;

        internal static bool CanOperate(Pawn pawn) => IsManaged(pawn) && pawn.Spawned
            && !pawn.Dead && !pawn.Downed && !pawn.InMentalState && pawn.Faction == Faction.OfPlayer
            && pawn.equipment != null && pawn.inventory != null
            && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation);

        internal static bool CanUseOrdinary(Pawn pawn, Thing thing) => thing is ThingWithComps
            && !thing.Destroyed && thing.def.IsWeapon && thing.def.equipmentType == EquipmentType.Primary
            && !IsBuiltIn(thing.def) && thing.TryGetComp<CompEquippable>() != null
            && !pawn.WorkTagIsDisabled(WorkTags.Violent)
            && (!thing.def.IsRangedWeapon || !pawn.WorkTagIsDisabled(WorkTags.Shooting))
            && (!pawn.IsQuestLodger() || EquipmentUtility.QuestLodgerCanEquip(thing, pawn))
            && EquipmentUtility.CanEquip(thing, pawn);

        internal static ThingWithComps? FirstInventoryWeapon(Pawn pawn) =>
            pawn.inventory?.innerContainer.FirstOrDefault(t => CanUseOrdinary(pawn, t)) as ThingWithComps;

        /// <summary>替换专武时先验证新武器装备成功，再销毁旧实体；外部武器模式不改变部件损坏状态。</summary>
        internal static bool EquipOrdinary(Pawn pawn, ThingWithComps weapon)
        {
            MechWeaponRecord? record = GameComponent_MechWeaponRegistry.Get(pawn);
            if (record == null || pawn.equipment == null || !CanUseOrdinary(pawn, weapon)
                || Borrowed(record)) return false;
            ThingWithComps? old = pawn.equipment.Primary;
            if (old != null && !IsBuiltInFor(pawn, old.def)) return false;
            if (old != null && pawn.IsQuestLodger() && !EquipmentUtility.QuestLodgerCanUnequip(old, pawn)) return false;
            ThingOwner? source = weapon.holdingOwner;
            if (weapon.Spawned) return false; // 地面装备由原版任务先取出物品。
            // 叠放武器只取一把，其余仍留在原货物栏。
            if (weapon.stackCount > 1) weapon = (ThingWithComps)weapon.SplitOff(1);
            using (ControlledChange(pawn))
            {
                source?.Remove(weapon);
                if (old != null) pawn.equipment.Remove(old);
                try
                {
                    pawn.equipment.AddEquipment(weapon);
                    if (!ReferenceEquals(pawn.equipment.Primary, weapon)) return false;
                    if (old != null) old.Destroy();
                    record.ActiveWeapon = null;
                    record.ExternalMode = true;
                    return true;
                }
                finally
                {
                    if (!ReferenceEquals(pawn.equipment.Primary, weapon))
                    {
                        if (old != null && !old.Destroyed) pawn.equipment.AddEquipment(old);
                        if (!weapon.Destroyed && weapon.holdingOwner == null)
                        {
                            if (source == null || !source.TryAdd(weapon, false))
                                pawn.inventory?.innerContainer.TryAdd(weapon, false);
                        }
                    }
                }
            }
        }

        internal static bool EquipBuiltIn(Pawn pawn, bool repairing = false, ThingDef? variant = null)
        {
            MechWeaponRecord? record = GameComponent_MechWeaponRegistry.Get(pawn);
            if (record?.WeaponDef == null || pawn.equipment == null || Borrowed(record)
                || (!repairing && Missing(pawn))) return false;
            ThingDef def = variant ?? record.WeaponDef;
            if (variant != null && !IsBuiltInFor(pawn, variant)) return false;
            ThingWithComps? old = pawn.equipment.Primary;
            bool oldBuiltIn = old != null && IsBuiltInFor(pawn, old.def);
            if (old != null && !oldBuiltIn && pawn.inventory == null) return false;
            if (old != null && pawn.IsQuestLodger() && !EquipmentUtility.QuestLodgerCanUnequip(old, pawn)) return false;
            // 合体回收时可能先暂存货物栏，必须取回原实例而不是生成第二把。
            ThingWithComps? stored = record.ActiveWeapon;
            bool reuse = stored != null && !stored.Destroyed && stored.def == def
                && pawn.inventory?.innerContainer.Contains(stored) == true;
            ThingWithComps created = reuse ? stored! : (ThingWithComps)ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def));
            using (ControlledChange(pawn))
            {
                try
                {
                    if (old != null)
                    {
                        if (oldBuiltIn) pawn.equipment.Remove(old);
                        else if (!pawn.equipment.GetDirectlyHeldThings().TryTransferToContainer(
                            old, pawn.inventory!.innerContainer, false)) return false;
                    }
                    if (reuse) pawn.inventory!.innerContainer.Remove(created);
                    pawn.equipment.AddEquipment(created);
                    if (!ReferenceEquals(pawn.equipment.Primary, created)) return false;
                    if (oldBuiltIn) old!.Destroy();
                    record.WeaponDef = def;
                    record.ActiveWeapon = created;
                    record.Missing = false;
                    record.ExternalMode = false;
                    return true;
                }
                finally
                {
                    if (!ReferenceEquals(pawn.equipment.Primary, created))
                    {
                        if (reuse)
                        {
                            if (!created.Destroyed && created.holdingOwner == null) pawn.inventory!.innerContainer.TryAdd(created, false);
                        }
                        else if (!created.Destroyed) created.Destroy();
                        if (old != null && !old.Destroyed && pawn.equipment.Primary == null)
                        {
                            old.holdingOwner?.Remove(old);
                            pawn.equipment.AddEquipment(old);
                        }
                    }
                }
            }
        }

        internal static void RepairWeapon(Pawn pawn)
        {
            GameComponent_MechWeaponRegistry.Ensure(pawn);
            MechWeaponRecord? record = GameComponent_MechWeaponRegistry.Get(pawn);
            if (record == null || !Missing(pawn) || Borrowed(record)) return;
            if (record.ExternalMode || pawn.equipment?.Primary != null)
            {
                record.Missing = false;
                record.ActiveWeapon = null;
                return;
            }
            EquipBuiltIn(pawn, repairing: true);
        }

        internal static IEnumerable<Gizmo> GetGizmos(Pawn pawn)
        {
            MechWeaponRecord? record = GameComponent_MechWeaponRegistry.Get(pawn);
            if (record?.WeaponDef == null || !IsManaged(pawn) || pawn.Faction != Faction.OfPlayer) yield break;
            ThingWithComps? primary = pawn.equipment?.Primary;
            bool usingBuiltIn = primary != null && IsBuiltInFor(pawn, primary.def);
            if (!usingBuiltIn)
            {
                var command = CreateCommand(record.WeaponDef, () =>
                {
                    if (CanOperate(pawn) && !Missing(pawn) && !Borrowed(record))
                        EquipBuiltIn(pawn);
                });
                if (Missing(pawn)) command.Disable("MAP_MechWeapon.Missing".Translate());
                else if (!CanOperate(pawn) || Borrowed(record)) command.Disable("MAP_MechWeapon.Unavailable".Translate());
                yield return command;
            }
            if (usingBuiltIn || primary == null)
            {
                ThingWithComps? target = FirstInventoryWeapon(pawn);
                if (target == null) yield break;
                var command = CreateCommand(target.def, () =>
                {
                    if (!CanOperate(pawn) || !ReferenceEquals(FirstInventoryWeapon(pawn), target)) return;
                    string confirmation = EquipmentUtility.GetPersonaWeaponConfirmationText(target, pawn);
                    if (!confirmation.NullOrEmpty())
                        Find.WindowStack.Add(new Dialog_MessageBox(confirmation, "Yes".Translate(), () =>
                        {
                            if (CanOperate(pawn) && ReferenceEquals(FirstInventoryWeapon(pawn), target)) EquipOrdinary(pawn, target);
                        }, "No".Translate()));
                    else EquipOrdinary(pawn, target);
                });
                if (!CanOperate(pawn) || Borrowed(record)) command.Disable("MAP_MechWeapon.Unavailable".Translate());
                yield return command;
            }
        }

        private static Command_Action CreateCommand(ThingDef def, Action action) => new Command_Action
        {
            defaultLabel = "MAP_MechWeapon.Switch".Translate(def.label),
            defaultDesc = "MAP_MechWeapon.SwitchDesc".Translate(),
            icon = def.uiIcon,
            action = action
        };
    }
}
