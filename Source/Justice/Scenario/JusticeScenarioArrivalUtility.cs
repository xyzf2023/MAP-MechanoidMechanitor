using System.Collections.Generic;
using MAP_MechanoidMechanitor;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class JusticeScenarioArrivalUtility
    {
        public static Pawn? FindScenarioMechanitorInStartingItems(List<Thing> startingItems)
        {
            Pawn? registered = JusticeScenarioUtility.MechanicalConsciousnessHost;
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
                        "[MAP-机械族机械师] 剧本" +
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

            return !MAPOverseerlessNodeUtility.IsOverseerlessNodeSubject(mech);
        }

        public static void AssignStartingMechsToMechanitor(
            Pawn mechanitor,
            IEnumerable<Thing> items,
            PlayerPawnsArriveMethod arrivalMethod)
        {
            mechanitor.relations ??= new Pawn_RelationsTracker(mechanitor);

            foreach (Thing item in items)
            {
                if (item is not Pawn mech || !IsOverseeCandidate(mech, mechanitor))
                {
                    continue;
                }

                Pawn? existingOverseer = mech.GetOverseer();
                if (existingOverseer == mechanitor)
                {
                    continue;
                }

                if (!mechanitor.mechanitor.CanOverseeSubject(mech))
                {
                    Log.Warning(
                        "[MAP-机械族机械师] 剧本" +
                        GetArrivalLabel(arrivalMethod) +
                        "：无法为 " +
                        $"{mech.LabelShort}（{mech.kindDef?.defName ?? "unknown"}）" +
                        "分配监管者：带宽不足或主体不兼容。");
                    continue;
                }

                if (existingOverseer?.relations != null)
                {
                    existingOverseer.relations.TryRemoveDirectRelation(
                        PawnRelationDefOf.Overseer,
                        mech);
                }

                mech.relations ??= new Pawn_RelationsTracker(mech);
                mechanitor.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mech);
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
                    "[MAP-机械族机械师] 剧本" +
                    GetArrivalLabel(arrivalMethod) +
                    "初始化失败：起始物品中未找到有效的机械意识宿主。");
                return false;
            }

            if (!RegisterAndPrepareMechanicalConsciousnessHost(mechanitor))
            {
                Log.Error(
                    "[MAP-机械族机械师] 剧本" +
                    GetArrivalLabel(arrivalMethod) +
                    "初始化失败：无法登记机械意识宿主，已中止投放。");
                return false;
            }

            if (!EnsureScenarioMechanitorState(mechanitor, arrivalMethod, skipAssignmentOnly: false))
            {
                Log.Error(
                    "[MAP-机械族机械师] 剧本" +
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
                    "[MAP-机械族机械师] 剧本逆重飞船初始化失败：起始物品中未找到有效的机械意识宿主。" +
                    "逆重飞船仍将继续生成，但起始机械体可能没有正确监管者。");
                return;
            }

            if (!RegisterAndPrepareMechanicalConsciousnessHost(mechanitor))
            {
                Log.Warning(
                    "[MAP-机械族机械师] 剧本逆重飞船初始化失败：无法登记机械意识宿主。" +
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
