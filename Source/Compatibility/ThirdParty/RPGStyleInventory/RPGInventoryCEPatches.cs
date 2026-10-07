using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.RPGStyleInventory
{
    internal static class RPGInventoryCEPatches
    {
        internal static RPGInventoryContext? Context;
        internal static MethodInfo? QuestLock;
        internal static MethodInfo? SwitchWeapon;
        internal static MethodInfo? DropHaul;
        internal static bool Enabled;
        [ThreadStatic] private static MenuInvocation? menu;
        [ThreadStatic] private static Thing? approvedSwitch;
        [ThreadStatic] private static Thing? approvedHaul;
        private static readonly List<Apparel> EmptyApparel = new List<Apparel>();

        private sealed class MenuInvocation
        {
            internal readonly object Tab;
            internal readonly Pawn Pawn;
            internal readonly Thing Thing;
            internal MenuInvocation(object tab, Pawn pawn, Thing thing) { Tab = tab; Pawn = pawn; Thing = thing; }
        }

        private static void InMenu(MenuInvocation invocation, Action action)
        {
            MenuInvocation? previous = menu;
            menu = invocation;
            try { action(); }
            finally { menu = previous; }
        }

        private static bool Valid(MenuInvocation invocation) => Context?.Enabled == true
            && RPGInventoryRuntime.CanDrop(invocation.Tab, invocation.Pawn, invocation.Thing);

        public static void MenuPostfix(object tab, Thing thing, List<FloatMenuOption> __result)
        {
            if (!Enabled || Context?.Enabled != true || RPGInventoryRuntime.CurrentPawn(tab) is not Pawn pawn)
                return;
            RPGInventoryRuntime.ProtectMenu(tab, pawn, thing, __result);
            MenuInvocation invocation = new MenuInvocation(tab, pawn, thing);
            for (int i = 1; i < __result.Count; i++)
            {
                Action? original = __result[i].action;
                if (original == null) continue;
                __result[i].action = () =>
                {
                    if (Enabled && Valid(invocation)) InMenu(invocation, original);
                };
            }
        }

        public static IEnumerable<CodeInstruction> MenuTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = instructions.ToList();
            MethodInfo enumerate = AccessTools.DeclaredMethod(typeof(List<Apparel>), "GetEnumerator", Type.EmptyTypes);
            MethodInfo canEat = AccessTools.DeclaredMethod(typeof(RaceProperties), nameof(RaceProperties.CanEverEat), new[] { typeof(Thing) });
            if (codes.Count(code => code.Calls(enumerate)) != 1 || QuestLock == null
                || codes.Count(code => code.Calls(QuestLock)) != 1 || codes.Count(code => code.Calls(canEat)) != 1)
                throw new InvalidOperationException("RPG CE 菜单的服装枚举、任务锁或服用调用不唯一。");
            foreach (CodeInstruction code in codes)
            {
                string? replacement = code.Calls(enumerate) ? nameof(ApparelEnumerator)
                    : code.Calls(QuestLock) ? nameof(IsQuestOrGearLocked) : null;
                if (replacement == null) continue;
                code.opcode = OpCodes.Call;
                code.operand = AccessTools.DeclaredMethod(typeof(RPGInventoryCEPatches), replacement);
            }
            int consumeIndex = codes.FindIndex(code => code.Calls(canEat));
            codes[consumeIndex].opcode = OpCodes.Ldarg_0; // 静态菜单的第一个参数是 tab。
            codes[consumeIndex].operand = null;
            codes.Insert(consumeIndex + 1, new CodeInstruction(OpCodes.Call,
                AccessTools.DeclaredMethod(typeof(RPGInventoryCEPatches), nameof(CanConsume))));
            return codes;
        }

        internal static List<Apparel>.Enumerator ApparelEnumerator(List<Apparel>? apparel) =>
            (apparel ?? EmptyApparel).GetEnumerator();

        internal static bool CanConsume(RaceProperties race, Thing thing, object tab)
        {
            if (!race.CanEverEat(thing)) return false;
            Pawn? pawn = RPGInventoryRuntime.CurrentPawn(tab);
            return !Enabled || Context?.Enabled != true || pawn == null
                || !GearInteractionUtility.HasGearCapability(pawn) || pawn.IsColonistPlayerControlled;
        }

        internal static bool IsQuestOrGearLocked(Pawn pawn, Thing thing)
        {
            bool original = (bool)RPGInventoryRuntime.Invoke(QuestLock!, null, pawn, thing)!;
            return original || (Enabled && Context?.Enabled == true && thing != null
                && RPGInventoryRuntime.ShouldProtect(pawn, thing) && !GearInteractionUtility.CanDrop(pawn, thing));
        }

        // 上游 void Prefix 即使原方法被跳过也可能执行；只让已确认的那一次调用修改 CE 持有记录。
        public static bool DropNotificationPrefix(object __0, Thing __1) =>
            !Enabled || Context?.Enabled != true || RPGInventoryRuntime.DropIsApproved(__0, __1);

        public static bool TransferPrefix(Pawn_EquipmentTracker __instance, ThingWithComps eq, ThingOwner container, ref bool __result)
        {
            MenuInvocation? invocation = menu;
            if (!Enabled || invocation == null || !ReferenceEquals(__instance.pawn, invocation.Pawn)
                || !RPGInventoryRuntime.ShouldProtect(invocation.Pawn, eq))
                return true;
            // 换枪也会转移旧 Primary；不能把菜单选中的新武器误作唯一允许转移的物品。
            if (Valid(invocation) && ReferenceEquals(container, invocation.Pawn.inventory?.innerContainer)
                && RPGInventoryRuntime.CanDrop(invocation.Tab, invocation.Pawn, eq))
                return true;
            __result = false;
            return false;
        }

        public static bool SwitchPrefix(object compInventory, ThingWithComps newEq)
        {
            MenuInvocation? invocation = menu;
            if (!Enabled || invocation == null || !RPGInventoryRuntime.ShouldProtect(invocation.Pawn, newEq))
                return true;
            Pawn pawn = invocation.Pawn;
            if (!Valid(invocation) || compInventory is not ThingComp comp || !ReferenceEquals(comp.parent, pawn)
                || !ReferenceEquals(newEq, invocation.Thing)
                || !CompHumanWeaponUser.PawnCanUseHumanWeapons(pawn)
                || !CompHumanWeaponUser.PawnAllowsEquipFloatMenu(pawn)
                || pawn.health?.capacities.CapableOf(PawnCapacityDefOf.Manipulation) != true
                || !MechWeaponUtility.CanUseOrdinary(pawn, newEq)
                || (pawn.equipment?.Primary is ThingWithComps old && pawn.IsQuestLodger()
                    && !EquipmentUtility.QuestLodgerCanUnequip(old, pawn)))
                return false;
            if (ReferenceEquals(approvedSwitch, newEq)) return true;
            string confirmation = EquipmentUtility.GetPersonaWeaponConfirmationText(newEq, pawn);
            if (confirmation.NullOrEmpty()) return true;
            bool executed = false;
            Find.WindowStack.Add(new Dialog_MessageBox(confirmation, "Yes".Translate(), () =>
            {
                if (executed || !Enabled || !Valid(invocation)) return;
                executed = true;
                Thing? previous = approvedSwitch;
                approvedSwitch = newEq;
                try { InMenu(invocation, () => RPGInventoryRuntime.Invoke(SwitchWeapon!, null, compInventory, newEq)); }
                finally { approvedSwitch = previous; }
            }, "No".Translate()));
            return false;
        }

        public static bool HaulPrefix(Pawn pawn, Thing thing, Pawn selPawn)
        {
            MenuInvocation? invocation = menu;
            if (!Enabled || Context?.Enabled != true || invocation == null) return true;
            if (!ReferenceEquals(invocation.Pawn, pawn) || !ReferenceEquals(pawn, selPawn)
                || !ReferenceEquals(invocation.Thing, thing) || !Valid(invocation))
                return false;
            if (ReferenceEquals(approvedHaul, thing)) return true;
            bool executed = false;
            Action action = () =>
            {
                if (executed || !Enabled || !Valid(invocation)) return;
                executed = true;
                Thing? previous = approvedHaul;
                approvedHaul = thing;
                try { InMenu(invocation, () => RPGInventoryRuntime.Invoke(DropHaul!, null, pawn, thing, selPawn)); }
                finally { approvedHaul = previous; }
            };
            if (!ModsConfig.BiotechActive || thing is not Apparel apparel
                || pawn.apparel?.WornApparel.Contains(apparel) != true
                || !MechanitorUtility.TryConfirmBandwidthLossFromDroppingThing(pawn, thing, action))
                action();
            return false;
        }
    }
}
