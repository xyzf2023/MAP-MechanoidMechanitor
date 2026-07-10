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
        MechanicalConsciousnessHost = 4
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
            HediffDef? def = GetAcquiredIdentityDef();
            return pawn?.health?.hediffSet != null
                && def != null
                && pawn.health.hediffSet.HasHediff(def);
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

            if (Current.Game?.GetComponent<GameComponent_MechanoidMechanitorRegistry>() == null)
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
                return GameComponent_MechanoidMechanitorRegistry
                    .GrantAcquiredMechanitorIdentity(pawn);
            }

            if (!CanBecomeAcquiredMechanoidMechanitor(pawn))
            {
                return false;
            }

            Pawn? previousOverseer = pawn.GetOverseer();
            RemoveExternalOverseerForPromotion(pawn, previousOverseer);

            try
            {
                if (!GameComponent_MechanoidMechanitorRegistry
                        .GrantAcquiredMechanitorIdentity(pawn))
                {
                    RestoreExternalOverseerAfterFailedPromotion(pawn, previousOverseer);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                if (!IsAcquiredMechanoidMechanitor(pawn))
                {
                    RestoreExternalOverseerAfterFailedPromotion(pawn, previousOverseer);
                }

                Log.Error(
                    $"[MAP-机械族机械师] PromoteToAcquiredMechanoidMechanitor 对 {pawn} 失败：{ex}");
                return false;
            }
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
            EnsureSkillInfrastructure(pawn);

            if (IsAcquiredMechanoidMechanitor(pawn))
            {
                pawn.story!.bodyType ??= BodyTypeDefOf.Male;
            }

            EnsureAcquiredSkillProfile(pawn);
            EnsureWorkSettings(pawn);

            PawnComponentsUtility.AddAndRemoveDynamicComponents(
                pawn,
                actAsIfSpawned: true);
            MAPMechanitorInitializationUtility.FinalizeNow(pawn);
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
            if (IsMechanoidMechanitor(pawn))
            {
                return false;
            }

            return CompMAPMechanitorNode.TryGetNodeComp(
                    pawn,
                    out CompMAPMechanitorNode? comp)
                && comp?.NodeProps?.requiresExternalOverseer == true;
        }

        public static int GetExtraMechBandwidth(Pawn? pawn)
        {
            if (GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(
                    pawn,
                    out MechanoidMechanitorRecord? record)
                && record != null
                && record.Pawn != null)
            {
                return GetBaseIntrinsicBandwidth(record.Pawn, record) + record.ChipBandwidthBonus;
            }

            if (CompMAPMechanitorNode.TryGetNodeComp(
                    pawn,
                    out CompMAPMechanitorNode? nodeComp)
                && nodeComp != null)
            {
                return nodeComp.CurrentIntrinsicBandwidth;
            }

            return 0;
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

        public static bool AllowsHumanWeapons(Pawn? pawn) =>
            MechanoidMechanitorCapabilityUtility.HasCapability(
                pawn,
                MechanoidMechanitorCapability.HumanWeapons);

        public static bool AllowsColonistLikeFloatMenu(Pawn? pawn) =>
            MechanoidMechanitorCapabilityUtility.HasCapability(
                pawn,
                MechanoidMechanitorCapability.ColonistLikeFloatMenu);

        public static bool AllowsGravshipPilot(Pawn? pawn) =>
            MechanoidMechanitorCapabilityUtility.HasCapability(
                pawn,
                MechanoidMechanitorCapability.GravshipPilot);

        public static bool AllowsWorkTab(Pawn? pawn) =>
            MechanoidMechanitorCapabilityUtility.HasCapability(
                pawn,
                MechanoidMechanitorCapability.WorkTab);

        public static bool AllowsPsychicRituals(Pawn? pawn) =>
            MechanoidMechanitorCapabilityUtility.HasCapability(
                pawn,
                MechanoidMechanitorCapability.PsychicRituals);

        public static bool AllowsShuttlePilot(Pawn? pawn) =>
            MechanoidMechanitorCapabilityUtility.HasCapability(
                pawn,
                MechanoidMechanitorCapability.ShuttlePilot);

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

        internal static HediffDef? GetAcquiredIdentityDef()
        {
            return acquiredIdentityDef ??=
                DefDatabase<HediffDef>.GetNamedSilentFail(AcquiredIdentityDefName);
        }

        private static void RemoveExternalOverseerForPromotion(
            Pawn pawn,
            Pawn? previousOverseer)
        {
            if (previousOverseer?.relations == null)
            {
                return;
            }

            if (pawn.mechanitor?.ControlledPawns.Contains(previousOverseer) == true)
            {
                return;
            }

            previousOverseer.relations.RemoveDirectRelation(
                PawnRelationDefOf.Overseer,
                pawn);
        }

        private static void RestoreExternalOverseerAfterFailedPromotion(
            Pawn pawn,
            Pawn? previousOverseer)
        {
            if (IsAcquiredMechanoidMechanitor(pawn)
                || previousOverseer == null
                || previousOverseer.Destroyed
                || previousOverseer.relations == null
                || pawn.GetOverseer() != null)
            {
                return;
            }

            if (previousOverseer.relations.DirectRelationExists(
                    PawnRelationDefOf.Overseer,
                    pawn))
            {
                return;
            }

            previousOverseer.relations.AddDirectRelation(PawnRelationDefOf.Overseer, pawn);
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

        private static void EnsureSkillInfrastructure(Pawn pawn)
        {
            pawn.story ??= new Pawn_StoryTracker(pawn);
            pawn.story.traits ??= new TraitSet(pawn);
            pawn.skills ??= new Pawn_SkillTracker(pawn);
        }

        private static void EnsureWorkSettings(Pawn pawn)
        {
            if (!MechWorkSettingsUtility.TryEnsureWorkSettingsInitialized(pawn))
            {
                return;
            }

            MechWorkSettingsUtility.RestrictToMechEnabledWorkTypes(pawn);
        }

        private static void EnsureAcquiredSkillProfile(Pawn pawn)
        {
            if (!IsAcquiredMechanoidMechanitor(pawn) || pawn.skills == null)
            {
                return;
            }

            SetSkillLevel(pawn, SkillDefOf.Shooting, 18);      // 射击
            SetSkillLevel(pawn, SkillDefOf.Melee, 16);         // 格斗
            SetSkillLevel(pawn, SkillDefOf.Social, 12);        // 社交
            SetSkillLevel(pawn, SkillDefOf.Crafting, 16);      // 制作
            SetSkillLevel(pawn, SkillDefOf.Construction, 10);  // 建造
            SetSkillLevel(pawn, SkillDefOf.Mining, 6);        // 采矿
            SetSkillLevel(pawn, SkillDefOf.Cooking, 6);       // 烹饪
            SetSkillLevel(pawn, SkillDefOf.Plants, 6);        // 种植
            SetSkillLevel(pawn, SkillDefOf.Animals, 6);       // 驯兽
            SetSkillLevel(pawn, SkillDefOf.Artistic, 10);      // 艺术
            SetSkillLevel(pawn, SkillDefOf.Medicine, 6);      // 医疗
            SetSkillLevel(pawn, SkillDefOf.Intellectual, 12);  // 智识
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
