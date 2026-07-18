using RimWorld;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class MechanoidMechanitorOrdinaryFactionRelationPolicy
    {
        public static bool IsLockedOption(MechanoidMechanitorFactionRelationOption option)
        {
            return option == MechanoidMechanitorFactionRelationOption.PermanentHostile
                || option == MechanoidMechanitorFactionRelationOption.PermanentNeutral
                || option == MechanoidMechanitorFactionRelationOption.PermanentAlly;
        }

        public static bool TryGetInitialRelationTarget(
            MechanoidMechanitorFactionRelationOption option,
            out int goodwill,
            out FactionRelationKind relationKind)
        {
            switch (option)
            {
                case MechanoidMechanitorFactionRelationOption.Hostile:
                case MechanoidMechanitorFactionRelationOption.PermanentHostile:
                    goodwill = -100;
                    relationKind = FactionRelationKind.Hostile;
                    return true;

                case MechanoidMechanitorFactionRelationOption.PermanentNeutral:
                    goodwill = 0;
                    relationKind = FactionRelationKind.Neutral;
                    return true;

                case MechanoidMechanitorFactionRelationOption.Ally:
                case MechanoidMechanitorFactionRelationOption.PermanentAlly:
                    goodwill = 100;
                    relationKind = FactionRelationKind.Ally;
                    return true;

                default:
                    goodwill = 0;
                    relationKind = FactionRelationKind.Neutral;
                    return false;
            }
        }

        public static bool TryGetLockedRelationTarget(
            MechanoidMechanitorFactionRelationOption option,
            out int goodwill,
            out FactionRelationKind relationKind)
        {
            if (!IsLockedOption(option))
            {
                goodwill = 0;
                relationKind = FactionRelationKind.Neutral;
                return false;
            }

            return TryGetInitialRelationTarget(option, out goodwill, out relationKind);
        }

        public static MechanoidMechanitorFactionRelationOption ResolveOptionFromGlobalMode(
            MechanoidMechanitorOrdinaryFactionRelationsMode mode)
        {
            switch (mode)
            {
                case MechanoidMechanitorOrdinaryFactionRelationsMode.AllHostile:
                    return MechanoidMechanitorFactionRelationOption.Hostile;

                case MechanoidMechanitorOrdinaryFactionRelationsMode.AllPermanentHostile:
                    return MechanoidMechanitorFactionRelationOption.PermanentHostile;

                case MechanoidMechanitorOrdinaryFactionRelationsMode.AllPermanentNeutral:
                    return MechanoidMechanitorFactionRelationOption.PermanentNeutral;

                case MechanoidMechanitorOrdinaryFactionRelationsMode.AllAlly:
                    return MechanoidMechanitorFactionRelationOption.Ally;

                case MechanoidMechanitorOrdinaryFactionRelationsMode.AllPermanentAlly:
                    return MechanoidMechanitorFactionRelationOption.PermanentAlly;

                default:
                    return MechanoidMechanitorFactionRelationOption.Default;
            }
        }
    }
}
