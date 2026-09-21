using System.Collections.Generic;
using MAP_MechanoidMechanitor;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class MechanoidMechanitorScenarioArrivalUtility
    {
        public static Pawn? FindScenarioMechanitorInStartingItems(List<Thing> startingItems)
        {
            Pawn? registered =
                GameComponent_MechanoidMechanitorRegistry.CurrentMechanicalConsciousnessHost;
            if (registered != null
                && startingItems.Contains(registered)
                && !registered.Dead
                && !registered.Destroyed)
            {
                return registered;
            }

            for (int i = 0; i < startingItems.Count; i++)
            {
                if (startingItems[i] is Pawn pawn
                    && !pawn.Dead
                    && !pawn.Destroyed
                    && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
                {
                    return pawn;
                }
            }

            return null;
        }

        public static bool RegisterAndPrepareMechanicalConsciousnessHost(
            Pawn mechanitor,
            bool promoteIfNeeded = true)
        {
            return GameComponent_MechanoidMechanitorRegistry.RegisterScenarioPawn(
                mechanitor,
                promoteIfNeeded);
        }

        public static bool EnsureScenarioMechanitorState(
            Pawn mechanitor,
            PlayerPawnsArriveMethod arrivalMethod,
            bool skipAssignmentOnly)
        {
            MechanoidMechanitorRoleUtility.EnsureRoleState(mechanitor);
            MAPMechanitorNodeLifecycleUtility.EnsureBasicTrackers(mechanitor);

            if (mechanitor.Dead
                || mechanitor.Destroyed
                || !mechanitor.RaceProps.IsMechanoid
                || mechanitor.Faction == null
                || !mechanitor.Faction.IsPlayerSafe()
                || mechanitor.relations == null
                || mechanitor.mechanitor == null
                || !MechanitorUtility.IsMechanitor(mechanitor)
                || mechanitor.mechanitor.controlGroups == null
                || mechanitor.mechanitor.controlGroups.Count == 0)
            {
                if (skipAssignmentOnly)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 机械族机械师剧本" +
                        GetArrivalLabel(arrivalMethod) +
                        "初始化失败：机械师状态不完整，已跳过监管者分配；" +
                        GetArrivalContinuationHint(arrivalMethod));
                }

                return false;
            }

            return true;
        }

        public static bool IsOverseeCandidate(Pawn mech, Pawn mechanitor)
        {
            if (mech == mechanitor
                || mech.Dead
                || mech.Destroyed
                || !mech.RaceProps.IsMechanoid)
            {
                return false;
            }

            if (mech.OverseerSubject == null)
            {
                return false;
            }

            return !AutonomousMechUtility.IsAutonomousMech(mech);
        }

        public static void AssignStartingMechsToMechanitor(
            Pawn mechanitor,
            IEnumerable<Thing> items,
            PlayerPawnsArriveMethod arrivalMethod)
        {
            mechanitor.relations ??= new Pawn_RelationsTracker(mechanitor);
            MAPMechanitorNodeLifecycleUtility.EnsureBasicTrackers(mechanitor);

            foreach (Thing item in items)
            {
                if (item is not Pawn mech || !IsOverseeCandidate(mech, mechanitor))
                {
                    continue;
                }

                // 已分配给本机械师则跳过（避免重复处理）。
                // MAP 节点不应依赖可能由 reflexive 关系返回错误方向的 GetOverseer 来跳过必要初始化；
                // 统一以方向工具作为权威依据（普通原版机械体同样由方向工具正确判定）。
                if (MAPOverseerRelationDirectionUtility.IsActualOverseerOf(mechanitor, mech))
                {
                    continue;
                }

                if (!MAPOverseerAssignmentUtility.TryAssignActualOverseer(mechanitor, mech))
                {
                    Log.Warning(
                        "[MAP-机械族机械师] 机械族机械师剧本" +
                        GetArrivalLabel(arrivalMethod) +
                        "：无法为起始机械体 " +
                        $"{mech.LabelShort}（{mech.kindDef?.defName ?? "unknown"}）" +
                        "分配监管者。");
                }
            }

            // 全部起始机械体循环结束后统一刷新一次，确保监管关系与控制名单最终同步。
            mechanitor.mechanitor?.Notify_BandwidthChanged();

            // 只读校验：不重复添加关系。
            foreach (Thing item in items)
            {
                if (item is not Pawn mech || !IsOverseeCandidate(mech, mechanitor))
                {
                    continue;
                }

                if (!MAPOverseerRelationDirectionUtility.IsActualOverseerOf(mechanitor, mech))
                {
                    Pawn_MechanitorTracker? arrivalTracker = mechanitor.mechanitor;
                    bool relationExists = mechanitor.relations != null
                        && mechanitor.relations.DirectRelationExists(
                            PawnRelationDefOf.Overseer, mech);
                    bool controlGroupExists = arrivalTracker?.GetControlGroup(mech) != null;
                    List<Pawn>? arrivalControlledPawns = arrivalTracker?.ControlledPawns;
                    bool controlledPawnsContainsSubject =
                        arrivalControlledPawns != null
                        && arrivalControlledPawns.Contains(mech);

                    Log.Warning(
                        "[MAP-机械族机械师] 机械族机械师剧本" +
                        GetArrivalLabel(arrivalMethod) +
                        "：起始机械体 " +
                        $"{mech.LabelShort}（{mech.kindDef?.defName ?? "unknown"}）" +
                        "监管关系最终校验未通过。" +
                        $" relationExists={relationExists};" +
                        $" controlGroupExists={controlGroupExists};" +
                        " controlledPawnsContainsSubject=" +
                        $"{controlledPawnsContainsSubject}（若为 false 可能仅为带宽不足，不一定是写入失败）。");
                }
            }
        }

        public static bool TryPrepareDropPodsOrStandingArrival(
            List<Thing> startingItems,
            PlayerPawnsArriveMethod arrivalMethod,
            out Pawn? mechanitor)
        {
            mechanitor = FindScenarioMechanitorInStartingItems(startingItems);
            if (mechanitor == null)
            {
                Log.Error(
                    "[MAP-机械族机械师] 机械族机械师剧本" +
                    GetArrivalLabel(arrivalMethod) +
                    "初始化失败：起始物品中未找到有效的机械意识宿主。");
                return false;
            }

            if (!RegisterAndPrepareMechanicalConsciousnessHost(mechanitor))
            {
                Log.Error(
                    "[MAP-机械族机械师] 机械族机械师剧本" +
                    GetArrivalLabel(arrivalMethod) +
                    "初始化失败：无法登记机械意识宿主，已中止投放。");
                return false;
            }

            if (!EnsureScenarioMechanitorState(mechanitor, arrivalMethod, skipAssignmentOnly: false))
            {
                Log.Error(
                    "[MAP-机械族机械师] 机械族机械师剧本" +
                    GetArrivalLabel(arrivalMethod) +
                    "初始化失败：机械师状态不完整，已中止投放。");
                return false;
            }

            AssignStartingMechsToMechanitor(mechanitor, startingItems, arrivalMethod);
            return true;
        }

        public static void TryPrepareGravshipArrival(List<Thing> startingItems)
        {
            Pawn? mechanitor = FindScenarioMechanitorInStartingItems(startingItems);
            if (mechanitor == null)
            {
                Log.Warning(
                    "[MAP-机械族机械师] 机械族机械师剧本逆重飞船初始化失败：起始物品中未找到有效的机械意识宿主。" +
                    "逆重飞船仍将继续生成，但起始机械体可能没有正确监管者。");
                return;
            }

            if (!RegisterAndPrepareMechanicalConsciousnessHost(mechanitor))
            {
                Log.Warning(
                    "[MAP-机械族机械师] 机械族机械师剧本逆重飞船初始化失败：无法登记机械意识宿主。" +
                    "逆重飞船仍将继续生成，但起始机械体可能没有正确监管者。");
                return;
            }

            if (!EnsureScenarioMechanitorState(
                    mechanitor,
                    PlayerPawnsArriveMethod.Gravship,
                    skipAssignmentOnly: true))
            {
                return;
            }

            AssignStartingMechsToMechanitor(
                mechanitor,
                startingItems,
                PlayerPawnsArriveMethod.Gravship);
        }

        private static string GetArrivalLabel(PlayerPawnsArriveMethod arrivalMethod)
        {
            return arrivalMethod switch
            {
                PlayerPawnsArriveMethod.Standing => "直接生成",
                PlayerPawnsArriveMethod.Gravship => "逆重飞船",
                _ => "运输舱",
            };
        }

        private static string GetArrivalContinuationHint(PlayerPawnsArriveMethod arrivalMethod)
        {
            return arrivalMethod == PlayerPawnsArriveMethod.Gravship
                ? "逆重飞船仍将继续生成。"
                : "投放仍将继续。";
        }
    }
}
