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

    }

    public static class MechServicePolicyUtility
    {
        public static bool SupportsMechanitorPriority(MechServiceStationMode mode) =>
            mode == MechServiceStationMode.AsNeeded || mode == MechServiceStationMode.ChargeFirst
            || mode == MechServiceStationMode.RepairFirst || mode == MechServiceStationMode.AssignedPriority;

        public static bool IsEligiblePawn(Pawn pawn) => ModsConfig.BiotechActive && pawn != null
            && !pawn.Destroyed && !pawn.Dead
            && pawn.Faction == Faction.OfPlayer && pawn.RaceProps.IsMechanoid
            && !pawn.InMentalState && pawn.jobs != null && pawn.health?.hediffSet != null
            && (MechServiceNeedUtility.CanCharge(pawn) || pawn.TryGetComp<CompMechRepairable>() != null);

        public static bool IsServiceJob(Pawn pawn) => pawn?.CurJob != null
            && (pawn.CurJobDef == MechServiceStationDefOf.MAP_Job_UseMechServiceStation
                || pawn.CurJobDef == MechServiceStationDefOf.MAP_Job_ReceiveMechService);

        public static bool IsLegalPawn(Pawn pawn) => IsEligiblePawn(pawn) && pawn.Spawned
            && (!pawn.Downed || pawn.CurJobDef == MechServiceStationDefOf.MAP_Job_ReceiveMechService);

        // 自行使用和搬运送达共用同一套身份/指定对象策略。
        public static bool MatchesUsagePolicy(CompMechServiceStation station, Pawn pawn) =>
            station.parent.Faction == Faction.OfPlayer
            && (station.Mode != MechServiceStationMode.AssignedOnly || station.AssignedPawn == pawn)
            && (station.Mode != MechServiceStationMode.MechanitorOnly
                || MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn));

        public static bool IsAllowed(CompMechServiceStation station, Pawn pawn) =>
            IsLegalPawn(pawn) && station.parent.Spawned && pawn.Map == station.parent.Map
            && MatchesUsagePolicy(station, pawn);

        public static bool CanAutoSeek(Pawn pawn)
        {
            if (!IsLegalPawn(pawn) || pawn.Drafted || MechServiceStandbyUtility.IsWaiting(pawn)
                || pawn.CurJob?.playerForced == true
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
            if (forced) return station.HasEnabledNeed(pawn) || (!station.HasServiceFunctions && station.StandbyAfterService);
            if (!station.HasServiceFunctions) return false;
            switch (station.Mode)
            {
                case MechServiceStationMode.ChargeFirst when station.ChargingEnabled:
                    return MechServiceNeedUtility.NeedsAutomaticCharge(pawn);
                case MechServiceStationMode.RepairFirst when station.RepairEnabled:
                    return station.NeedsRepair(pawn);
                default:
                    return (station.ChargingEnabled && MechServiceNeedUtility.NeedsAutomaticCharge(pawn))
                        || station.NeedsRepair(pawn);
            }
        }

        public static int Priority(CompMechServiceStation station, Pawn pawn, bool forced)
        {
            if (forced) return 2;
            if (station.Mode == MechServiceStationMode.AssignedPriority && station.AssignedPawn == pawn)
                return 1;
            if (!SupportsMechanitorPriority(station.Mode) || !station.MechanitorPriority
                || !MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn))
                return 0;
            bool qualifies = station.Mode == MechServiceStationMode.RepairFirst && station.RepairEnabled
                ? station.NeedsRepair(pawn)
                : station.Mode == MechServiceStationMode.ChargeFirst && station.ChargingEnabled
                    ? station.NeedsCharge(pawn) : station.HasEnabledNeed(pawn);
            return qualifies ? 1 : 0;
        }

        public static string Label(MechServiceStationMode mode)
        {
            switch (mode)
            {
                case MechServiceStationMode.AssignedOnly: return "指定机械族专用";
                case MechServiceStationMode.MechanitorOnly: return "机械族机械师专用";
                case MechServiceStationMode.AssignedPriority: return "指定机械族优先";
                case MechServiceStationMode.ChargeFirst: return "优先充电";
                case MechServiceStationMode.RepairFirst: return "优先维修";
                default: return "按需使用";
            }
        }
    }
}
