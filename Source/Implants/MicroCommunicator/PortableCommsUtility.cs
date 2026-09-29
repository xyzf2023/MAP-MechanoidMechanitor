using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using MAP_MechanoidMechanitor.Scenarios;

namespace MAP_MechanoidMechanitor
{
    public static class PortableCommsUtility
    {
        public static bool HasMicroCommunicator(Pawn? pawn)
        {
            return pawn != null
                && pawn.health?.hediffSet != null
                && pawn.health.hediffSet.HasHediff(
                    MAPMechanitor_HediffDefOf.MAP_MicroCommunicator);
        }

        public static bool CanUsePortableComms(
            Pawn? pawn,
            out string reason)
        {
            reason = string.Empty;

            if (pawn == null)
            {
                reason = "MAP_MicroCommunicator_Disabled".Translate();
                return false;
            }

            if (pawn.Dead || pawn.Destroyed)
            {
                reason = "MAP_MicroCommunicator_Disabled".Translate();
                return false;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                reason = "MAP_MicroCommunicator_Disabled".Translate();
                return false;
            }

            if (!pawn.Spawned)
            {
                reason = "MAP_MicroCommunicator_Disabled".Translate();
                return false;
            }

            Map? map = pawn.Map;
            if (map == null)
            {
                reason = "MAP_MicroCommunicator_Disabled".Translate();
                return false;
            }

            if (!HasMicroCommunicator(pawn))
            {
                reason = "MAP_MicroCommunicator_Disabled".Translate();
                return false;
            }

            // 太阳耀斑：必须严格使用本 MOD 专用提示，不得复用原版
            // CannotUseSolarFlare。
            if (map.gameConditionManager.ElectricityDisabled(map))
            {
                reason = "MAP_MicroCommunicator_SolarFlareDisabled".Translate();
                return false;
            }

            return true;
        }

        // 所有由微型通讯器产生的菜单动作在真正执行前，
        // 统一再次校验植入体 / 地图 / 耀斑等状态。
        private static bool TryRevalidatePortableComms(Pawn pawn)
        {
            if (CanUsePortableComms(pawn, out string reason))
            {
                return true;
            }

            Messages.Message(
                reason,
                pawn,
                MessageTypeDefOf.RejectInput,
                historical: false);

            return false;
        }

        // 复制原版 Faction.LeaderIsAvailableToTalk() 的纯显示/可用判断。
        // 该方法为 Faction 私有，这里严格内联其等价逻辑，不调用反射。
        private static bool IsFactionLeaderAvailableToTalk(Faction faction)
        {
            Pawn? leader = faction.leader;

            if (leader == null)
            {
                return false;
            }

            if (leader.Spawned
                && (leader.Downed
                    || leader.IsPrisoner
                    || !leader.Awake()
                    || leader.InMentalState))
            {
                return false;
            }

            return true;
        }

