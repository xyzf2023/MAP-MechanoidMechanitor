using RimWorld;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class MechanoidMechanitorMechHiveRelationPolicy
    {
        public static bool IsLockedMode(MechanoidMechanitorMechHiveRelationMode mode)
        {
            return mode == MechanoidMechanitorMechHiveRelationMode.PermanentNeutral
                || mode == MechanoidMechanitorMechHiveRelationMode.Ally;
        }

        public static bool TryGetInitialTarget(
            MechanoidMechanitorMechHiveRelationMode mode,
            out FactionRelationKind relationKind,
            out bool hostileOnHarmByPlayer)
        {
            switch (mode)
            {
                case MechanoidMechanitorMechHiveRelationMode.Neutral:
                    relationKind = FactionRelationKind.Neutral;
                    hostileOnHarmByPlayer = true;
                    return true;

                case MechanoidMechanitorMechHiveRelationMode.PermanentNeutral:
                    relationKind = FactionRelationKind.Neutral;
                    hostileOnHarmByPlayer = false;
                    return true;

                case MechanoidMechanitorMechHiveRelationMode.Ally:
                    relationKind = FactionRelationKind.Ally;
                    hostileOnHarmByPlayer = false;
                    return true;

                default:
                    relationKind = FactionRelationKind.Neutral;
                    hostileOnHarmByPlayer = false;
                    return false;
            }
        }

        public static bool TryGetLockedTarget(
            MechanoidMechanitorMechHiveRelationMode mode,
            out FactionRelationKind relationKind)
        {
            switch (mode)
            {
                case MechanoidMechanitorMechHiveRelationMode.PermanentNeutral:
                    relationKind = FactionRelationKind.Neutral;
                    return true;

                case MechanoidMechanitorMechHiveRelationMode.Ally:
                    relationKind = FactionRelationKind.Ally;
                    return true;

                default:
                    relationKind = FactionRelationKind.Neutral;
                    return false;
            }
        }

        public static bool TryGetEffectiveLockedTarget(
            GameComponent_MechanoidMechanitorStoryState storyState,
            out FactionRelationKind relationKind,
            out bool hostileOnHarmByPlayer)
        {
            relationKind = FactionRelationKind.Neutral;
            hostileOnHarmByPlayer = false;
            if (storyState == null)
            {
                return false;
            }

            if (storyState.CerebrexFacilityTrespassed
                || storyState.PurgeDirectiveFinalPenaltyTriggered)
            {
                relationKind = FactionRelationKind.Hostile;
                hostileOnHarmByPlayer = false;
                return true;
            }

            if (!storyState.TryGetMechHiveRelationMode(
                    out MechanoidMechanitorMechHiveRelationMode mode))
            {
                return false;
            }

            if (!TryGetLockedTarget(mode, out relationKind))
            {
                return false;
            }

            hostileOnHarmByPlayer = false;
            return true;
        }

        public static bool TryGetEffectiveInitialTarget(
            GameComponent_MechanoidMechanitorStoryState storyState,
            out FactionRelationKind relationKind,
            out bool hostileOnHarmByPlayer)
        {
            relationKind = FactionRelationKind.Neutral;
            hostileOnHarmByPlayer = false;
            if (storyState == null)
            {
                return false;
            }

            if (storyState.CerebrexFacilityTrespassed
                || storyState.PurgeDirectiveFinalPenaltyTriggered)
            {
                relationKind = FactionRelationKind.Hostile;
                hostileOnHarmByPlayer = false;
                return true;
            }

            if (!storyState.TryGetMechHiveRelationMode(
                    out MechanoidMechanitorMechHiveRelationMode mode))
            {
                return false;
            }

            return TryGetInitialTarget(mode, out relationKind, out hostileOnHarmByPlayer);
        }
    }
}
