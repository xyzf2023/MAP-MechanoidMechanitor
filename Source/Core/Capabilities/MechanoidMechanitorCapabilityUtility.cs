using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class MechanoidMechanitorCapabilityUtility
    {
        public static bool HasCapability(
            Pawn? pawn,
            MechanoidMechanitorCapability capability)
        {
            if (pawn == null || capability == MechanoidMechanitorCapability.None)
            {
                return false;
            }

            return (GetCapabilities(pawn) & capability) == capability;
        }

        public static MechanoidMechanitorCapability GetCapabilities(Pawn? pawn)
        {
            if (pawn == null)
            {
                return MechanoidMechanitorCapability.None;
            }

            MechanoidMechanitorCapability capabilities = MechanoidMechanitorCapability.None;
            AddCapabilitiesFromRealComponents(pawn, ref capabilities);
            AddCapabilitiesFromMechanitorIdentity(pawn, ref capabilities);
            AddCapabilitiesFromDataProcessingAllocation(pawn, ref capabilities);
            return capabilities;
        }

        private static void AddCapabilitiesFromRealComponents(
            Pawn pawn,
            ref MechanoidMechanitorCapability capabilities)
        {
            if (CompMAPMechanitorTravelNode.TryGetTravelNodeComp(
                    pawn,
                    out CompMAPMechanitorTravelNode? travelComp)
                && travelComp?.TravelProps != null)
            {
                CompProperties_MAPMechanitorTravelNode props = travelComp.TravelProps;
                if (props.canLeadCaravan)
                {
                    capabilities |= MechanoidMechanitorCapability.TravelLeadCaravan;
                }

                if (props.canCollectCaravanItems)
                {
                    capabilities |= MechanoidMechanitorCapability.TravelCollectItems;
                }

                if (props.refreshTrackersOnTransporterArrival)
                {
                    capabilities |= MechanoidMechanitorCapability.TravelRefreshTrackers;
                }
            }

            if (pawn.GetComp<CompFreeColonistEquivalentUser>() != null)
            {
                capabilities |= MechanoidMechanitorCapability.FreeColonistEquivalent;
            }

            CompColonistLikeFloatMenuUser? floatMenuComp =
                pawn.GetComp<CompColonistLikeFloatMenuUser>();
            if (floatMenuComp != null && floatMenuComp.Props.allowColonistLikeFloatMenu)
            {
                capabilities |= MechanoidMechanitorCapability.ColonistLikeFloatMenu;
            }

            if (pawn.GetComp<CompHumanWeaponUser>() != null)
            {
                capabilities |= MechanoidMechanitorCapability.HumanWeapons;
            }

            CompGravshipPilotUser? gravshipComp = pawn.GetComp<CompGravshipPilotUser>();
            if (gravshipComp != null && gravshipComp.Props.allowGravshipPilotConsole)
            {
                capabilities |= MechanoidMechanitorCapability.GravshipPilot;
            }

            CompWorkTabVisibleUser? workTabComp = pawn.GetComp<CompWorkTabVisibleUser>();
            if (workTabComp != null && workTabComp.Props.showInWorkTab)
            {
                capabilities |= MechanoidMechanitorCapability.WorkTab;
            }

            CompPsychicRitualParticipantUser? psychicComp =
                pawn.GetComp<CompPsychicRitualParticipantUser>();
            if (psychicComp != null && psychicComp.Props.allowPsychicRituals)
            {
                capabilities |= MechanoidMechanitorCapability.PsychicRituals;
            }

            if (CompMechanoidMechanitorSelfWorkModeUser.GetFor(pawn) != null)
            {
                capabilities |= MechanoidMechanitorCapability.SelfWorkMode;
            }

            if (pawn.GetComp<CompColonistLikeSocialTabUser>() != null)
            {
                capabilities |= MechanoidMechanitorCapability.ColonistLikeSocialTab;
            }

            if (pawn.GetComp<CompSyntheticCompanionUser>() != null)
            {
                capabilities |= MechanoidMechanitorCapability.SyntheticSpouseInteraction
                    | MechanoidMechanitorCapability.SyntheticPregnancy;
            }
        }

        private static void AddCapabilitiesFromMechanitorIdentity(
            Pawn pawn,
            ref MechanoidMechanitorCapability capabilities)
        {
            if (!GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(
                    pawn,
                    out _))
            {
                return;
            }

            capabilities |= MechanoidMechanitorCapability.ImplantInstallation
                | MechanoidMechanitorCapability.ShuttlePilot;

            if (!GameComponent_MechanoidMechanitorRegistry.TryGetAcquiredMechanitorRecord(
                    pawn,
                    out _))
            {
                return;
            }

            capabilities |= MechanoidMechanitorCapability.HumanWeapons
                | MechanoidMechanitorCapability.ColonistLikeFloatMenu
                | MechanoidMechanitorCapability.GravshipPilot
                | MechanoidMechanitorCapability.WorkTab
                | MechanoidMechanitorCapability.PsychicRituals
                | MechanoidMechanitorCapability.SelfWorkMode
                | MechanoidMechanitorCapability.TravelLeadCaravan
                | MechanoidMechanitorCapability.TravelCollectItems
                | MechanoidMechanitorCapability.TravelRefreshTrackers;
        }

        private static void AddCapabilitiesFromDataProcessingAllocation(
            Pawn pawn,
            ref MechanoidMechanitorCapability capabilities)
        {
            if (DataProcessingAllocationUtility.HasVirtualTravelNode(pawn))
            {
                capabilities |= MechanoidMechanitorCapability.TravelLeadCaravan
                    | MechanoidMechanitorCapability.TravelCollectItems
                    | MechanoidMechanitorCapability.TravelRefreshTrackers;
            }

            if (DataProcessingAllocationUtility.HasShuttlePilotAllocation(pawn))
            {
                capabilities |= MechanoidMechanitorCapability.ShuttlePilot;
            }
        }
    }
}
