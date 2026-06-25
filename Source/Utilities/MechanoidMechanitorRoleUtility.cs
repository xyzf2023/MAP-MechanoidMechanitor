using System;
using System.Collections.Generic;
using MAP_MechanoidMechanitor.Scenarios;
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
        private static List<WorkTypeDef>? cachedRoleWorkTypes;

        public static bool HasNativeMechanitorMarker(Pawn? pawn)
        {
            return pawn?.GetComp<CompNativeMechanoidMechanitor>() != null;
        }

        public static bool HasAcquiredMechanitorHediff(Pawn? pawn)
        {
            return TryGetAcquiredIdentityComp(pawn, out _);
        }

        public static bool IsNativeMechanoidMechanitor(Pawn? pawn)
        {
            return GameComponent_MechanoidMechanitorRegistry.TryGetNativeMechanitorRecord(
                pawn,
                out _);
        }

        public static bool IsAcquiredMechanoidMechanitor(Pawn? pawn)
        {
            return GameComponent_MechanoidMechanitorRegistry.TryGetAcquiredMechanitorRecord(
                pawn,
                out _);
        }

        public static bool IsMechanoidMechanitor(Pawn? pawn)
        {
            return GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(
                pawn,
                out _);
        }

        public static bool IsMechanicalConsciousnessHost(Pawn? pawn)
        {
            return GameComponent_MechanoidMechanitorRegistry
                .IsMechanicalConsciousnessHost(pawn);
        }

        public static bool IsScenarioProtagonist(Pawn? pawn)
        {
            return GameComponent_MechanoidMechanitorRegistry
                .IsScenarioProtagonist(pawn);
        }

        public static bool CanHostMechanicalConsciousness(Pawn? pawn)
        {
            return GameComponent_MechanoidMechanitorRegistry
                .CanHostMechanicalConsciousness(pawn);
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

            if (pawn.health?.hediffSet == null
                || HasNativeMechanitorMarker(pawn)
                || IsAcquiredMechanoidMechanitor(pawn))
            {
                return false;
            }

            return pawn.Faction == null || pawn.Faction.IsPlayerSafe();
        }

        public static bool PromoteToAcquiredMechanoidMechanitor(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            if (IsNativeMechanoidMechanitor(pawn))
            {
                GameComponent_MechanoidMechanitorRegistry.EnsureNativeMechanitorRecord(pawn);
                EnsureRoleState(pawn);
                return true;
            }

            if (IsAcquiredMechanoidMechanitor(pawn))
            {
                GameComponent_MechanoidMechanitorRegistry.EnsureAcquiredMechanitorHediff(pawn);
                EnsureRoleState(pawn);
                return true;
            }

            if (!CanBecomeAcquiredMechanoidMechanitor(pawn))
            {
                return false;
            }

            return GameComponent_MechanoidMechanitorRegistry
                .GrantAcquiredMechanitorIdentity(pawn);
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

            pawn.relations ??= new Pawn_RelationsTracker(pawn);
            MAPMechanitorNodeLifecycleUtility.EnsureBasicTrackers(pawn);
            pawn.equipment ??= new Pawn_EquipmentTracker(pawn);
            pawn.interactions ??= new Pawn_InteractionsTracker(pawn);
            pawn.guest ??= new Pawn_GuestTracker(pawn);
            pawn.genes ??= new Pawn_GeneTracker(pawn);
            pawn.skills ??= new Pawn_SkillTracker(pawn);

            if (IsAcquiredMechanoidMechanitor(pawn))
            {
                pawn.story ??= new Pawn_StoryTracker(pawn);
                pawn.story.bodyType ??= BodyTypeDefOf.Male;
            }

            EnsureAcquiredSkillProfile(pawn);
            EnsureWorkSettings(pawn);

            PawnComponentsUtility.AddAndRemoveDynamicComponents(
                pawn,
                actAsIfSpawned: true);
            pawn.mechanitor?.Notify_BandwidthChanged();
        }

        public static bool UsesVanillaControlPath(Pawn? pawn)
        {
            if (IsAcquiredMechanoidMechanitor(pawn))
            {
                return true;
            }

            return CompMAPMechanitorNode.TryGetNodeComp(
                    pawn,
                    out CompMAPMechanitorNode? comp)
                && comp?.NodeProps?.controlBackend == MAPMechanitorControlBackend.Vanilla;
        }

        public static bool RequiresExternalOverseer(Pawn? pawn)
        {
            if (IsAcquiredMechanoidMechanitor(pawn))
            {
                return !IsMechanicalConsciousnessHost(pawn);
            }

            return CompMAPMechanitorNode.TryGetNodeComp(
                    pawn,
                    out CompMAPMechanitorNode? comp)
                && comp?.NodeProps?.requiresExternalOverseer == true;
        }

        public static int GetExtraMechBandwidth(Pawn? pawn)
        {
            if (!GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(
                    pawn,
                    out MechanoidMechanitorRecord? record)
                || record == null
                || record.Pawn == null)
            {
                return 0;
            }

            return GetBaseIntrinsicBandwidth(record.Pawn, record) + record.ChipBandwidthBonus;
        }

        public static int GetExtraMechControlGroups(Pawn? pawn)
        {
            if (IsAcquiredMechanoidMechanitor(pawn))
            {
                return AcquiredExtraControlGroups;
            }

            if (CompMAPMechanitorNode.TryGetNodeComp(
                    pawn,
                    out CompMAPMechanitorNode? nativeComp)
                && nativeComp?.NodeProps != null)
            {
                return nativeComp.NodeProps.extraMechControlGroups;
            }

            return 0;
        }

        public static bool AllowsBossChipBandwidthUpgrade(Pawn? pawn)
        {
            if (IsAcquiredMechanoidMechanitor(pawn))
            {
                return true;
            }

            if (CompMAPMechanitorNode.TryGetNodeComp(
                    pawn,
                    out CompMAPMechanitorNode? nativeComp)
                && nativeComp?.NodeProps != null)
            {
                return nativeComp.NodeProps.allowBossChipBandwidthUpgrade
                    && nativeComp.NodeProps.maxIntrinsicBandwidth > 0;
            }

            return false;
        }

        public static int GetMaxIntrinsicBandwidth(Pawn? pawn)
        {
            if (IsAcquiredMechanoidMechanitor(pawn))
            {
                return AcquiredMaxIntrinsicBandwidth;
            }

            if (CompMAPMechanitorNode.TryGetNodeComp(
                    pawn,
                    out CompMAPMechanitorNode? nativeComp)
                && nativeComp != null)
            {
                return nativeComp.MaxIntrinsicBandwidth;
            }

            return 0;
        }

        public static int GetRemainingIntrinsicBandwidth(Pawn? pawn)
        {
            if (!GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(
                    pawn,
                    out MechanoidMechanitorRecord? record)
                || record == null
                || record.Pawn == null)
            {
                return 0;
            }

            int maxIntrinsic = GetMaxIntrinsicBandwidth(pawn);
            if (maxIntrinsic <= 0)
            {
                return 0;
            }

            int current = GetBaseIntrinsicBandwidth(record.Pawn, record) + record.ChipBandwidthBonus;
            return Math.Max(0, maxIntrinsic - current);
        }

        public static int AddChipBandwidth(Pawn? pawn, int requestedAmount)
        {
            if (requestedAmount <= 0
                || !AllowsBossChipBandwidthUpgrade(pawn)
                || !GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(
                    pawn,
                    out MechanoidMechanitorRecord? record)
                || record == null)
            {
                return 0;
            }

            int actualAdded = Math.Min(requestedAmount, GetRemainingIntrinsicBandwidth(pawn));
            if (actualAdded <= 0)
            {
                return 0;
            }

            record.ChipBandwidthBonus += actualAdded;
            pawn?.mechanitor?.Notify_BandwidthChanged();
            return actualAdded;
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

            CompColonistLikeFloatMenuUser? comp =
                pawn?.GetComp<CompColonistLikeFloatMenuUser>();
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

            CompPsychicRitualParticipantUser? comp =
                pawn?.GetComp<CompPsychicRitualParticipantUser>();
            return comp != null && comp.Props.allowPsychicRituals;
        }

        public static bool IsRoleWorkType(WorkTypeDef? workType)
        {
            return workType != null
                && roleWorkTypeDefNames.Contains(workType.defName);
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

        internal static HediffDef? GetAcquiredIdentityDef()
        {
            return acquiredIdentityDef ??=
                DefDatabase<HediffDef>.GetNamedSilentFail(AcquiredIdentityDefName);
        }

        private static int GetBaseIntrinsicBandwidth(
            Pawn pawn,
            MechanoidMechanitorRecord record)
        {
            if (record.Origin == MechanoidMechanitorOrigin.Acquired)
            {
                return AcquiredBaseExtraBandwidth;
            }

            if (CompMAPMechanitorNode.TryGetNodeComp(pawn, out CompMAPMechanitorNode? nativeComp)
                && nativeComp != null)
            {
                return nativeComp.BaseExtraMechBandwidth;
            }

            return 0;
        }

        private static void EnsureWorkSettings(Pawn pawn)
        {
            pawn.workSettings ??= new Pawn_WorkSettings(pawn);
            if (!pawn.workSettings.Initialized)
            {
                pawn.workSettings.EnableAndInitialize();
            }

            MechWorkSettingsUtility.RestrictToMechEnabledWorkTypes(pawn);
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
            if (pawn.skills == null)
            {
                return;
            }

            if (pawn.skills.skills == null)
            {
                pawn.skills.skills = new List<SkillRecord>();
            }

            List<SkillRecord> skillsList = pawn.skills.skills;
            SkillRecord? record = null;
            for (int i = 0; i < skillsList.Count; i++)
            {
                SkillRecord? candidate = skillsList[i];
                if (candidate != null && candidate.def == skill)
                {
                    record = candidate;
                    break;
                }
            }

            if (record != null)
            {
                if (record.Level < level)
                {
                    record.Level = level;
                }

                return;
            }

            record = new SkillRecord(pawn, skill)
            {
                Level = level
            };
            skillsList.Add(record);
        }
    }
}
