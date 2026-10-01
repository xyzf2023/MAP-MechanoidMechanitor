using System;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class MechanoidMechanitorCapabilityUtility
    {
        private static readonly MechanoidMechanitorCapability[] DefinedCapabilities =
            (MechanoidMechanitorCapability[])Enum.GetValues(typeof(MechanoidMechanitorCapability));

        /// <summary>带工作类型参数的能力查询；保留监管、驯兽、保育的独立授权和白名单。</summary>
        public static bool AllowsWorkGiver(Pawn? pawn, WorkGiverDef? workGiver, bool vanillaAllowed = false) =>
            MechWorkTypeAuthorizationUtility.AllowsWorkGiver(pawn, workGiver, vanillaAllowed);

        public static bool HasCapability(
            Pawn? pawn,
            MechanoidMechanitorCapability capability)
        {
            if (pawn == null || capability == MechanoidMechanitorCapability.None)
            {
                return false;
            }

            long requested = (long)capability;
            if ((requested & (requested - 1)) == 0)
                return HasSingleCapability(pawn, capability);

            foreach (MechanoidMechanitorCapability candidate in DefinedCapabilities)
            {
                long bit = (long)candidate;
                if (bit == 0 || (bit & (bit - 1)) != 0 || (requested & bit) == 0)
                    continue;
                if (!HasSingleCapability(pawn, candidate))
                    return false;
                requested &= ~bit;
            }

            return requested == 0;
        }

        /// <summary>完整清单复用单项来源，查询不初始化 Tracker、不修改授权。</summary>
        public static MechanoidMechanitorCapability GetCapabilities(Pawn? pawn)
        {
            MechanoidMechanitorCapability result = MechanoidMechanitorCapability.None;
            if (pawn == null)
                return result;

            foreach (MechanoidMechanitorCapability candidate in DefinedCapabilities)
            {
                long bit = (long)candidate;
                if (bit != 0 && (bit & (bit - 1)) == 0 && HasSingleCapability(pawn, candidate))
                    result |= candidate;
            }

            return result;
        }

        private static bool HasSingleCapability(Pawn pawn, MechanoidMechanitorCapability capability)
        {
            if (capability == MechanoidMechanitorCapability.AutonomousMech)
            {
                return GameComponent_AutonomousMechRegistry.IsAuthorized(pawn);
            }

            if (capability == MechanoidMechanitorCapability.CommandRangeBypass
                || capability == MechanoidMechanitorCapability.CrossMapCommand)
            {
                return (MechCommandRangeUtility.ResolveCapabilities(pawn) & capability) == capability;
            }

            // 移动热路径只检查组件和机动作战标记，不聚合其他能力。
            if (capability == MechanoidMechanitorCapability.MovementCostImmunity)
            {
                return HasMovementCostImmunityCapability(pawn);
            }

            // 仿生伴侣能力仅来自动态授权注册表，避免完整能力汇总。
            if (capability == MechanoidMechanitorCapability.SyntheticSpouseInteraction
                || capability == MechanoidMechanitorCapability.SyntheticPregnancy)
            {
                return SyntheticCompanionStateUtility.IsSyntheticCompanion(pawn);
            }

            if (capability == MechanoidMechanitorCapability.Flight)
            {
                return GameComponent_MechanicalFlightRegistry.IsAuthorized(pawn);
            }

            if (capability == MechanoidMechanitorCapability.MechanitorControl)
            {
                return HasMechanitorControlCapability(pawn);
            }

            if (capability == MechanoidMechanitorCapability.IndividualSkills)
            {
                return HasIndividualSkills(pawn);
            }

            if (capability == MechanoidMechanitorCapability.CharacterTab)
            {
                return HasCharacterTab(pawn);
            }

            if (capability == MechanoidMechanitorCapability.SelfRepair
                || capability == MechanoidMechanitorCapability.Recreation
                || capability == MechanoidMechanitorCapability.GeneralMechWork
                || capability == MechanoidMechanitorCapability.DynamicWorkTypes
                || capability == MechanoidMechanitorCapability.ManagedSchedule
                || capability == MechanoidMechanitorCapability.Inspiration
                || capability == MechanoidMechanitorCapability.SelfDataProcessing
                || capability == MechanoidMechanitorCapability.EnhancedControlModes
                || capability == MechanoidMechanitorCapability.ImplantSelfEffects
                || capability == MechanoidMechanitorCapability.SelfWorkSpeedFeedback
                || capability == MechanoidMechanitorCapability.ImplantInstallation
                || capability == MechanoidMechanitorCapability.Royalty
                || capability == MechanoidMechanitorCapability.Psycasting)
            {
                return GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(pawn, out _);
            }

            if (capability == MechanoidMechanitorCapability.DataProcessing)
                return HasDataProcessing(pawn);
            if (capability == MechanoidMechanitorCapability.BandwidthUpgrade)
                return HasBandwidthUpgrade(pawn);
            if (capability == MechanoidMechanitorCapability.ResearchAbilityRecipient)
                return ManagedAbilityEligibilityUtility.IsResearchConsciousnessRecipient(pawn);

            // 常用单项只读其来源，不为一次持械/工作查询解析指挥关系、合体或全部能力。
            switch (capability)
            {
                case MechanoidMechanitorCapability.HumanWeapons:
                    return IsAcquiredSource(pawn) || pawn.GetComp<CompHumanWeaponUser>() != null;
                case MechanoidMechanitorCapability.ColonistLikeFloatMenu:
                    return IsAcquiredSource(pawn) || pawn.GetComp<CompColonistLikeFloatMenuUser>()?.Props.allowColonistLikeFloatMenu == true;
                case MechanoidMechanitorCapability.GravshipPilot:
                    return IsAcquiredSource(pawn) || pawn.GetComp<CompGravshipPilotUser>()?.Props.allowGravshipPilotConsole == true;
                case MechanoidMechanitorCapability.WorkTab:
                    return IsAcquiredSource(pawn) || pawn.GetComp<CompWorkTabVisibleUser>()?.Props.showInWorkTab == true;
                case MechanoidMechanitorCapability.PsychicRituals:
                    return IsAcquiredSource(pawn) || pawn.GetComp<CompPsychicRitualParticipantUser>()?.Props.allowPsychicRituals == true;
                case MechanoidMechanitorCapability.SelfWorkMode:
                    return IsAcquiredSource(pawn) || CompMechanoidMechanitorSelfWorkModeUser.GetFor(pawn) != null;
                case MechanoidMechanitorCapability.ShuttlePilot:
                    return GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(pawn, out _)
                        || DataProcessingAllocationUtility.HasShuttlePilotAllocation(pawn);
                case MechanoidMechanitorCapability.ColonistLikeSocialTab:
                    return GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(pawn, out _)
                        || pawn.GetComp<CompColonistLikeSocialTabUser>() != null
                        || GameComponent_SyntheticCompanionRegistry.HasAuthorizationRecord(pawn);
                case MechanoidMechanitorCapability.ClassroomTeaching:
                    return GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(pawn, out _)
                        || pawn.GetComp<CompClassroomTeachingUser>() != null;
                case MechanoidMechanitorCapability.TravelLeadCaravan:
                    return IsAcquiredSource(pawn) || pawn.GetComp<CompMAPMechanitorTravelNode>()?.TravelProps?.canLeadCaravan == true
                        || DataProcessingAllocationUtility.HasVirtualTravelNode(pawn);
                case MechanoidMechanitorCapability.TravelCollectItems:
                    return IsAcquiredSource(pawn) || pawn.GetComp<CompMAPMechanitorTravelNode>()?.TravelProps?.canCollectCaravanItems == true
                        || DataProcessingAllocationUtility.HasVirtualTravelNode(pawn);
                case MechanoidMechanitorCapability.TravelRefreshTrackers:
                    return IsAcquiredSource(pawn) || pawn.GetComp<CompMAPMechanitorTravelNode>()?.TravelProps?.refreshTrackersOnTransporterArrival == true
                        || DataProcessingAllocationUtility.HasVirtualTravelNode(pawn);
            }

            // 合体资格只能是先天能力，唯一事实来源是合体资格注册表。
            if (capability == MechanoidMechanitorCapability.Fusion)
            {
                return MechFusionEligibilityUtility.HasFusionEligibility(pawn);
            }

            // 自由殖民者替代资格由剧本状态与机械意识宿主身份动态提供。
            if (capability == MechanoidMechanitorCapability.FreeColonistEquivalent)
            {
                return IsScenarioFreeColonistEquivalent(pawn);
            }

            // 意识形态两项能力位可由档位 + 注册表直接得出，跳过完整能力汇总。
            if (capability == MechanoidMechanitorCapability.IdeologyMembership
                || capability == MechanoidMechanitorCapability.IdeologyFullParticipation)
            {
                return (GetIdeologyCapabilities(pawn) & capability) == capability;
            }

            // 作息查询高频触发；只查询单项来源，不执行完整能力聚合。
            if (capability == MechanoidMechanitorCapability.ColonistLikeTimetable)
            {
                return HasColonistLikeTimetableCapability(pawn);
            }

            return false;
        }

        /// <summary>
        /// ColonistLikeTimetable 单项快速判断，能力来源与 GetCapabilities 严格一致：
        /// 正式机械族机械师来自权威注册表；非机械师机械族来自真实
        /// CompColonistLikeTimetableUser。不回退到仿生伴侣、数据处理或飞行授权。
        /// </summary>
        private static bool HasColonistLikeTimetableCapability(Pawn pawn)
        {
            if (pawn.RaceProps?.IsMechanoid != true)
            {
                return false;
            }

            return GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(
                       pawn,
                       out _)
                || pawn.GetComp<CompColonistLikeTimetableUser>() != null;
        }

        // 单项查询与完整汇总共用资格判断；两个来源不叠加、不另存状态。
        private static bool HasMovementCostImmunityCapability(Pawn pawn)
        {
            return pawn.GetComp<CompMovementCostImmunity>() != null
                || MechanoidMechanitorWorkModeUtility.HasMobileCombatFlag(pawn);
        }

        private static bool IsAcquiredSource(Pawn pawn) =>
            GameComponent_MechanoidMechanitorRegistry.TryGetAcquiredMechanitorRecord(pawn, out _);

        private static bool HasDataProcessing(Pawn pawn) =>
            GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(pawn, out _)
            || (pawn.RaceProps?.Humanlike == true && !pawn.RaceProps.IsMechanoid
                && pawn.health?.hediffSet?.HasHediff(MAPMechanitor_HediffDefOf.MAP_ParallelThoughtInterface) == true);

        private static bool HasBandwidthUpgrade(Pawn pawn) =>
            GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(pawn, out _)
            && MechanoidMechanitorRoleUtility.AllowsBossChipBandwidthUpgrade(pawn)
            && MechanoidMechanitorRoleUtility.GetMaxIntrinsicBandwidth(pawn) > 0;

        private static bool HasMechanitorControlCapability(Pawn pawn)
        {
            // 直接读来源，不调用 IsMechanitor/IsMechanitorNodeController，避免递归及初始化副作用。
            return (pawn.RaceProps?.IsMechanoid == true
                    && (GameComponent_MechanoidMechanitorRegistry.TryGetAcquiredMechanitorRecord(pawn, out _)
                        || pawn.GetComp<CompMAPMechanitorNode>()?.NodeProps?.controlBackend
                            == MAPMechanitorControlBackend.Vanilla))
                || pawn.health?.hediffSet?.HasHediff(
                       HediffDefOf.MechlinkImplant) == true
                || MechFusionMechanitorSynchronizationService
                    .HasTemporaryMechanitorAccess(pawn);
        }

        private static bool HasIndividualSkills(Pawn pawn) =>
            GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(pawn, out _)
            || pawn.GetComp<CompCommanderSkills>() != null;

        private static bool HasCharacterTab(Pawn pawn) =>
            GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(pawn, out _)
            || pawn.GetComp<CompColonistLikeMechProfile>() != null;

        /// <summary>
        /// 专属剧本中当前机械意识宿主的全局自由殖民者替代资格。
        /// 仅由场景状态与注册表身份动态决定，不来自 ThingDef Comp。
        /// </summary>
        private static bool IsScenarioFreeColonistEquivalent(Pawn pawn)
        {
            return GameComponent_MechanoidMechanitorScenarioState.IsEnabled
                && GameComponent_MechanoidMechanitorRegistry
                    .IsMechanicalConsciousnessHost(pawn)
                && GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(
                    pawn,
                    out _);
        }

        /// <summary>
        /// 意识形态能力位的唯一事实来源。只读取文化适配档位与注册表身份，
        /// 不回调 AllowsIdeology...，避免与意识形态模块形成递归。
        /// </summary>
        private static MechanoidMechanitorCapability GetIdeologyCapabilities(
            Pawn? pawn)
        {
            if (pawn == null
                || pawn.Destroyed
                || !GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(
                    pawn,
                    out _))
            {
                return MechanoidMechanitorCapability.None;
            }

            MechanoidMechanitorIdeologyAdaptationLevel level =
                MechanoidMechanitorIdeologyAdaptationUtility.GetEffectiveLevel();
            if (level >= MechanoidMechanitorIdeologyAdaptationLevel.Full)
            {
                return MechanoidMechanitorCapability.IdeologyMembership
                    | MechanoidMechanitorCapability.IdeologyFullParticipation;
            }

            return level >= MechanoidMechanitorIdeologyAdaptationLevel.Partial
                ? MechanoidMechanitorCapability.IdeologyMembership
                : MechanoidMechanitorCapability.None;
        }

    }
}
