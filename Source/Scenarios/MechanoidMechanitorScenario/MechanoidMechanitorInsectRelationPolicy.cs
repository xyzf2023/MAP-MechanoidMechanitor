using RimWorld;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class MechanoidMechanitorInsectRelationPolicy
    {
        public static bool IsLockedMode(
            MechanoidMechanitorInsectRelationMode mode)
        {
            return mode == MechanoidMechanitorInsectRelationMode.PermanentNeutral
                || mode == MechanoidMechanitorInsectRelationMode.Ally
                || mode == MechanoidMechanitorInsectRelationMode.Pursuit;
        }

        public static bool TryGetInitialTarget(
            MechanoidMechanitorInsectRelationMode mode,
            out FactionRelationKind relationKind,
            out bool hostileOnHarmByPlayer)
        {
            switch (mode)
            {
                case MechanoidMechanitorInsectRelationMode.PermanentNeutral:
                    relationKind = FactionRelationKind.Neutral;
                    hostileOnHarmByPlayer = false;
                    return true;

                case MechanoidMechanitorInsectRelationMode.Ally:
                    relationKind = FactionRelationKind.Ally;
                    hostileOnHarmByPlayer = false;
                    return true;

                case MechanoidMechanitorInsectRelationMode.Pursuit:
                    relationKind = FactionRelationKind.Hostile;
                    hostileOnHarmByPlayer = false;
                    return true;

                default:
                    relationKind = FactionRelationKind.Neutral;
                    hostileOnHarmByPlayer = false;
                    return false;
            }
        }

        public static bool TryGetLockedTarget(
            MechanoidMechanitorInsectRelationMode mode,
            out FactionRelationKind relationKind)
        {
            switch (mode)
            {
                case MechanoidMechanitorInsectRelationMode.PermanentNeutral:
                    relationKind = FactionRelationKind.Neutral;
                    return true;

                case MechanoidMechanitorInsectRelationMode.Ally:
                    relationKind = FactionRelationKind.Ally;
                    return true;

                case MechanoidMechanitorInsectRelationMode.Pursuit:
                    relationKind = FactionRelationKind.Hostile;
                    return true;

                default:
                    relationKind = FactionRelationKind.Neutral;
                    return false;
            }
        }

        public static bool TryGetEffectiveInitialTarget(
            GameComponent_MechanoidMechanitorStoryState storyState,
            out FactionRelationKind relationKind,
            out bool hostileOnHarmByPlayer)
        {
            relationKind = FactionRelationKind.Neutral;
            hostileOnHarmByPlayer = false;

            if (storyState == null
                || !storyState.TryGetInsectRelationMode(
                    out MechanoidMechanitorInsectRelationMode mode))
            {
                return false;
            }

            return TryGetInitialTarget(
                mode,
                out relationKind,
                out hostileOnHarmByPlayer);
        }

        public static bool TryGetEffectiveLockedTarget(
            GameComponent_MechanoidMechanitorStoryState storyState,
            out FactionRelationKind relationKind,
            out bool hostileOnHarmByPlayer)
        {
            relationKind = FactionRelationKind.Neutral;
            hostileOnHarmByPlayer = false;

            if (storyState == null
                || !storyState.TryGetInsectRelationMode(
                    out MechanoidMechanitorInsectRelationMode mode))
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
    }
}
