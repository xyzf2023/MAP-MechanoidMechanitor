using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.MobileDragoon
{
    /// <summary>月亮的手动上下机入口；菜单、任务和模块转移继续由外骨骼框架执行。</summary>
    internal static class MobileDragoonMoonInteractionPatch
    {
        private static Type coreType = null!;
        private static MethodInfo acceptablePawnKind = null!;

        internal static void Configure(Type resolvedCoreType, MethodInfo resolvedAcceptablePawnKind)
        {
            coreType = resolvedCoreType;
            acceptablePawnKind = resolvedAcceptablePawnKind;
        }

        internal static bool CanOperatePilot(Pawn? pawn) =>
            MobileDragoonMoonPilotPatch.Enabled
            && MobileDragoonMoonPilotPatch.IsMoon(pawn)
            && GearInteractionUtility.CanOperate(pawn)
            && pawn!.IsColonyMechPlayerControlled
            && pawn.apparel != null && pawn.drafter != null
            && FloatMenuMakerMap.ShouldGenerateFloatMenuForPawn(pawn).Accepted;

        private static Apparel? WornCore(Pawn pawn) =>
            pawn.apparel?.WornApparel.FirstOrDefault(apparel => coreType.IsInstanceOfType(apparel));

        internal static bool CanUseBay(Pawn pawn, Thing bay)
        {
            if (!CanOperatePilot(pawn) || bay == null || !bay.Spawned || bay.Destroyed
                || bay.Map != pawn.Map || bay.Faction != Faction.OfPlayer
                || bay.IsForbidden(pawn) || bay.Position.Fogged(bay.Map)
                || !MobileDragoonMoonPilotPatch.IsDragoonContext(bay, pawn, true))
                return false;

            // 上游判断的是任意外骨骼核心；穿着其他外骨骼时不借龙骑兵上下文放行。
            Apparel? worn = WornCore(pawn);
            return worn == null || MobileDragoonMoonPilotPatch.IsDragoonCore(worn);
        }

        internal static void PostfixBayOptions(Thing __instance, Pawn __0,
            ref IEnumerable<FloatMenuOption> __result)
        {
            if (!MobileDragoonMoonPilotPatch.Enabled || !MobileDragoonMoonPilotPatch.IsMoon(__0)
                || !MobileDragoonMoonPilotPatch.IsDragoonContext(__instance, __0, true))
                return;

            MobileDragoonMoonPilotPatch.TryGetCore(__instance, out Thing? bayCore);
            __result = ProtectOptions(__result ?? Enumerable.Empty<FloatMenuOption>(),
                __0, __instance, bayCore, WornCore(__0));
        }

        private static IEnumerable<FloatMenuOption> ProtectOptions(IEnumerable<FloatMenuOption> options,
            Pawn pawn, Thing bay, Thing? expectedBayCore, Apparel? expectedWornCore)
        {
            foreach (FloatMenuOption option in options)
            {
                Action? original = option.action;
                if (original != null)
                {
                    if (!CanUseBay(pawn, bay))
                    {
                        option.action = null;
                        option.Label += ": " + "NotAllowed".Translate();
                    }
                    else
                    {
                        bool executed = false;
                        option.action = () =>
                        {
                            // 菜单弹出后可能更换核心、失去控制或换图，不能沿用上游捕获的旧方向。
                            if (executed || !CanUseBay(pawn, bay)
                                || !MobileDragoonMoonPilotPatch.TryGetCore(bay, out Thing? currentCore)
                                || !ReferenceEquals(currentCore, expectedBayCore)
                                || !ReferenceEquals(WornCore(pawn), expectedWornCore)
                                || (currentCore != null) == (expectedWornCore != null)
                                || !(AcceptanceReport)acceptablePawnKind.Invoke(bay, new object[] { pawn })!
                                || !pawn.CanReach(bay, PathEndMode.InteractionCell, Danger.Deadly))
                                return;

                            // 预留由上游任务处理，保留玩家强制订单抢占已有预留的原有行为。
                            executed = true;
                            original();
                        };
                    }
                }
                // 保留上游禁用原因、优先工作装饰和点击目标复查信息。
                yield return option;
            }
        }

        internal static void PostfixPawnGizmos(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            // 上游为真实玩家殖民者添加按钮；仅补它提前返回的月亮，避免重复。
            if (__instance.IsColonistPlayerControlled || !CanOperatePilot(__instance)
                || Find.Selector.SingleSelectedThing != __instance)
                return;
            Apparel? worn = WornCore(__instance);
            if (worn != null && !MobileDragoonMoonPilotPatch.IsDragoonCore(worn))
                return;

            bool boarding = worn == null;
            var command = new Command_Action
            {
                defaultLabel = (boarding ? "WG_GetIn" : "WG_GetOff").Translate(),
                defaultDesc = (boarding ? "MAP_MobileDragoon.GetInDesc" : "MAP_MobileDragoon.GetOffDesc").Translate(),
                icon = ContentFinder<Texture2D>.Get(boarding ? "UI/GetInWalker" : "UI/GetOffWalker", false)
                    ?? TexCommand.DesirePower,
                action = () => OpenBayMenu(__instance, boarding)
            };
            if (!AvailableBays(__instance, boarding).Any())
                command.Disable("MAP_MobileDragoon.NoAvailableBay".Translate());

            __result = (__result ?? Enumerable.Empty<Gizmo>()).Concat(new[] { command });
        }

        private static IEnumerable<Building> AvailableBays(Pawn pawn, bool boarding)
        {
            if (!CanOperatePilot(pawn)) yield break;
            foreach (Building bay in pawn.Map.listerBuildings.AllBuildingsColonistOfClass<Building>())
            {
                if (MobileDragoonMoonPilotPatch.TryGetCore(bay, out Thing? core)
                    && (core != null) == boarding && CanUseBay(pawn, bay))
                    yield return bay;
            }
        }

        private static void OpenBayMenu(Pawn pawn, bool boarding)
        {
            if (!CanOperatePilot(pawn) || (WornCore(pawn) == null) != boarding)
                return;

            var options = new List<FloatMenuOption>();
            // 分配过的整备台优先；未分配时仍可选择合法整备台，特别是空台下机。
            foreach (Building bay in AvailableBays(pawn, boarding)
                .OrderByDescending(b => b.GetComp<CompAssignableToPawn>()?.AssignedPawns.Contains(pawn) == true)
                .ThenBy(b => b.Position.DistanceToSquared(pawn.Position)))
            {
                foreach (FloatMenuOption option in bay.GetFloatMenuOptions(pawn))
                {
                    option.Label += $" ({bay.Position.x}, {bay.Position.z})";
                    options.Add(option);
                }
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption("MAP_MobileDragoon.NoAvailableBay".Translate(), null));
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }

    /// <summary>普通月亮专用整备台菜单，不授予其他殖民者交互资格。</summary>
    public sealed class FloatMenuOptionProvider_MobileDragoonMoon : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool MechanoidCanDo => true;

        protected override bool AppliesInt(FloatMenuContext context) =>
            MobileDragoonMoonInteractionPatch.CanOperatePilot(context.FirstSelectedPawn)
            && !CompColonistLikeFloatMenuUser.PawnCanUseColonistLikeFloatMenu(context.FirstSelectedPawn);

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing clickedThing, FloatMenuContext context)
        {
            Pawn pawn = context.FirstSelectedPawn;
            if (!AppliesInt(context) || !MobileDragoonMoonInteractionPatch.CanUseBay(pawn, clickedThing))
                yield break;

            foreach (FloatMenuOption option in clickedThing.GetFloatMenuOptions(pawn))
                yield return option;
        }
    }
}
