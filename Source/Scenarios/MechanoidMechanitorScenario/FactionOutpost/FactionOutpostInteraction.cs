using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 普通派系前哨的统一攻击入口。世界菜单、抵达动作和最终地图进入都复用同一合法性检查，
    /// 避免只隐藏按钮却保留可绕过的执行路径。
    /// </summary>
    public static class FactionOutpostInteraction
    {
        public static FloatMenuAcceptanceReport CanAttack(MAPFactionOutpost? outpost)
        {
            if (outpost == null || !outpost.Spawned || outpost.Cleaned)
            {
                return FloatMenuAcceptanceReport.WithFailMessage(
                    "MAP_MechanoidMechanitor.FactionOutpost.Attack.OutpostUnavailable".Translate());
            }

            if (!FactionOutpostRelationUtility.CanPlayerAttack(outpost))
            {
                return FloatMenuAcceptanceReport.WithFailMessage(
                    "MAP_MechanoidMechanitor.FactionOutpost.Attack.RelationBlocked".Translate());
            }

            if (outpost.EnterCooldownBlocksEntering())
            {
                return FloatMenuAcceptanceReport.WithFailMessage(
                    "MessageEnterCooldownBlocksEntering".Translate(
                        outpost.EnterCooldownTicksLeft().ToStringTicksToPeriod()));
            }

            return true;
        }

        public static bool TryFinalizeAttackEntry(
            MAPFactionOutpost? outpost,
            out string? failMessage)
        {
            failMessage = null;
            FloatMenuAcceptanceReport can = CanAttack(outpost);
            if (!can)
            {
                failMessage = can.FailMessage;
                return false;
            }

            return FactionOutpostRelationUtility.TryEnsureHostileForAttackEntry(
                outpost,
                out failMessage);
        }

        public static Action<Action>? GetAttackConfirmProxy(MAPFactionOutpost outpost)
        {
            Faction? owner = outpost?.Faction;
            Faction? player = Faction.OfPlayerSilentFail;
            if (owner == null || player == null || owner.HostileTo(player))
            {
                return null;
            }

            return action => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "ConfirmAttackFriendlyFaction".Translate(outpost!.LabelCap, owner!.Name),
                delegate
                {
                    if ((bool)CanAttack(outpost))
                    {
                        action();
                    }
                }));
        }

        public static IEnumerable<FloatMenuOption> GetCaravanFloatMenuOptions(
            MAPFactionOutpost outpost,
            Caravan caravan)
        {
            if (outpost == null || !outpost.Spawned || outpost.Cleaned)
            {
                yield break;
            }

            // 建成、非敌对、无地图的前哨允许远行队交易或赠送礼物。
            // 交易选项是否实际可用由 CanTradeWith（要求库存）决定；
            // 赠礼选项由 CanOfferGiftsTo（只要求友好服务可用）决定，
            // 因此买空库存后交易被禁用但赠礼仍可用。
            if (outpost.CanInteractAsFriendlyCompletedOutpost)
            {
                foreach (FloatMenuOption option in
                    MAPCaravanArrivalAction_TradeFactionOutpost.GetFloatMenuOptions(caravan, outpost))
                {
                    yield return option;
                }

                foreach (FloatMenuOption option in
                    MAPCaravanArrivalAction_OfferGiftsFactionOutpost.GetFloatMenuOptions(caravan, outpost))
                {
                    yield return option;
                }
            }

            // 攻击选项始终保留（即使非敌对也可通过确认发动攻击）。
            Action<Action>? confirm = GetAttackConfirmProxy(outpost);
            foreach (FloatMenuOption option in CaravanArrivalActionUtility.GetFloatMenuOptions(
                () => CanAttack(outpost),
                () => new MAPCaravanArrivalAction_AttackFactionOutpost(outpost),
                "AttackSettlement".Translate(outpost.Label),
                caravan,
                outpost.Tile,
                outpost,
                confirm))
            {
                yield return option;
            }
        }

        public static IEnumerable<FloatMenuOption> GetTransporterFloatMenuOptions(
            MAPFactionOutpost outpost,
            IEnumerable<IThingHolder> pods,
            Action<PlanetTile, TransportersArrivalAction> launchAction)
        {
            if (outpost == null
                || !outpost.Spawned
                || outpost.Cleaned
                || !TransportersArrivalActionUtility.AnyNonDownedColonist(pods))
            {
                yield break;
            }

            Action<Action>? confirm = GetAttackConfirmProxy(outpost);
            foreach (FloatMenuOption option in TransportersArrivalActionUtility.GetFloatMenuOptions(
                () => MAPTransportersArrivalAction_AttackFactionOutpost.CanAttack(pods, outpost),
                () => new MAPTransportersArrivalAction_AttackFactionOutpost(
                    outpost,
                    PawnsArrivalModeDefOf.EdgeDrop),
                "AttackAndDropAtEdge".Translate(outpost.Label),
                launchAction,
                outpost.Tile,
                confirm))
            {
                yield return option;
            }

            foreach (FloatMenuOption option in TransportersArrivalActionUtility.GetFloatMenuOptions(
                () => MAPTransportersArrivalAction_AttackFactionOutpost.CanAttack(pods, outpost),
                () => new MAPTransportersArrivalAction_AttackFactionOutpost(
                    outpost,
                    PawnsArrivalModeDefOf.CenterDrop),
                "AttackAndDropInCenter".Translate(outpost.Label),
                launchAction,
                outpost.Tile,
                confirm))
            {
                yield return option;
            }
        }
    }
}
