using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [Flags]
    public enum MechanoidMechanitorIdentity
    {
        None = 0,
        Native = 1,
        Acquired = 2,
        MechanicalConsciousnessHost = 4,
        ScenarioProtagonist = 8
    }

    public static class MechanoidMechanitorRoleUtility
    {
        public const int AcquiredBaseExtraBandwidth = 20;
        public const int AcquiredExtraControlGroups = 5;
        public const int AcquiredMaxIntrinsicBandwidth = 500;

        private const string AcquiredIdentityDefName = "MAP_AcquiredMechanoidMechanitor";
        private const string MechRecodeAbilityDefName = "MAP_Ability_MechRecode";
        private const string MechReconstructionAbilityDefName = "MAP_Ability_MechReconstruction";

        private static readonly HashSet<string> roleWorkTypeDefNames = new HashSet<string>
        {
            "Firefighter",
            "Patient",
            "Doctor",
            "PatientBedRest",
            "BasicWorker",
            "Warden",
            "Handling",
            "Cooking",
            "Hunting",
            "Construction",
            "Growing",
            "Mining",
            "PlantCutting",
            "Smithing",
            "Tailoring",
            "Art",
            "Crafting",
            "Hauling",
            "Cleaning",
            "Research",
            "Childcare",
            "DarkStudy",
            "Fishing"
        };

        private static HediffDef? acquiredIdentityDef;
        private static AbilityDef? mechRecodeAbilityDef;
        private static AbilityDef? mechReconstructionAbilityDef;
        private static List<WorkTypeDef>? cachedRoleWorkTypes;

        public static bool IsNativeMechanoidMechanitor(Pawn? pawn)
        {
            return pawn?.GetComp<CompNativeMechanoidMechanitor>() != null;
        }

        public static bool IsAcquiredMechanoidMechanitor(Pawn? pawn)
        {
            return TryGetAcquiredIdentityComp(pawn, out _);
        }

        public static bool IsMechanoidMechanitor(Pawn? pawn)
        {
            return IsNativeMechanoidMechanitor(pawn)
                || IsAcquiredMechanoidMechanitor(pawn);
        }

        public static bool IsMechanicalConsciousnessHost(Pawn? pawn)
        {
            return Scenarios.GameComponent_MechanoidMechanitorRegistry.IsMechanicalConsciousnessHost(pawn);
        }

        public static bool IsScenarioProtagonist(Pawn? pawn)
        {
            return Scenarios.GameComponent_MechanoidMechanitorRegistry.IsScenarioProtagonist(pawn);
        }

        public static MechanoidMechanitorIdentity GetIdentity(Pawn? pawn)
        {
            if (pawn == null)
            {
                return MechanoidMechanitorIdentity.None;
            }

            MechanoidMechanitorIdentity result = MechanoidMechanitorIdentity.None;
            if (IsNativeMechanoidMechanitor(pawn))
            {
                result |= MechanoidMechanitorIdentity.Native;
            }
            if (IsAcquiredMechanoidMechanitor(pawn))
            {
                result |= MechanoidMechanitorIdentity.Acquired;
            }
            if (IsMechanicalConsciousnessHost(pawn))
            {
                result |= MechanoidMechanitorIdentity.MechanicalConsciousnessHost;
            }
            if (IsScenarioProtagonist(pawn))
            {
                result |= MechanoidMechanitorIdentity.ScenarioProtagonist;
            }

            return result;
        }

        public static bool CanBecomeAcquiredMechanoidMechanitor(Pawn? pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed)
            {
                return false;
            }

            if (!ModsConfig.BiotechActive || !pawn.RaceProps.IsMechanoid)
            {
                return false;
            }

            if (pawn.health?.hediffSet == null)
            {
                return false;
            }

            return !IsNativeMechanoidMechanitor(pawn);
        }

        public static bool PromoteToAcquiredMechanoidMechanitor(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            if (IsNativeMechanoidMechanitor(pawn))
            {
                EnsureRoleState(pawn);
                return true;
            }

            if (!IsAcquiredMechanoidMechanitor(pawn))
            {
                if (!CanBecomeAcquiredMechanoidMechanitor(pawn))
                {
                    return false;
                }

                HediffDef? def = GetAcquiredIdentityDef();
                if (def == null)
                {
                    Log.Error("[MAP_MechanoidMechanitor] Cannot promote pawn: acquired mechanitor HediffDef is missing.");
                    return false;
                }

                pawn.health.AddHediff(def);
            }

            if (!IsAcquiredMechanoidMechanitor(pawn))
            {
                return false;
            }

            EnsureRoleState(pawn);
            return true;
        }

        public static void EnsureRoleState(Pawn? pawn)
        {
            if (pawn == null || !IsMechanoidMechanitor(pawn))
            {
                return;
            }

            if (pawn.Faction == null)
            {
                pawn.SetFactionDirect(Faction.OfPlayer);
            }

            if (pawn.relations == null)
            {
                pawn.relations = new Pawn_RelationsTracker(pawn);
            }

            MAPMechanitorNodeLifecycleUtility.EnsureBasicTrackers(pawn);

            if (pawn.equipment == null)
            {
                pawn.equipment = new Pawn_EquipmentTracker(pawn);
            }

            if (pawn.interactions == null)
            {
                pawn.interactions = new Pawn_InteractionsTracker(pawn);
            }

            if (pawn.guest == null)
            {
                pawn.guest = new Pawn_GuestTracker(pawn);
            }

            if (pawn.genes == null)
            {
                pawn.genes = new Pawn_GeneTracker(pawn);
            }

            if (pawn.skills == null)
            {
                pawn.skills = new Pawn_SkillTracker(pawn);
            }

            EnsureAcquiredSkillProfile(pawn);
            EnsureWorkSettings(pawn);
            EnsureRoleAbilities(pawn);

            PawnComponentsUtility.AddAndRemoveDynamicComponents(pawn, actAsIfSpawned: true);
            pawn.mechanitor?.Notify_BandwidthChanged();
        }

        public static bool UsesVanillaControlPath(Pawn? pawn)
        {
            if (IsAcquiredMechanoidMechanitor(pawn))
            {
                return true;
            }

            return CompMAPMechanitorNode.TryGetNodeComp(pawn, out CompMAPMechanitorNode? comp)
                && comp?.NodeProps?.controlBackend == MAPMechanitorControlBackend.Vanilla;
        }

        public static bool RequiresExternalOverseer(Pawn? pawn)
        {
            if (IsAcquiredMechanoidMechanitor(pawn))
            {
                return !IsMechanicalConsciousnessHost(pawn);
            }

            return CompMAPMechanitorNode.TryGetNodeComp(pawn, out CompMAPMechanitorNode? comp)
                && comp?.NodeProps?.requiresExternalOverseer == true;
        }

        public static int GetExtraMechBandwidth(Pawn? pawn)
        {
            if (CompMAPMechanitorNode.TryGetNodeComp(pawn, out CompMAPMechanitorNode? nativeComp)
                && nativeComp != null)
            {
                return nativeComp.CurrentIntrinsicBandwidth;
            }

            if (TryGetAcquiredIdentityComp(pawn, out HediffComp_AcquiredMechanoidMechanitor? acquiredComp)
                && acquiredComp != null)
            {
                return acquiredComp.CurrentIntrinsicBandwidth;
            }

            return 0;
        }

        public static int GetExtraMechControlGroups(Pawn? pawn)
        {
            if (CompMAPMechanitorNode.TryGetNodeComp(pawn, out CompMAPMechanitorNode? nativeComp)
                && nativeComp?.NodeProps != null)
            {
                return nativeComp.NodeProps.extraMechControlGroups;
            }

            return IsAcquiredMechanoidMechanitor(pawn)
                ? AcquiredExtraControlGroups
                : 0;
        }

        public static bool AllowsBossChipBandwidthUpgrade(Pawn? pawn)
        {
            if (CompMAPMechanitorNode.TryGetNodeComp(pawn, out CompMAPMechanitorNode? nativeComp)
                && nativeComp?.NodeProps != null)
            {
                return nativeComp.NodeProps.allowBossChipBandwidthUpgrade
                    && nativeComp.NodeProps.maxIntrinsicBandwidth > 0;
            }

            return IsAcquiredMechanoidMechanitor(pawn);
        }

        public static int GetMaxIntrinsicBandwidth(Pawn? pawn)
        {
            if (CompMAPMechanitorNode.TryGetNodeComp(pawn, out CompMAPMechanitorNode? nativeComp)
                && nativeComp != null)
            {
                return nativeComp.MaxIntrinsicBandwidth;
            }

            return IsAcquiredMechanoidMechanitor(pawn)
                ? AcquiredMaxIntrinsicBandwidth
                : 0;
        }

        public static int GetRemainingIntrinsicBandwidth(Pawn? pawn)
        {
            if (CompMAPMechanitorNode.TryGetNodeComp(pawn, out CompMAPMechanitorNode? nativeComp)
                && nativeComp != null)
            {
                return nativeComp.RemainingIntrinsicBandwidth;
            }

            if (TryGetAcquiredIdentityComp(pawn, out HediffComp_AcquiredMechanoidMechanitor? acquiredComp)
                && acquiredComp != null)
            {
                return acquiredComp.RemainingIntrinsicBandwidth;
            }

            return 0;
        }

        public static int AddChipBandwidth(Pawn? pawn, int requestedAmount)
        {
            if (CompMAPMechanitorNode.TryGetNodeComp(pawn, out CompMAPMechanitorNode? nativeComp)
                && nativeComp != null)
            {
                return nativeComp.AddChipBandwidth(requestedAmount);
            }

            if (TryGetAcquiredIdentityComp(pawn, out HediffComp_AcquiredMechanoidMechanitor? acquiredComp)
                && acquiredComp != null)
            {
                return acquiredComp.AddChipBandwidth(requestedAmount);
            }

            return 0;
        }

        public static bool AllowsHumanWeapons(Pawn? pawn)
        {
            if (IsAcquiredMechanoidMechanitor(pawn))
            {
                return true;
            }

            CompHumanWeaponUser? comp = pawn?.GetComp<CompHumanWeaponUser>();
            return comp != null;
        }

        public static bool AllowsColonistLikeFloatMenu(Pawn? pawn)
        {
            if (IsAcquiredMechanoidMechanitor(pawn))
            {
                return true;
            }

            CompColonistLikeFloatMenuUser? comp = pawn?.GetComp<CompColonistLikeFloatMenuUser>();
            return comp != null && comp.Props.allowColonistLikeFloatMenu;
        }

        public static bool AllowsGravshipPilot(Pawn? pawn)
        {
            if (IsAcquiredMechanoidMechanitor(pawn))
            {
                return true;
            }

            CompGravshipPilotUser? comp = pawn?.GetComp<CompGravshipPilotUser>();
            return comp != null && comp.Props.allowGravshipPilotConsole;
        }

        public static bool AllowsWorkTab(Pawn? pawn)
        {
            if (IsAcquiredMechanoidMechanitor(pawn))
            {
                return true;
            }

            CompWorkTabVisibleUser? comp = pawn?.GetComp<CompWorkTabVisibleUser>();
            return comp != null && comp.Props.showInWorkTab;
        }

        public static bool AllowsPsychicRituals(Pawn? pawn)
        {
            if (IsAcquiredMechanoidMechanitor(pawn))
            {
                return true;
            }

            CompPsychicRitualParticipantUser? comp = pawn?.GetComp<CompPsychicRitualParticipantUser>();
            return comp != null && comp.Props.allowPsychicRituals;
        }

        public static bool IsRoleWorkType(WorkTypeDef? workType)
        {
            return workType != null && roleWorkTypeDefNames.Contains(workType.defName);
        }

        public static List<WorkTypeDef> GetRoleWorkTypes()
        {
            if (cachedRoleWorkTypes == null)
            {
                cachedRoleWorkTypes = new List<WorkTypeDef>();
                List<WorkTypeDef> all = DefDatabase<WorkTypeDef>.AllDefsListForReading;
                for (int i = 0; i < all.Count; i++)
                {
                    WorkTypeDef def = all[i];
                    if (roleWorkTypeDefNames.Contains(def.defName))
                    {
                        cachedRoleWorkTypes.Add(def);
                    }
                }
            }

            return cachedRoleWorkTypes;
        }

        public static bool TryGetAcquiredIdentityComp(
            Pawn? pawn,
            out HediffComp_AcquiredMechanoidMechanitor? comp)
        {
            comp = null;
            HediffDef? def = GetAcquiredIdentityDef();
            if (pawn?.health?.hediffSet == null || def == null)
            {
                return false;
            }

            Hediff? hediff = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            comp = hediff?.TryGetComp<HediffComp_AcquiredMechanoidMechanitor>();
            return comp != null;
        }

        private static HediffDef? GetAcquiredIdentityDef()
        {
            return acquiredIdentityDef ??=
                DefDatabase<HediffDef>.GetNamedSilentFail(AcquiredIdentityDefName);
        }

        private static void EnsureWorkSettings(Pawn pawn)
        {
            if (pawn.workSettings == null)
            {
                pawn.workSettings = new Pawn_WorkSettings(pawn);
            }

            if (!pawn.workSettings.Initialized)
            {
                pawn.workSettings.EnableAndInitialize();
            }

            MechWorkSettingsUtility.RestrictToMechEnabledWorkTypes(pawn);
        }

        private static void EnsureRoleAbilities(Pawn pawn)
        {
            if (pawn.abilities == null)
            {
                pawn.abilities = new Pawn_AbilityTracker(pawn);
            }

            mechRecodeAbilityDef ??=
                DefDatabase<AbilityDef>.GetNamedSilentFail(MechRecodeAbilityDefName);
            mechReconstructionAbilityDef ??=
                DefDatabase<AbilityDef>.GetNamedSilentFail(MechReconstructionAbilityDefName);

            if (mechRecodeAbilityDef != null)
            {
                pawn.abilities.GainAbility(mechRecodeAbilityDef);
            }
            if (mechReconstructionAbilityDef != null)
            {
                pawn.abilities.GainAbility(mechReconstructionAbilityDef);
            }
        }

        private static void EnsureAcquiredSkillProfile(Pawn pawn)
        {
            if (!IsAcquiredMechanoidMechanitor(pawn) || pawn.skills == null)
            {
                return;
            }

            SetSkillLevel(pawn, SkillDefOf.Shooting, 18);
            SetSkillLevel(pawn, SkillDefOf.Melee, 16);
            SetSkillLevel(pawn, SkillDefOf.Social, 12);
            SetSkillLevel(pawn, SkillDefOf.Crafting, 12);
            SetSkillLevel(pawn, SkillDefOf.Construction, 10);
            SetSkillLevel(pawn, SkillDefOf.Mining, 10);
            SetSkillLevel(pawn, SkillDefOf.Cooking, 10);
            SetSkillLevel(pawn, SkillDefOf.Plants, 10);
            SetSkillLevel(pawn, SkillDefOf.Animals, 10);
            SetSkillLevel(pawn, SkillDefOf.Artistic, 10);
            SetSkillLevel(pawn, SkillDefOf.Medicine, 10);
            SetSkillLevel(pawn, SkillDefOf.Intellectual, 10);
        }

        private static void SetSkillLevel(Pawn pawn, SkillDef skill, int level)
        {
            SkillRecord? record = pawn.skills?.GetSkill(skill);
            if (record != null && record.Level < level)
            {
                record.Level = level;
            }
        }
    }
}
