using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    [StaticConstructorOnStartup]
    internal static class GroupFlightUtility
    {
        internal const int Radius = 8;
        internal const int LandingRadius = 10;
        private static readonly Texture2D Icon =
            ContentFinder<Texture2D>.Get("UI/GroupAntigravity", false) ?? TexCommand.Install;//群体反重力贴图路径

        internal static GroupFlightMember? Member(Pawn? pawn)
        {
            var registry = GameComponent_MechanicalFlightRegistry.CurrentRegistry;
            return pawn != null && registry != null
                && registry.GroupMembers.TryGetValue(pawn, out var member) ? member : null;
        }

        internal static GroupFlightSession? ProvidedSession(Pawn? pawn)
        {
            var registry = GameComponent_MechanicalFlightRegistry.CurrentRegistry;
            if (pawn == null || registry == null)
                return null;
            foreach (var session in registry.GroupFlights)
                if (ReferenceEquals(session.Provider, pawn))
                    return session;
            return null;
        }

        internal static bool IsManaged(Pawn? pawn) => Member(pawn) != null;
        internal static bool IsProviding(Pawn? pawn) =>
            Member(pawn)?.Session is GroupFlightSession session
                && ReferenceEquals(session.Provider, pawn) && !session.Closing;
        internal static bool IsPassenger(Pawn? pawn) =>
            Member(pawn)?.Session is GroupFlightSession session
                && !ReferenceEquals(session.Provider, pawn);
        internal static bool BlocksIndependentMovement(Pawn? pawn)
        {
            var member = Member(pawn);
            return member != null && (IsPassenger(pawn) || member.Session?.Closing == true
                || member.SearchOrigin.IsValid || pawn?.Drafted != true);
        }

        internal static bool IsAttached(GroupFlightMember member) =>
            !member.SearchOrigin.IsValid && member.Session?.Closing == false
                && GameComponent_MechanicalFlightRegistry.TryGetRecord(member.Pawn, out var record)
                && record?.IsCruising == true;

        internal static int PassengerCount(Pawn? provider)
        {
            var session = ProvidedSession(provider);
            if (session == null || session.Closing)
                return 0;
            int count = 0;
            foreach (var member in session.Members)
                if (!ReferenceEquals(member.Pawn, provider) && IsAttached(member))
                    count++;
            return count;
        }

        internal static bool AllowsDownedLanding(Pawn? pawn) =>
            Member(pawn)?.AllowDownedLanding == true;

        internal static Command MakeCommand(Pawn pawn, MechanicalFlightProfileDef? profile)
        {
            var command = new Command_Action
            {
                defaultLabel = "MAP_GroupFlight_Label".Translate(),
                defaultDesc = "TODO",
                icon = Icon,
                action = () =>
                {
                    if (GravityDisorderUtility.IsAffected(pawn))
                        return;
                    var active = ProvidedSession(pawn);
                    if (active != null)
                    {
                        if (!active.Closing)
                            BeginGroupLanding(active, false);
                    }
                    else
                        TryStart(pawn, profile);
                }
            };
            string? reason = DisabledReason(pawn, profile);
            if (!reason.NullOrEmpty())
                command.Disable(reason);
            return command;
        }

        private static string? DisabledReason(Pawn pawn, MechanicalFlightProfileDef? profile)
        {
            if (GravityDisorderUtility.IsAffected(pawn))
                return GravityDisorderUtility.BlockedReason;
            if (!pawn.Drafted)
                return "MAP_GroupFlight_RequiresDraft".Translate();
            var session = ProvidedSession(pawn);
            if (session != null)
                return session.Closing ? "MAP_MechanicalFlight_Landing".Translate().ToString() : null;
            if (profile == null || IsManaged(pawn))
                return "MAP_MechanicalFlight_Unavailable".Translate();
            return CanJoin(pawn, pawn, profile, out string? reason) ? null : reason;
        }

        private static bool CanJoin(Pawn pawn, Pawn provider,
            MechanicalFlightProfileDef profile, out string? reason)
        {
            reason = "MAP_MechanicalFlight_Unavailable".Translate();
            if (pawn.Faction != Faction.OfPlayer || !pawn.IsPlayerControlled
                || pawn.drafter == null || !pawn.Drafted || pawn.Spawned != true
                || pawn.Map != provider.Map || pawn.Dead || pawn.Downed || pawn.InMentalState
                || IsManaged(pawn) || ProvidedSession(pawn) != null
                || MechanicalFlightUtility.IsAirborne(pawn) || pawn.flight?.Flying == true)
                return false;
            var candidate = new MechanicalFlightAuthorizationRecord(
                pawn, profile, MechanicalFlightAuthorizationSource.None);
            return MechanicalFlightUtility.CanBeginTakeoff(pawn, candidate, true, out reason,
                externallySupported: true, requireEnergy: ReferenceEquals(pawn, provider));
        }

        private static void TryStart(Pawn provider, MechanicalFlightProfileDef? profile)
        {
            string? reason = DisabledReason(provider, profile);
            if (!reason.NullOrEmpty() || profile == null)
            {
                if (!reason.NullOrEmpty())
                    Messages.Message(reason, provider, MessageTypeDefOf.RejectInput, false);
                return;
            }
            var registry = GameComponent_MechanicalFlightRegistry.CurrentRegistry;
            if (registry == null || provider.Map == null)
                return;
            var candidates = new List<Pawn> { provider };
            foreach (Pawn pawn in provider.Map.mapPawns.AllPawnsSpawned)
                if (pawn != provider && pawn.Position.DistanceToSquared(provider.Position) <= Radius * Radius
                    && CanJoin(pawn, provider, profile, out _))
                    candidates.Add(pawn);
            var session = new GroupFlightSession
            {
                Provider = provider, Map = provider.Map,
                LastProviderPosition = provider.Position,
                NextRangeCheckTick = GenTicks.TicksGame + 60
            };
            registry.GroupFlights.Add(session);
            // 全部运行记录先准备完毕，再升空和按实际人数扣一次能量。
            foreach (Pawn pawn in candidates)
            {
                var record = GameComponent_MechanicalFlightRegistry.EnsureRuntimeRecord(pawn);
                if (record == null)
                    continue;
                var member = new GroupFlightMember
                {
                    Pawn = pawn, Session = session, Offset = pawn.Position - provider.Position,
                    ExpectedPosition = pawn.Position
                };
                session.Members.Add(member);
                registry.GroupMembers[pawn] = member;
                record.Purpose = MechanicalFlightPurpose.GroupAntigravity;
                record.SetRuntimeProfile(profile);
            }
            if (Member(provider) == null)
            {
                foreach (var member in session.Members.ToArray())
                    if (GameComponent_MechanicalFlightRegistry.TryGetRecord(member.Pawn, out var record))
                        MechanicalFlightUtility.FinalizeRuntimeState(member.Pawn, record!);
                registry.GroupFlights.Remove(session);
                return;
            }
            foreach (var member in session.Members.ToArray())
            {
                Pawn pawn = member.Pawn!;
                if (session.Closing || pawn.Dead || pawn.Downed || !pawn.Spawned
                    || pawn.Map != session.Map || Member(pawn) != member
                    || !GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record))
                    continue;
                pawn.pather?.StopDead();
                MechanicalFlightStraightPathPatch.ClearMotion(pawn);
                pawn.jobs.ClearQueuedJobs();
                if (profile.breakThinRoofOnTakeoff)
                    MechanicalFlightRoofUtility.BreakThinRoofArea(pawn, profile);
                if (session.Closing || pawn.Dead || pawn.Downed || !pawn.Spawned)
                {
                    MechanicalFlightUtility.FinalizeRuntimeState(pawn, record!);
                    continue;
                }
                pawn.flight.StartFlying();
                if (!pawn.flight.Flying)
                {
                    MechanicalFlightUtility.FinalizeRuntimeState(pawn, record!);
                    continue;
                }
                record!.Phase = MechanicalFlightPhase.TakingOff;
                record.TicksUntilNextEnergyDrain = Math.Max(1, profile.energyDrainIntervalTicks);
                record.LowEnergyWarningSent = false;
                record.PendingExitMap = null;
                GameComponent_MechanicalFlightRegistry.NotifyRuntimeStateChanged(record);
                pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait_Combat),
                    JobCondition.InterruptForced);
                MechanicalFlightPresentationUtility.NotifyFlightStarted(pawn, record);
            }
            foreach (var member in session.Members.ToArray())
                if (GameComponent_MechanicalFlightRegistry.TryGetRecord(member.Pawn, out var idleRecord)
                    && idleRecord?.IsRuntimeActive != true)
                    MechanicalFlightUtility.FinalizeRuntimeState(member.Pawn, idleRecord!);
            if (!IsProviding(provider) || session.Closing)
            {
                BeginGroupLanding(session, false);
                return;
            }
            MechanicalFlightEnergyUtility.TryConsumeFlightEnergy(provider, profile);
            CheckProviderEnergy(session);
        }

        internal static void NotifyUndrafted(Pawn pawn)
        {
            var member = Member(pawn);
            if (member == null || member.Session == null || member.Session.Closing
                || member.SearchOrigin.IsValid)
                return;
            if (ReferenceEquals(member.Session.Provider, pawn))
            {
                BeginGroupLanding(member.Session, pawn.Downed);
                return;
            }
            if (pawn.Downed)
            {
                MechanicalFlightEmergencyUtility.TryCrashFromDowned(pawn);
                return;
            }
            if (GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                && record?.IsCruising == true
                && !MechanicalFlightUtility.TryBeginLanding(pawn))
            {
                MechanicalFlightUtility.NotifyGroundLandingBlocked(pawn, record);
                if (pawn.drafter != null && !pawn.Drafted)
                    pawn.drafter.Drafted = true;
            }
        }

        internal static bool HandleDowned(Pawn pawn)
        {
            var session = ProvidedSession(pawn);
            if (session == null)
                return AllowsDownedLanding(pawn);
            if (session.DeathHandled)
                return false;
            var member = Member(pawn);
            if (member != null)
                member.AllowDownedLanding = true;
            BeginGroupLanding(session, true);
            return true;
        }

        internal static void NotifyProviderDied(Pawn pawn)
        {
            var session = ProvidedSession(pawn);
            if (session == null || session.DeathHandled)
                return;
            session.DeathHandled = true;
            session.Closing = true;
            foreach (var member in session.Members.ToArray())
            {
                Pawn? passenger = member.Pawn;
                if (passenger == null || passenger == pawn || passenger.Dead
                    || !GameComponent_MechanicalFlightRegistry.TryGetRecord(passenger, out var record)
                    || record == null || !record.IsRuntimeActive
                    || record.Phase == MechanicalFlightPhase.Crashing)
                    continue;
                // 先标记由 Crash 结算；连锁爆炸触发的死亡补丁不得再结算第二次。
                MechanicalFlightEmergencyUtility.Crash(passenger, record);
            }
        }

        internal static bool HandleEnergyDepletion(Pawn pawn)
        {
            var member = Member(pawn);
            if (member == null)
                return false;
            if (GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record))
                record!.PendingShutdownAfterLanding = true;
            if (ReferenceEquals(member.Session?.Provider, pawn))
                BeginGroupLanding(member.Session!, false);
            else
                BeginMemberLanding(member);
            return true;
        }

        internal static void BeginGroupLanding(GroupFlightSession session, bool providerDowned)
        {
            if (session.DeathHandled)
                return;
            if (providerDowned && Member(session.Provider) is GroupFlightMember providerMember)
                providerMember.AllowDownedLanding = true;
            if (session.Closing)
                return;
            session.Closing = true;
            // 先固定所有圆心和参考点，再启动会改变 Pawn 状态的任务。
            var members = new List<GroupFlightMember>();
            foreach (var member in session.Members)
                if (GameComponent_MechanicalFlightRegistry.TryGetRecord(member.Pawn, out var record)
                    && record?.IsCruising == true)
                {
                    PrepareSearch(member);
                    members.Add(member);
                }
            // 可选落点少的先预留，减少因处理顺序造成的无谓坠毁。
            var choices = new Dictionary<GroupFlightMember, int>();
            foreach (var member in members)
                choices[member] = CountLandingCells(member);
            members.Sort((a, b) =>
            {
                int compared = choices[a].CompareTo(choices[b]);
                return compared != 0 ? compared
                    : a.Pawn!.thingIDNumber.CompareTo(b.Pawn!.thingIDNumber);
            });
            foreach (var member in members)
                if (Member(member.Pawn) == member)
                    BeginMemberLanding(member);
        }

        private static void PrepareSearch(GroupFlightMember member)
        {
            if (member.SearchOrigin.IsValid || member.Pawn?.Map == null)
                return;
            member.SearchOrigin = member.Pawn.Position;
            member.SearchMap = member.Pawn.Map;
            member.SearchAnchor = member.Session?.Provider?.Map == member.Pawn.Map
                ? member.Session.Provider.Position : member.Pawn.Position;
        }

        internal static void BeginMemberLanding(GroupFlightMember member)
        {
            Pawn? pawn = member.Pawn;
            if (pawn == null || !GameComponent_MechanicalFlightRegistry.TryGetRecord(pawn, out var record)
                || record == null || !record.IsRuntimeActive || record.IsEmergencySequence
                || record.Phase == MechanicalFlightPhase.Landing)
                return;
            PrepareSearch(member);
            MechanicalFlightEmergencyUtility.TryBeginGroupLanding(pawn, record);
        }

        private static int CountLandingCells(GroupFlightMember member)
        {
            if (member.Pawn?.Map == null)
                return 0;
            int count = 0;
            foreach (IntVec3 cell in CellRect.CenteredOn(member.SearchOrigin, LandingRadius)
                .ClipInsideMap(member.Pawn.Map))
                if (IsLandingCellValid(member, cell))
                    count++;
            return count;
        }

        internal static bool IsLandingCellValid(GroupFlightMember member, IntVec3 cell)
        {
            Pawn? pawn = member.Pawn;
            if (pawn?.Map == null || pawn.Map != member.SearchMap || !member.SearchOrigin.IsValid
                || cell.DistanceToSquared(member.SearchOrigin) > LandingRadius * LandingRadius
                || !MechanicalFlightEmergencyUtility.IsSafeLandingCell(cell, pawn, pawn.Map))
                return false;
            // 会话内的逻辑分配立即生效，不等下一 Tick 的 Job 预留。
            var registry = GameComponent_MechanicalFlightRegistry.CurrentRegistry;
            if (registry != null)
                foreach (var other in registry.GroupMembers.Values)
                    if (other != member && other.Pawn?.Map == pawn.Map
                        && GameComponent_MechanicalFlightRegistry.TryGetRecord(other.Pawn, out var record)
                        && record?.IsEmergencySequence == true
                        && record.EmergencyLandingTarget == cell)
                        return false;
            return true;
        }

        internal static bool TryFindLandingCell(GroupFlightMember member, out IntVec3 result)
        {
            result = IntVec3.Invalid;
            if (member.Pawn?.Map == null)
                return false;
            float bestAnchor = float.MaxValue, bestOrigin = float.MaxValue;
            foreach (IntVec3 cell in CellRect.CenteredOn(member.SearchOrigin, LandingRadius)
                .ClipInsideMap(member.Pawn.Map))
            {
                if (!IsLandingCellValid(member, cell))
                    continue;
                float anchor = cell.DistanceToSquared(member.SearchAnchor);
                float origin = cell.DistanceToSquared(member.SearchOrigin);
                if (!result.IsValid || anchor < bestAnchor
                    || (anchor == bestAnchor && (origin < bestOrigin
                        || (origin == bestOrigin && (cell.z < result.z
                            || (cell.z == result.z && cell.x < result.x))))))
                {
                    result = cell;
                    bestAnchor = anchor;
                    bestOrigin = origin;
                }
            }
            return result.IsValid;
        }

        internal static void TickGroups()
        {
            var registry = GameComponent_MechanicalFlightRegistry.CurrentRegistry;
            if (registry == null)
                return;
            foreach (var session in registry.GroupFlights.ToArray())
            {
                Pawn? provider = session.Provider;
                if (provider == null || provider.Destroyed || provider.Dead)
                {
                    if (provider?.Dead == true)
                        NotifyProviderDied(provider);
                    else
                        BeginGroupLanding(session, false);
                }
                else if (provider.Downed)
                    HandleDowned(provider);
                else if (!provider.Spawned || provider.Map != session.Map
                    || (!session.Closing && !IsProviding(provider)))
                    BeginGroupLanding(session, false);
                if (session.Closing)
                    continue;
                if (provider?.Drafted != true)
                {
                    BeginGroupLanding(session, false);
                    continue;
                }
                if (GenTicks.TicksGame >= session.NextRangeCheckTick)
                {
                    session.NextRangeCheckTick = GenTicks.TicksGame + 60;
                    ValidateMembers(session);
                }
                SynchronizeFormation(provider);
            }
        }

        private static void ValidateMembers(GroupFlightSession session)
        {
            foreach (var member in session.Members.ToArray())
            {
                Pawn? pawn = member.Pawn;
                if (pawn == null || ReferenceEquals(pawn, session.Provider) || !IsAttached(member))
                    continue;
                if (!pawn.Spawned || pawn.Map != session.Map
                    || pawn.Position.DistanceToSquared(session.Provider!.Position) > Radius * Radius)
                    ReleaseOutOfRange(member);
            }
        }

        private static void ReleaseOutOfRange(GroupFlightMember member)
        {
            if (member.Pawn?.Spawned != true)
            {
                if (GameComponent_MechanicalFlightRegistry.TryGetRecord(member.Pawn, out var record))
                    MechanicalFlightUtility.ClearRuntimeState(record!, true);
                return;
            }
            if (!MechanicalFlightUtility.TryBeginLanding(member.Pawn))
                BeginMemberLanding(member);
        }

        internal static bool CanMoveFormationTo(Pawn? pawn, IntVec3 cell)
        {
            var member = Member(pawn);
            var session = member?.Session;
            if (session == null || !ReferenceEquals(session.Provider, pawn) || session.Closing)
                return true;
            if (pawn?.Map == null)
                return false;
            foreach (var passenger in session.Members)
                if (IsAttached(passenger) && !(cell + passenger.Offset).InBounds(pawn.Map))
                    return false;
            return true;
        }

        internal static void SynchronizeFormation(Pawn provider)
        {
            var session = Member(provider)?.Session;
            if (session == null || session.Closing || session.Provider != provider
                || provider.Map != session.Map || provider.Dead || provider.Downed)
                return;
            bool providerRelocated = session.LastProviderPosition.IsValid
                && provider.Position != session.LastProviderPosition;
            if (providerRelocated)
            {
                provider.pather?.StopDead();
                MechanicalFlightStraightPathPatch.ClearMotion(provider);
            }
            foreach (var member in session.Members.ToArray())
            {
                Pawn? pawn = member.Pawn;
                if (pawn == null || pawn == provider || !IsAttached(member))
                    continue;
                if (!pawn.Spawned || pawn.Map != provider.Map)
                {
                    ReleaseOutOfRange(member);
                    continue;
                }
                // 外部位移保留实际位置；范围内重建偏移，范围外退出，禁止拉回。
                if (pawn.Position != member.ExpectedPosition || providerRelocated)
                {
                    if (pawn.Position.DistanceToSquared(provider.Position) > Radius * Radius)
                    {
                        ReleaseOutOfRange(member);
                        continue;
                    }
                    member.Offset = pawn.Position - provider.Position;
                }
                IntVec3 next = provider.Position + member.Offset;
                if (!next.InBounds(provider.Map))
                {
                    ReleaseOutOfRange(member);
                    continue;
                }
                if (pawn.Position != next)
                {
                    pawn.Position = next;
                    pawn.Drawer.tweener.ResetTweenedPosToRoot();
                }
                member.ExpectedPosition = next;
            }
            session.LastProviderPosition = provider.Position;
        }

        // 正常直线移动与外部传送区分：正常移动前保留原来的偏移。
        internal static void NotifyProviderMoved(Pawn pawn)
        {
            var session = Member(pawn)?.Session;
            if (session?.Provider != pawn || session.Closing)
                return;
            session.LastProviderPosition = pawn.Position;
            SynchronizeFormation(pawn);
        }

        internal static bool TryGetGroundPosition(Pawn pawn, out Vector3 position)
        {
            position = default;
            var member = Member(pawn);
            Pawn? provider = member?.Session?.Provider;
            if (member == null || provider == null || provider == pawn || !IsAttached(member)
                || pawn.Map != provider.Map || pawn.Position != member.ExpectedPosition)
                return false;
            if (!MechanicalFlightStraightPathPatch.TryGetExactGroundDrawPos(provider, out position))
                position = provider.Position.ToVector3Shifted();
            position += member.Offset.ToVector3();
            return true;
        }

        internal static void TickFlight(MechanicalFlightAuthorizationRecord record)
        {
            Pawn pawn = record.Pawn!;
            var member = Member(pawn);
            if (member == null)
            {
                MechanicalFlightEmergencyUtility.Crash(pawn, record);
                return;
            }
            if (record.Phase == MechanicalFlightPhase.Crashing)
                return;
            if (pawn.Downed && !HandleDowned(pawn))
            {
                MechanicalFlightEmergencyUtility.TryCrashFromDowned(pawn);
                return;
            }
            if (ReferenceEquals(member.Session?.Provider, pawn)
                && MechanicalFlightEnergyUtility.TryGetEnergyFraction(pawn, out float energy))
                MechanicalFlightEmergencyUtility.TrySendLowEnergyWarning(pawn, record, energy);
            if (record.IsEmergencySequence)
            {
                MechanicalFlightEmergencyUtility.Tick(record);
                return;
            }
            if (record.Phase == MechanicalFlightPhase.Landing)
            {
                if (pawn.flight?.Flying != true)
                    MechanicalFlightUtility.CompleteNormalLanding(pawn, record);
                return;
            }
            if (pawn.flight?.Flying != true)
            {
                MechanicalFlightUtility.FinalizeRuntimeState(pawn, record);
                return;
            }
            if (record.Phase == MechanicalFlightPhase.TakingOff
                && pawn.flight.PositionOffsetFactor >= 0.999f)
                record.Phase = MechanicalFlightPhase.Hovering;
            if (!pawn.Drafted)
            {
                NotifyUndrafted(pawn);
                if (!record.IsCruising)
                    return;
            }
            MechanicalFlightPresentationUtility.Tick(pawn, record);
            if (!IsProviding(pawn))
                return;
            record.TicksUntilNextEnergyDrain--;
            if (record.TicksUntilNextEnergyDrain <= 0)
            {
                MechanicalFlightEnergyUtility.TryConsumeFlightEnergy(pawn, record.Profile!);
                record.TicksUntilNextEnergyDrain = Math.Max(1, record.Profile!.energyDrainIntervalTicks);
            }
            CheckProviderEnergy(member.Session!);
        }

        private static void CheckProviderEnergy(GroupFlightSession session)
        {
            Pawn? provider = session.Provider;
            if (session.Closing || !GameComponent_MechanicalFlightRegistry.TryGetRecord(provider, out var record)
                || record?.Profile == null
                || !MechanicalFlightEnergyUtility.TryGetEnergyFraction(provider, out float energy))
                return;
            MechanicalFlightEmergencyUtility.TrySendLowEnergyWarning(provider!, record, energy);
            if (energy <= 0f)
                HandleEnergyDepletion(provider!);
            else if (energy < record.Profile.automaticLandingEnergy)
                BeginGroupLanding(session, false);
        }

        internal static void NotifyFlightEnded(MechanicalFlightAuthorizationRecord record)
        {
            var registry = GameComponent_MechanicalFlightRegistry.CurrentRegistry;
            var member = Member(record.Pawn);
            if (registry == null || member?.Session == null)
                return;
            registry.GroupMembers.Remove(record.Pawn!);
            member.Session.Members.Remove(member);
            if (member.Session.Members.Count == 0)
                registry.GroupFlights.Remove(member.Session);
        }

        internal static void RebuildIndex(GameComponent_MechanicalFlightRegistry registry)
        {
            registry.GroupMembers.Clear();
            registry.GroupFlights ??= new List<GroupFlightSession>();
            foreach (var session in registry.GroupFlights.ToArray())
            {
                if (session == null)
                {
                    registry.GroupFlights.Remove(session!);
                    continue;
                }
                session.Members ??= new List<GroupFlightMember>();
                foreach (var member in session.Members.ToArray())
                {
                    if (member?.Pawn == null || member.Pawn.Discarded
                        || registry.GroupMembers.ContainsKey(member.Pawn))
                    {
                        session.Members.Remove(member!);
                        continue;
                    }
                    member.Session = session;
                    registry.GroupMembers[member.Pawn] = member;
                }
                if (session.Members.Count == 0)
                    registry.GroupFlights.Remove(session);
            }
            // 活跃提供者必须属于自己的会话，防止损坏存档形成循环托举。
            foreach (var session in registry.GroupFlights)
                if (!session.Closing && (session.Provider == null
                    || !registry.GroupMembers.TryGetValue(session.Provider, out var provider)
                    || provider.Session != session))
                    session.Closing = true;
        }

        internal static void ReconcileAfterLoad()
        {
            var registry = GameComponent_MechanicalFlightRegistry.CurrentRegistry;
            if (registry == null)
                return;
            foreach (var session in registry.GroupFlights)
                session.NextRangeCheckTick = 0;
            foreach (var member in new List<GroupFlightMember>(registry.GroupMembers.Values))
                if (!GameComponent_MechanicalFlightRegistry.TryGetRecord(member.Pawn, out var record)
                    || record?.IsExternallyPowered != true || !record.IsRuntimeActive)
                {
                    registry.GroupMembers.Remove(member.Pawn!);
                    member.Session!.Members.Remove(member);
                    if (record?.IsExternallyPowered == true)
                        MechanicalFlightUtility.FinalizeRuntimeState(member.Pawn, record);
                }
            registry.GroupFlights.RemoveAll(session => session.Members.Count == 0);
        }

        internal static void ReconcileFlight(MechanicalFlightAuthorizationRecord record)
        {
            Pawn pawn = record.Pawn!;
            var member = Member(pawn);
            if (member == null)
            {
                if (pawn.flight?.Flying == true)
                    MechanicalFlightEmergencyUtility.Crash(pawn, record);
                else
                    MechanicalFlightUtility.FinalizeRuntimeState(pawn, record);
                return;
            }
            if (record.IsEmergencySequence)
                MechanicalFlightEmergencyUtility.ReconcileAfterLoad(record);
            else if (record.Phase == MechanicalFlightPhase.Landing)
            {
                if (pawn.flight?.Flying != true)
                    MechanicalFlightUtility.CompleteNormalLanding(pawn, record);
                else
                    pawn.flight.ForceLand();
            }
            else if (pawn.flight?.Flying != true)
                MechanicalFlightUtility.FinalizeRuntimeState(pawn, record);
            else if (member.Session?.Closing == true)
                BeginMemberLanding(member);
            else
            {
                pawn.pather?.StopDead();
                MechanicalFlightStraightPathPatch.ClearMotion(pawn);
                // 不强制归位；保存时的实际位置变化由首次范围检查处理。
                if (!member.ExpectedPosition.IsValid)
                    member.ExpectedPosition = pawn.Position;
                // StopDead 不会结束等待 PatherArrival 的 Toil；恢复提供者的已保存移动命令。
                if (ReferenceEquals(member.Session?.Provider, pawn)
                    && pawn.Drafted && pawn.CurJobDef == JobDefOf.Goto
                    && pawn.CurJob.targetA.IsValid)
                {
                    var pather = pawn.pather;
                    if (pather != null)
                        MechanicalFlightStraightPathPatch.TryStartDirectPath(
                            pather, pawn, pawn.CurJob.targetA, PathEndMode.OnCell);
                }
            }
        }
    }
}
