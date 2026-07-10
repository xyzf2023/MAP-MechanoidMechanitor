using System;

namespace MAP_MechanoidMechanitor
{
    [Flags]
    public enum MechanoidMechanitorCapability
    {
        None = 0,

        TravelLeadCaravan = 1 << 0,
        TravelCollectItems = 1 << 1,
        TravelRefreshTrackers = 1 << 2,

        FreeColonistEquivalent = 1 << 3,
        ColonistLikeFloatMenu = 1 << 4,
        HumanWeapons = 1 << 5,
        GravshipPilot = 1 << 6,
        WorkTab = 1 << 7,
        PsychicRituals = 1 << 8,
        SelfWorkMode = 1 << 9
    }
}
