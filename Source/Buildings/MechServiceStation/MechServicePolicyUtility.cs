using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor
{
    public enum MechServiceStationMode
    {
        AsNeeded, ChargeFirst, RepairFirst, AssignedOnly, MechanitorOnly, AssignedPriority
    }

    public static class MechServiceNeedUtility
    {
        public static bool CanCharge(Pawn pawn) => pawn.needs?.energy != null;

        public static bool NeedsCharge(Pawn pawn) =>
            pawn.needs?.energy is Need_MechEnergy energy && energy.CurLevel < energy.MaxLevel;

        public static bool NeedsAutomaticCharge(Pawn pawn)
        {
            if (pawn.needs?.energy is not Need_MechEnergy energy) return false;
            if (MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn)
                && MechanoidMechanitorTimetableUtility.GetCurrentIntent(pawn) == MechanoidMechanitorScheduleIntent.Recharge)
                return energy.CurLevel < MechanoidMechanitorRechargeUtility.GetStopRechargeEnergy(pawn);
            return energy.CurLevel + 0.1f < JobGiver_GetEnergy.GetMinAutorechargeThreshold(pawn);
        }

        public static bool NeedsRepair(Pawn pawn) =>
            pawn.health?.hediffSet != null && pawn.equipment != null
            && pawn.TryGetComp<CompMechRepairable>() != null && MechRepairUtility.CanRepair(pawn);

        public static bool HasAnyNeed(Pawn pawn) => NeedsCharge(pawn) || NeedsRepair(pawn);
    }

    public static class MechServicePolicyUtility
    {
        public static bool IsLegalPawn(Pawn pawn) => ModsConfig.BiotechActive && pawn != null
            && pawn.Spawned && !pawn.Destroyed && !pawn.Dead && !pawn.Downed
            && pawn.Faction == Faction.OfPlayer && pawn.RaceProps.IsMechanoid
            && !pawn.InMentalState && pawn.jobs != null && pawn.health?.hediffSet != null
            && (MechServiceNeedUtility.CanCharge(pawn) || pawn.TryGetComp<CompMechRepairable>() != null);

        public static bool IsAllowed(CompMechServiceStation station, Pawn pawn) =>
            IsLegalPawn(pawn) && station.parent.Spawned && pawn.Map == station.parent.Map
            && station.parent.Faction == Faction.OfPlayer
            && (station.Mode != MechServiceStationMode.AssignedOnly || station.AssignedPawn == pawn)
            && (station.Mode != MechServiceStationMode.MechanitorOnly
                || MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn));

        public static bool CanAutoSeek(Pawn pawn)
        {
            if (!IsLegalPawn(pawn) || pawn.Drafted || pawn.CurJob?.playerForced == true
                || pawn.GetLord() != null || pawn.mindState?.duty != null)
                return false;
            if (!pawn.IsColonyMechPlayerControlled
                && !MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
                return false;

            MechWorkModeDef? mode;
            if (MechanoidMechanitorSelfWorkModeUtility.TryGetCurrentMode(pawn, out MechWorkModeDef? ownMode))
                mode = MechanoidMechanitorSelfWorkModeUtility.GetMappedVanillaWorkMode(ownMode);
            else
            {
                MechanitorControlGroup? group = pawn.GetMechControlGroup();
                mode = group?.WorkMode;
                if (group != null && MechanoidMechanitorWorkModeUtility.IsMechanoidMechanitorControlGroup(group)
                    && MechanoidMechanitorWorkModeUtility.SatisfiesVanillaWorkMode(mode, MechWorkModeDefOf.Work))
                    return true;
            }
            return mode == null || mode == MechWorkModeDefOf.Work || mode == MechWorkModeDefOf.Recharge;
        }

        public static bool HasEntryNeed(CompMechServiceStation station, Pawn pawn, bool forced)
        {
            if (forced) return MechServiceNeedUtility.HasAnyNeed(pawn);
            switch (station.Mode)
            {
                case MechServiceStationMode.ChargeFirst:
                    return MechServiceNeedUtility.NeedsAutomaticCharge(pawn);
                case MechServiceStationMode.RepairFirst:
                    return MechServiceNeedUtility.NeedsRepair(pawn);
                default:
                    return MechServiceNeedUtility.NeedsAutomaticCharge(pawn)
                        || MechServiceNeedUtility.NeedsRepair(pawn);
            }
        }

        public static int Priority(CompMechServiceStation station, Pawn pawn, bool forced)
        {
            if (forced) return 2;
            if (station.Mode == MechServiceStationMode.AssignedPriority && station.AssignedPawn == pawn)
                return 1;
            if (!station.MechanitorPriority || !MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
                return 0;
            bool qualifies = station.Mode == MechServiceStationMode.RepairFirst
                ? MechServiceNeedUtility.NeedsRepair(pawn)
                : station.Mode == MechServiceStationMode.ChargeFirst
                    ? MechServiceNeedUtility.NeedsCharge(pawn) : MechServiceNeedUtility.HasAnyNeed(pawn);
            return qualifies ? 1 : 0;
        }

        public static bool IsComplete(Pawn pawn, MechServiceStationMode mode, bool forced)
        {
            if (!forced && mode == MechServiceStationMode.ChargeFirst)
                return !MechServiceNeedUtility.NeedsCharge(pawn);
            if (!forced && mode == MechServiceStationMode.RepairFirst)
                return !MechServiceNeedUtility.NeedsRepair(pawn);
            return !MechServiceNeedUtility.HasAnyNeed(pawn);
        }

        public static string Label(MechServiceStationMode mode)
        {
            switch (mode)
            {
                case MechServiceStationMode.AssignedOnly: return "指定机械族";
                case MechServiceStationMode.MechanitorOnly: return "机械族机械师专用";
                case MechServiceStationMode.AssignedPriority: return "指定机械族优先";
                case MechServiceStationMode.ChargeFirst: return "优先充电";
                case MechServiceStationMode.RepairFirst: return "优先维修";
                default: return "按需使用";
            }
        }
    }
}
