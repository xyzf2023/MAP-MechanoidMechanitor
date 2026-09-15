using MAP_MechanoidMechanitor.Scenarios;
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

            return (GetCapabilities(pawn) & capability) == capability;
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

        public static MechanoidMechanitorCapability GetCapabilities(Pawn? pawn)
        {
            if (pawn == null)
            {
                return MechanoidMechanitorCapability.None;
            }

            MechanoidMechanitorCapability capabilities = MechanoidMechanitorCapability.None;
            AddCapabilitiesFromRealComponents(pawn, ref capabilities);
            AddCapabilitiesFromMechanitorIdentity(pawn, ref capabilities);
            AddCapabilitiesFromScenarioState(pawn, ref capabilities);
            AddCapabilitiesFromSyntheticCompanionAuthorization(pawn, ref capabilities);
            AddCapabilitiesFromDataProcessingAllocation(pawn, ref capabilities);
            AddCapabilitiesFromMechanicalFlightAuthorization(pawn, ref capabilities);
            AddCapabilitiesFromMechanitorControl(pawn, ref capabilities);
            AddCapabilitiesFromFusionEligibility(pawn, ref capabilities);
            return capabilities;
        }

        private static bool HasMechanitorControlCapability(Pawn pawn)
        {
            return pawn.health?.hediffSet?.HasHediff(
                       HediffDefOf.MechlinkImplant) == true
                || MechFusionMechanitorSynchronizationService
                    .HasTemporaryMechanitorAccess(pawn);
        }

        private static void AddCapabilitiesFromMechanitorControl(
            Pawn pawn,
            ref MechanoidMechanitorCapability capabilities)
        {
            if (HasMechanitorControlCapability(pawn))
            {
                capabilities |= MechanoidMechanitorCapability.MechanitorControl;
            }
        }

        /// <summary>
        /// 合体资格只从先天资格注册表加入一次；其他能力位保持原有来源。
        /// </summary>
        private static void AddCapabilitiesFromFusionEligibility(
            Pawn pawn,
            ref MechanoidMechanitorCapability capabilities)
        {
            if (MechFusionEligibilityUtility.HasFusionEligibility(pawn))
            {
                capabilities |= MechanoidMechanitorCapability.Fusion;
            }
        }

        private static void AddCapabilitiesFromMechanicalFlightAuthorization(
            Pawn pawn,
            ref MechanoidMechanitorCapability capabilities)
        {
            if (GameComponent_MechanicalFlightRegistry.IsAuthorized(pawn))
            {
                capabilities |= MechanoidMechanitorCapability.Flight;
            }
        }

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

        private static void AddCapabilitiesFromScenarioState(
            Pawn pawn,
            ref MechanoidMechanitorCapability capabilities)
        {
            if (IsScenarioFreeColonistEquivalent(pawn))
            {
                capabilities |= MechanoidMechanitorCapability.FreeColonistEquivalent;
            }
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

            // 非机械师机械族（如恋人）通过真实 ThingComp 声明 timetable 数据层能力。
            // 业务代码完全不需要知道其 PawnDef；能力提升只查询，不产生副作用。
            if (pawn.GetComp<CompColonistLikeTimetableUser>() != null)
            {
                capabilities |= MechanoidMechanitorCapability.ColonistLikeTimetable;
            }

            // 非机械师机械族通过真实 ThingComp 声明课堂教师候选能力。
            // 具体是否允许 Skill / Daycare 仍由 ProgressionEducation 课程白名单负责。
            if (pawn.GetComp<CompClassroomTeachingUser>() != null)
            {
                capabilities |= MechanoidMechanitorCapability.ClassroomTeaching;
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

            // 所有正式机械族机械师（Native + Acquired）天然获得 timetable 数据层与课堂教师候选能力。
            // 这一分支在“是否为后天机械师”的二次判断之前执行，确保非后天机械师同样获得这些能力。
            // Psycasting 是能力层自身的正式能力来源，不再由外部 Harmony 补丁注入。
            capabilities |= MechanoidMechanitorCapability.ImplantInstallation
                | MechanoidMechanitorCapability.ShuttlePilot
                | MechanoidMechanitorCapability.ColonistLikeSocialTab
                | MechanoidMechanitorCapability.Royalty
                | MechanoidMechanitorCapability.ColonistLikeTimetable
                | MechanoidMechanitorCapability.ClassroomTeaching
                | MechanoidMechanitorCapability.Psycasting;

            capabilities |= GetIdeologyCapabilities(pawn);

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

        /// <summary>
        /// 动态仿生伴侣授权来源。仅查询注册表，不回调状态工具，避免与 TryGetState 递归。
        /// </summary>
        private static void AddCapabilitiesFromSyntheticCompanionAuthorization(
            Pawn pawn,
            ref MechanoidMechanitorCapability capabilities)
        {
            if (!GameComponent_SyntheticCompanionRegistry.IsAuthorized(pawn))
            {
                return;
            }

            capabilities |= MechanoidMechanitorCapability.ColonistLikeSocialTab
                | MechanoidMechanitorCapability.SyntheticSpouseInteraction
                | MechanoidMechanitorCapability.SyntheticPregnancy;
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
