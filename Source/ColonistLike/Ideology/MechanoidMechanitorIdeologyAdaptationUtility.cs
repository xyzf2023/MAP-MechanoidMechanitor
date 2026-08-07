using System;
using System.Collections.Generic;
using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 文化适配公共判断与操作入口。各 Harmony 补丁应优先调用此处，避免重复判定。
    /// </summary>
    public static class MechanoidMechanitorIdeologyAdaptationUtility
    {
        private static readonly AccessTools.FieldRef<Pawn_IdeoTracker, float> CertaintyIntField =
            AccessTools.FieldRefAccess<Pawn_IdeoTracker, float>("certaintyInt");

        [ThreadStatic]
        private static int ideoMutationDepth;

        public static MechanoidMechanitorIdeologyAdaptationLevel GetEffectiveLevel()
        {
            if (!ModsConfig.IdeologyActive)
            {
                return MechanoidMechanitorIdeologyAdaptationLevel.Disabled;
            }

            if (!GameComponent_MechanoidMechanitorStoryState.IsStoryConfigurationActive)
            {
                return MechanoidMechanitorIdeologyAdaptationLevel.Disabled;
            }

            return GameComponent_MechanoidMechanitorStoryState
                .GetConfiguredIdeologyAdaptationLevel();
        }

        public static bool IsAtLeast(MechanoidMechanitorIdeologyAdaptationLevel minimum)
        {
            return GetEffectiveLevel() >= minimum;
        }

        public static bool IsRegisteredMechanoidMechanitor(Pawn? pawn)
        {
            return pawn != null
                && !pawn.Destroyed
                && GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(pawn, out _);
        }

        public static bool AllowsIdeologyMembership(Pawn? pawn)
        {
            return ModsConfig.IdeologyActive
                && IsAtLeast(MechanoidMechanitorIdeologyAdaptationLevel.Partial)
                && IsRegisteredMechanoidMechanitor(pawn);
        }

        public static bool AllowsIdeologyFullParticipation(Pawn? pawn)
        {
            return ModsConfig.IdeologyActive
                && IsAtLeast(MechanoidMechanitorIdeologyAdaptationLevel.Full)
                && IsRegisteredMechanoidMechanitor(pawn);
        }

        public static bool ShouldCountAsIdeoBeliever(Pawn? pawn)
        {
            if (!AllowsIdeologyMembership(pawn) || pawn == null)
            {
                return false;
            }

            if (pawn.Dead || pawn.Destroyed || pawn.Faction != Faction.OfPlayer)
            {
                return false;
            }

            if (pawn.Ideo == null || pawn.ideo == null)
            {
                return false;
            }

            return IsPresentInWorldPlayerScope(pawn);
        }

        public static bool CanServeAsIdeologyRoleOrRitualParticipant(Pawn? pawn)
        {
            return AllowsIdeologyFullParticipation(pawn);
        }

        public static bool IsPresentInWorldPlayerScope(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed)
            {
                return false;
            }

            // 仅计入已生成在玩家地图、玩家远行队、旅行中运输载具的成员。
            // 不因 pawn.IsWorldPawn() 为 true（仅保存在世界 Pawn 池）就计入。
            return PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive.Contains(pawn);
        }

        public static void EnsureIdeologyState(Pawn? pawn)
        {
            if (!ModsConfig.IdeologyActive || pawn == null || pawn.Destroyed)
            {
                return;
            }

            ColonistLikeSocialTrackerUtility.EnsureTrackers(pawn);

            if (!AllowsIdeologyMembership(pawn))
            {
                return;
            }

            EnsureIdeoTracker(pawn);
            EnsureDefaultIdeoIfMissing(pawn);
            ForceCertaintyFull(pawn);
            if (AllowsIdeologyFullParticipation(pawn))
            {
                pawn.abilities ??= new Pawn_AbilityTracker(pawn);
            }
        }

        public static void EnsureIdeoTracker(Pawn pawn)
        {
            if (!ModsConfig.IdeologyActive || pawn.ideo != null)
            {
                return;
            }

            ColonistLikeSocialTrackerUtility.EnsureTrackers(pawn);
            pawn.ideo = new Pawn_IdeoTracker(pawn);
        }

        public static bool TrySetIdeo(Pawn? pawn, Ideo? ideo)
        {
            if (!ModsConfig.IdeologyActive
                || pawn == null
                || pawn.Destroyed
                || ideo == null
                || !AllowsIdeologyMembership(pawn))
            {
                return false;
            }

            EnsureIdeoTracker(pawn);
            if (pawn.ideo == null)
            {
                return false;
            }

            // 计数式作用域：嵌套安全调用时内部调用不会提前关闭外层授权。
            ideoMutationDepth++;
            try
            {
                // SetIdeo 自身已完成 Notify_MemberLost/Gained、新旧意识形态的
                // RecacheColonistBelieverCount、Notify_ColonistChangedIdeo 及关系/缓存刷新，
                // 手动切换不触发 Notify_MemberGainedByConversion，也不奖励改革点数。
                pawn.ideo.SetIdeo(ideo);
            }
            finally
            {
                ideoMutationDepth--;
            }

            // SetIdeo 会把确定度设为随机初始值，这里仅需恢复为 100%。
            ForceCertaintyFull(pawn);
            return true;
        }

        public static bool IsIdeoMutationAllowed => ideoMutationDepth > 0;

        public static void ForceCertaintyFull(Pawn? pawn)
        {
            if (pawn?.ideo == null || !AllowsIdeologyMembership(pawn))
            {
                return;
            }

            CertaintyIntField(pawn.ideo) = 1f;
        }

        public static void CalibrateAllRegisteredMechanitors()
        {
            if (!ModsConfig.IdeologyActive
                || !IsAtLeast(MechanoidMechanitorIdeologyAdaptationLevel.Partial))
            {
                return;
            }

            IReadOnlyList<Pawn> mechanitors =
                GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors;
            for (int i = 0; i < mechanitors.Count; i++)
            {
                EnsureIdeologyState(mechanitors[i]);
            }
        }

        private static void EnsureDefaultIdeoIfMissing(Pawn pawn)
        {
            if (pawn.ideo == null)
            {
                return;
            }

            if (pawn.Ideo != null)
            {
                return;
            }

            Ideo? primary = Faction.OfPlayer?.ideos?.PrimaryIdeo;
            if (primary == null)
            {
                return;
            }

            TrySetIdeo(pawn, primary);
        }
    }
}
