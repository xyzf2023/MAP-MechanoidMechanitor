using System.Collections.Generic;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 文化信徒/主流文化统计共用的有效成员枚举，保证两套统计同源。
    /// </summary>
    public static class MechanoidMechanitorIdeologyMemberUtility
    {
        private static readonly List<Pawn> TmpMembers = new List<Pawn>();
        private static readonly HashSet<Pawn> TmpSeen = new HashSet<Pawn>();

        public static List<Pawn> GetEffectiveIdeologyMembers(
            bool excludeCryptosleep = false,
            bool excludeQuestLodgers = false)
        {
            TmpMembers.Clear();
            TmpSeen.Clear();

            List<Pawn> freeColonists = excludeCryptosleep
                ? PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists_NoCryptosleep
                : PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists;

            for (int i = 0; i < freeColonists.Count; i++)
            {
                Pawn pawn = freeColonists[i];
                if (pawn == null || !TmpSeen.Add(pawn))
                {
                    continue;
                }

                if (excludeQuestLodgers && pawn.IsQuestLodger())
                {
                    continue;
                }

                TmpMembers.Add(pawn);
            }

            if (!MechanoidMechanitorIdeologyAdaptationUtility.IsAtLeast(
                    MechanoidMechanitorIdeologyAdaptationLevel.Partial))
            {
                return TmpMembers;
            }

            IReadOnlyList<Pawn> mechanitors =
                GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors;
            for (int i = 0; i < mechanitors.Count; i++)
            {
                Pawn pawn = mechanitors[i];
                if (!MechanoidMechanitorIdeologyAdaptationUtility.ShouldCountAsIdeoBeliever(pawn))
                {
                    continue;
                }

                if (excludeCryptosleep && pawn.Suspended)
                {
                    continue;
                }

                if (excludeQuestLodgers && pawn.IsQuestLodger())
                {
                    continue;
                }

                if (!TmpSeen.Add(pawn))
                {
                    continue;
                }

                TmpMembers.Add(pawn);
            }

            return TmpMembers;
        }
    }
}
