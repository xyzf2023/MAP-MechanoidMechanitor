using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.RPGStyleInventory
{
    internal sealed class RPGInventoryContext
    {
        internal readonly Type TabType;
        internal readonly bool Revamped;
        internal readonly MethodInfo CanControl;
        internal readonly MethodInfo CanControlColonist;
        internal readonly MethodInfo SelectedPawn;
        internal readonly MethodInfo Drop;
        internal readonly FieldInfo? CachedPawn;
        internal bool Enabled;
        internal bool LayoutEnabled;
        internal bool CEEnabled;

        internal RPGInventoryContext(Type type, bool revamped, MethodInfo control,
            MethodInfo colonist, MethodInfo selectedPawn, MethodInfo drop, FieldInfo? cachedPawn)
        {
            TabType = type;
            Revamped = revamped;
            CanControl = control;
            CanControlColonist = colonist;
            SelectedPawn = selectedPawn;
            Drop = drop;
            CachedPawn = cachedPawn;
        }
    }

    internal static class RPGInventoryRuntime
    {
        internal const string TabTypeName = "Sandy_Detailed_RPG_Inventory.Sandy_Detailed_RPG_GearTab";
        internal const string OriginalPackageId = "Sandy.RPGStyleInventory";
        internal const string RevampedPackageId = "Sandy.RPGStyleInventory.avilmask.Revamped";
        private static readonly Dictionary<Type, RPGInventoryContext> Contexts = new Dictionary<Type, RPGInventoryContext>();
        [ThreadStatic] private static object? approvedDropTab;
        [ThreadStatic] private static Thing? approvedDropThing;

        internal static void Register(RPGInventoryContext context) => Contexts[context.TabType] = context;
        internal static RPGInventoryContext? For(object tab) => For(tab.GetType());
        internal static RPGInventoryContext? For(Type type)
        {
            for (Type? current = type; current != null; current = current.BaseType)
                if (Contexts.TryGetValue(current, out RPGInventoryContext value))
                    return value;
            return null;
        }

        internal static object? Invoke(MethodInfo method, object? instance, params object?[] arguments)
        {
            try { return method.Invoke(instance, arguments); }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        internal static Pawn? CurrentPawn(object tab)
        {
            RPGInventoryContext? context = For(tab);
            Pawn? selected = ColonistLikeInspectTabUtility.ResolvePawn(Find.Selector?.SingleSelectedThing);
            if (context == null || selected == null)
                return null;
            // Revamped 的执行方法使用共享缓存，不能把新选择与旧缓存混在一起。
            if (context.CachedPawn != null && !ReferenceEquals(context.CachedPawn.GetValue(tab), selected))
                return null;
            return ReferenceEquals(Invoke(context.SelectedPawn, tab), selected) ? selected : null;
        }

        internal static bool ShouldProtect(Pawn pawn, Thing thing) =>
            GearInteractionUtility.HasGearCapability(pawn) || GearInteractionUtility.IsProtectedBuiltIn(pawn, thing);

        internal static bool CanDrop(object tab, Pawn pawn, Thing thing) =>
            ReferenceEquals(CurrentPawn(tab), pawn) && pawn.Spawned && !pawn.Dead && !pawn.Destroyed
            && pawn.jobs != null && pawn.inventory != null
            && For(tab) is RPGInventoryContext context && (bool)Invoke(context.CanControl, tab)!
            && (!ShouldProtect(pawn, thing) || GearInteractionUtility.CanOperate(pawn))
            && GearInteractionUtility.CanDrop(pawn, thing, respectKindLock: !context.Revamped || ShouldProtect(pawn, thing));

        internal static bool DropIsApproved(object tab, Thing thing) =>
            ReferenceEquals(approvedDropTab, tab) && ReferenceEquals(approvedDropThing, thing);

        internal static void RequestDrop(object tab, Pawn pawn, Thing thing)
        {
            RPGInventoryContext context = For(tab)!;
            bool executed = false;
            Action action = () =>
            {
                if (executed || !context.Enabled || !CanDrop(tab, pawn, thing))
                    return;
                executed = true;
                object? previousTab = approvedDropTab;
                Thing? previousThing = approvedDropThing;
                approvedDropTab = tab;
                approvedDropThing = thing;
                try { Invoke(context.Drop, tab, thing); }
                finally { approvedDropTab = previousTab; approvedDropThing = previousThing; }
            };
            if (!ModsConfig.BiotechActive || thing is not Apparel apparel
                || pawn.apparel?.WornApparel.Contains(apparel) != true
                || !MechanitorUtility.TryConfirmBandwidthLossFromDroppingThing(pawn, thing, action))
                action();
        }

        internal static void ProtectMenu(object tab, Pawn pawn, Thing thing, List<FloatMenuOption> options)
        {
            RPGInventoryContext? context = For(tab);
            if (context?.Enabled != true || !ShouldProtect(pawn, thing))
                return;
            // 两个已校验菜单均先创建信息卡。只包装之后的操作，不按翻译文本识别菜单。
            for (int i = 1; i < options.Count; i++)
            {
                FloatMenuOption option = options[i];
                Action? original = option.action;
                if (original == null)
                    continue;
                if (!CanDrop(tab, pawn, thing))
                {
                    option.action = null;
                    option.Label += ": " + GearInteractionUtility.DropBlockedReason(pawn, thing);
                    continue;
                }
                option.action = () =>
                {
                    if (context.Enabled && CanDrop(tab, pawn, thing))
                        original();
                };
            }
        }

        internal static bool CanDisplay(Pawn? pawn) => pawn != null && !pawn.Discarded
            && pawn.inventory != null && pawn.equipment != null && pawn.apparel != null
            && GearInteractionUtility.HasGearCapability(pawn);

        internal static bool CanDisplayApparel(Pawn? pawn) => pawn?.apparel != null
            && HumanApparelUtility.TryGetApparelComp(pawn, out _);

        // 只保护读取，不创建 Tracker，也不写入强制服装数据。
        internal static TraitSet? ReadTraits(Pawn pawn) => pawn.story?.traits;
        internal static bool HasTrait(TraitSet? traits, TraitDef trait) => traits?.HasTrait(trait) == true;
        internal static OutfitForcedHandler? ReadForcedHandler(Pawn pawn) => pawn.outfits?.forcedHandler;
        internal static bool IsForced(OutfitForcedHandler? handler, Apparel apparel) =>
            apparel != null && !apparel.Destroyed && handler?.IsForced(apparel) == true;
    }
}