        public static void OpenCommsMenu(Pawn pawn)
        {
            if (!CanUsePortableComms(pawn, out string reason))
            {
                Messages.Message(
                    reason,
                    pawn,
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>();

            AddStandardCommunicationOptions(pawn, options);
            AddMapSpecialCommunicationOptions(pawn, options);

            if (options.Count == 0)
            {
                options.Add(
                    new FloatMenuOption(
                        "MAP_MicroCommunicator_NoTargets".Translate(),
                        null));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void AddStandardCommunicationOptions(
            Pawn pawn,
            List<FloatMenuOption> options)
        {
            // 标准通讯（派系 / 轨道商船）仍遵守 Talking 能力要求。
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking))
            {
                options.Add(
                    new FloatMenuOption(
                        "CannotUseReason".Translate(
                            "IncapableOfCapacity".Translate(
                                PawnCapacityDefOf.Talking.label,
                                pawn.Named("PAWN"))),
                        null));
                return;
            }

            AddPassingShipOptions(pawn, options);
            AddFactionOptions(pawn, options);
        }

        private static void AddPassingShipOptions(
            Pawn pawn,
            List<FloatMenuOption> options)
        {
            Map? map = pawn.Map;
            if (map == null)
            {
                return;
            }

            List<PassingShip>? passingShips =
                map.passingShipManager?.passingShips;
            if (passingShips == null)
            {
                return;
            }

            foreach (PassingShip ship in passingShips)
            {
                // 仅明确安全支持 TradeShip 及其子类。
                // 其他未知 PassingShip / ICommunicable 直接忽略，
                // 绝不调用 unknown.TryOpenComms(pawn)。
                if (ship is TradeShip tradeShip)
                {
                    AddTradeShipOption(pawn, tradeShip, options);
                }
            }
        }

        private static void AddTradeShipOption(
            Pawn pawn,
            TradeShip ship,
            List<FloatMenuOption> options)
        {
            string label = "CallOnRadio".Translate(ship.GetCallLabel());

            // 菜单创建时即计算 AcceptanceReport，严格镜像原版
            // PassingShip.CommFloatMenuOption 的拒绝语义。
            AcceptanceReport initialReport =
                pawn.CanTradeWith(ship.Faction, ship.TraderKind);

            System.Action? action = null;

            if (!initialReport.Accepted)
            {
                if (!initialReport.Reason.NullOrEmpty())
                {
                    string initialReason = initialReport.Reason;

                    action = () =>
                    {
                        if (!TryRevalidatePortableComms(pawn))
                        {
                            return;
                        }

                        Messages.Message(
                            initialReason,
                            pawn,
                            MessageTypeDefOf.RejectInput,
                            historical: false);
                    };
                }
                // Rejected 且 Reason 为空 -> action 保持 null，菜单项 disabled。
            }
            else
            {
                action = () =>
                {
                    if (!TryRevalidatePortableComms(pawn))
                    {
                        return;
                    }

                    if (!ship.CanTradeNow)
                    {
                        return;
                    }

                    // 菜单打开后派系关系 / 能力 / permit 等可能变化，
                    // 点击时重新计算一次。
                    AcceptanceReport currentReport =
                        pawn.CanTradeWith(ship.Faction, ship.TraderKind);
                    if (!currentReport.Accepted)
                    {
                        if (!currentReport.Reason.NullOrEmpty())
                        {
                            Messages.Message(
                                currentReport.Reason,
                                pawn,
                                MessageTypeDefOf.RejectInput,
                                historical: false);
                        }

                        return;
                    }

                    Map? map = pawn.Map;
                    if (map == null)
                    {
                        return;
                    }

                    // 微型通讯器只替代通讯台，绝不替代轨道贸易信标。
                    if (!Building_OrbitalTradeBeacon
                            .AllPowered(map)
                            .Any())
                    {
                        Messages.Message(
                            "MessageNeedBeaconToTradeWithShip".Translate(),
                            pawn,
                            MessageTypeDefOf.RejectInput,
                            historical: false);
                        return;
                    }

                    // 最终动作直接复用 TradeShip.TryOpenComms(pawn)，
                    // 不自行 new Dialog_Trade（保留原版教程/信件等附带行为）。
                    ship.TryOpenComms(pawn);
                };
            }

            options.Add(new FloatMenuOption(label, action));
        }

        private static void AddFactionOptions(
            Pawn pawn,
            List<FloatMenuOption> options)
        {
            foreach (Faction faction in
                     Find.FactionManager.AllFactionsVisibleInViewOrder)
            {
                if (faction == null || faction.temporary || faction.IsPlayer)
                {
                    continue;
                }

                string text = "CallOnRadio".Translate(faction.GetCallLabel());
                text = text + " ("
                    + faction.PlayerRelationKind.GetLabelCap() + ", "
                    + faction.PlayerGoodwill.ToStringWithSign() + ")";

                // 复制原版 Faction.CommFloatMenuOption 的纯显示逻辑。
                // LeaderIsAvailableToTalk() 是 Faction 私有方法，这里复用等价判断。
                if (!IsFactionLeaderAvailableToTalk(faction))
                {
                    string text2 = (faction.leader == null)
                        ? "LeaderUnavailableNoLeader".Translate()
                        : "LeaderUnavailable".Translate(
                            faction.leader.LabelShort,
                            faction.leader);
                    options.Add(
                        new FloatMenuOption(
                            text + " (" + text2 + ")",
                            null,
                            faction.def.FactionIcon,
                            faction.Color));
                    continue;
                }

                FloatMenuOption option = new FloatMenuOption(
                    text,
                    () =>
                    {
                        if (!TryRevalidatePortableComms(pawn))
                        {
                            return;
                        }

                        // 菜单打开后领袖状态也可能变化，点击时再次验证。
                        if (!IsFactionLeaderAvailableToTalk(faction))
                        {
                            return;
                        }

                        faction.TryOpenComms(pawn);
                    },
                    faction.def.FactionIcon,
                    faction.Color,
                    MenuOptionPriority.InitiateSocial);
                options.Add(option);
            }
        }

        private static void AddMapSpecialCommunicationOptions(
            Pawn pawn,
            List<FloatMenuOption> options)
        {
            // 1. 联络机械主脑
            if (MechanoidMechanitorMechHiveCommunicationUtility
                    .TryGetContactableMechHive(out _))
            {
                options.Add(
                    new FloatMenuOption(
                        MechanoidMechanitorMechHiveCommunicationUtility
                            .ContactOvermindLabel,
                        () =>
                        {
                            if (!TryRevalidatePortableComms(pawn))
                            {
                                return;
                            }

                            if (!MechanoidMechanitorMechHiveCommunicationUtility
                                    .TryGetContactableMechHive(out _))
                            {
                                return;
                            }

                            MechanoidMechanitorMechHiveCommunicationUtility
                                .TryOpenContactOvermindDialog(
                                    pawn,
                                    pawn.Map);
                        }));
            }

            // 2. 接入共生盟约
            if (SymbiosisCovenantCommunicationUtility.CanAccessCovenant()
                && !SymbiosisCovenantCommunicationUtility.CanBroadcastDeclaration())
            {
                options.Add(
                    new FloatMenuOption(
                        SymbiosisCovenantCommunicationUtility.AccessLabel,
                        () =>
                        {
                            if (!TryRevalidatePortableComms(pawn))
                            {
                                return;
                            }

                            if (!SymbiosisCovenantCommunicationUtility
                                    .CanAccessCovenant())
                            {
                                return;
                            }

                            SymbiosisCovenantCommunicationUtility
                                .TryOpenDialog(pawn);
                        }));
            }

            // 3. 广播脱离声明（便携专用确认入口，无 console）
            if (SymbiosisCovenantCommunicationUtility
                    .CanBroadcastDeclaration())
            {
                options.Add(
                    new FloatMenuOption(
                        SymbiosisCovenantCommunicationUtility
                            .DeclarationLabel,
                        () =>
                        {
                            if (!TryRevalidatePortableComms(pawn))
                            {
                                return;
                            }

                            if (!SymbiosisCovenantCommunicationUtility
                                    .CanBroadcastDeclaration())
                            {
                                return;
                            }

                            SymbiosisCovenantCommunicationUtility
                                .ShowPortableDeclarationConfirmation(pawn);
                        }));
            }
        }
    }
}
