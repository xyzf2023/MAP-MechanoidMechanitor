using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械族机械师接入原版 Royalty 爵位系统的统一资格与基础设施入口。
    /// 不修改 Pawn.IsColonist / RaceProps.Humanlike，只在爵位相关调用点局部放行。
    /// </summary>
    public static class MechanoidMechanitorRoyaltyUtility
    {
        public static bool IsRoyaltyEligibleMechanitor(Pawn? pawn)
        {
            return ModsConfig.RoyaltyActive
                && pawn != null
                && !pawn.Dead
                && !pawn.Destroyed
                && pawn.RaceProps.IsMechanoid
                && pawn.Faction != null
                && pawn.Faction.IsPlayer
                && MechanoidMechanitorCapabilityUtility.HasCapability(
                    pawn,
                    MechanoidMechanitorCapability.Royalty);
        }

        public static bool IsColonistOrSupportedMechanicalRoyal(Pawn pawn)
        {
            return pawn.IsColonist || IsRoyaltyEligibleMechanitor(pawn);
        }

        public static bool CanMeetRoyalApparelRequirements(Pawn? pawn)
        {
            return pawn != null
                && pawn.apparel != null
                && HumanApparelUtility.CanUseWearFloatMenu(pawn);
        }

        public static void EnsureRoyaltyInfrastructure(Pawn? pawn)
        {
            if (!IsRoyaltyEligibleMechanitor(pawn))
            {
                return;
            }

            pawn!.story ??= new Pawn_StoryTracker(pawn);
            pawn.story.traits ??= new TraitSet(pawn);
            pawn.abilities ??= new Pawn_AbilityTracker(pawn);

            bool createdRoyaltyTracker = pawn.royalty == null;
            pawn.royalty ??= new Pawn_RoyaltyTracker(pawn);

            if (createdRoyaltyTracker)
            {
                // 机械贵族保留王座 / 王座厅语义；卧室需求由专门补丁关闭。
                pawn.royalty.allowRoomRequirements = true;
            }

            // 只有真正具备本 MOD 人类服装能力的机械体才承担贵族服装要求。
            pawn.royalty.allowApparelRequirements =
                CanMeetRoyalApparelRequirements(pawn);

            pawn.royalty.UpdateAvailableAbilities();
            pawn.abilities.Notify_TemporaryAbilitiesChanged();
        }

        public static void SynchronizeAllRegisteredMechanitors()
        {
            if (!ModsConfig.RoyaltyActive)
            {
                return;
            }

            IReadOnlyList<Pawn> mechanitors =
                GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors;
            for (int i = 0; i < mechanitors.Count; i++)
            {
                EnsureRoyaltyInfrastructure(mechanitors[i]);
            }
        }

        public static IEnumerable<Pawn> EligibleMechanitorsFrom(
            IEnumerable<Pawn>? pawns)
        {
            if (pawns == null)
            {
                yield break;
            }

            foreach (Pawn pawn in pawns)
            {
                if (IsRoyaltyEligibleMechanitor(pawn))
                {
                    yield return pawn;
                }
            }
        }
    }
}
