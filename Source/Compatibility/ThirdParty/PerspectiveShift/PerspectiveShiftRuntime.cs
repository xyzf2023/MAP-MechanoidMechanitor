using System;
using System.Reflection;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.PerspectiveShift
{
    internal static class PerspectiveShiftRuntime
    {
        internal static Type AvatarType = null!;
        internal static Type StateType = null!;
        internal static FieldInfo AvatarPawn = null!;
        private static FieldInfo? currentAvatar;

        internal static void Resolve(ModContentPack mod)
        {
            Type avatarType = PerspectiveShiftCompatibilityModule.ResolveType(mod, "Avatar");
            Type stateType = PerspectiveShiftCompatibilityModule.ResolveType(mod, "State");
            FieldInfo avatarPawn = PerspectiveShiftCompatibilityModule.Field(avatarType, "pawn", typeof(Pawn));
            FieldInfo avatar = PerspectiveShiftCompatibilityModule.Field(stateType, "Avatar", avatarType, true);
            AvatarType = avatarType;
            StateType = stateType;
            AvatarPawn = avatarPawn;
            currentAvatar = avatar;
        }

        internal static Pawn? GetPawn(object? avatar) => avatar == null ? null : AvatarPawn.GetValue(avatar) as Pawn;
        internal static object? CurrentAvatar => currentAvatar?.GetValue(null);
        internal static Pawn? CurrentPawn => GetPawn(CurrentAvatar);
        internal static bool CanQuery => Scribe.mode == LoadSaveMode.Inactive
            && !MechanoidMechanitorPostLoadSafetyCoordinator.LoadInProgress;

        internal static bool IsMechanitor(Pawn? pawn) => pawn?.RaceProps?.IsMechanoid == true
            && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn);

        internal static bool IsPlayerMechanitor(Pawn? pawn) => IsMechanitor(pawn)
            && GameComponent_MechanoidMechanitorRegistry.IsPawnAliveAndInitialized(pawn)
            && pawn!.Faction == Faction.OfPlayer && !pawn.IsPrisoner && !pawn.IsSlave && pawn.HostFaction == null;

        internal static bool IsControlled(Pawn? pawn) => pawn != null
            && ReferenceEquals(CurrentPawn, pawn) && IsMechanitor(pawn);

        internal static bool HasControlInfrastructure(Pawn pawn) => pawn.jobs != null && pawn.pather != null
            && pawn.stances != null && pawn.drafter != null && pawn.needs != null && pawn.thinker != null
            && pawn.equipment != null && pawn.story != null && pawn.skills != null && pawn.playerSettings != null;

        internal static bool IsUnderAIControl(Pawn pawn) => pawn.GetLord() != null || pawn.mindState?.duty != null;

        // 查询不补建 Tracker、不改变身份、模式、能量或存档数据。
        internal static bool CanOperate(Pawn pawn) => CanQuery && IsPlayerMechanitor(pawn)
            && HasControlInfrastructure(pawn) && !pawn.Downed && !pawn.InMentalState && !pawn.Deathresting
            && pawn.health.capacities?.CanBeAwake == true && pawn.needs.energy?.IsLowEnergySelfShutdown != true
            && MechTransformationUtility.IsInPawnForm(pawn)
            && !MechanicalFlightEmergencyUtility.IsEmergencySequence(pawn);

        internal static bool IsEnergyJob(Pawn? pawn) => pawn?.CurJobDef == JobDefOf.MechCharge
            || pawn?.CurJobDef == JobDefOf.SelfShutdown;
    }
}
