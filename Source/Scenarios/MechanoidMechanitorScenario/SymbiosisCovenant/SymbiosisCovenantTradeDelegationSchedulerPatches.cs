using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    internal sealed class SymbiosisCovenantTradeDelegationScheduleState
    {
        public SymbiosisCovenantTradeDelegationScheduleState()
        {
        }

        public int nextTick = -1;
        public int retryCount;
        public Faction? lastLeadFaction;
    }

    public static class SymbiosisCovenantTradeDelegationScheduler
    {
        private const int SchedulerIntervalTicks = 2500;
        private const int TicksPerDay = 60000;

        private static readonly ConditionalWeakTable<
            GameComponent_SymbiosisCovenantState,
            SymbiosisCovenantTradeDelegationScheduleState> States =
                new ConditionalWeakTable<
                    GameComponent_SymbiosisCovenantState,
                    SymbiosisCovenantTradeDelegationScheduleState>();

        private static SymbiosisCovenantTradeDelegationScheduleState GetState(
            GameComponent_SymbiosisCovenantState component)
        {
            return States.GetOrCreateValue(component);
        }

        public static Faction? GetLastLeadFaction(GameComponent_SymbiosisCovenantState component)
        {
            return GetState(component).lastLeadFaction;
        }

        public static void NotifyLeadUsed(
            GameComponent_SymbiosisCovenantState component,
            Faction leadFaction)
        {
            GetState(component).lastLeadFaction = leadFaction;
        }

        public static int GetNextTick(GameComponent_SymbiosisCovenantState component)
        {
            return GetState(component).nextTick;
        }

        public static int GetRetryCount(GameComponent_SymbiosisCovenantState component)
        {
            return GetState(component).retryCount;
        }

        public static float GetDaysUntilNext(GameComponent_SymbiosisCovenantState component)
        {
            int next = GetState(component).nextTick;
            if (next < 0 || Find.TickManager == null)
            {
                return -1f;
            }
            return Math.Max(0f, (next - Find.TickManager.TicksGame) / (float)TicksPerDay);
        }

        public static void HandleLevelRecalculated(GameComponent_SymbiosisCovenantState component)
        {
            SymbiosisCovenantTradeDelegationScheduleState schedule = GetState(component);
            if (!SymbiosisCovenantTradeDelegationUtility.IsAvailableNow(
                    component,
                    out SymbiosisCovenantDelegationLevelSettings? settings)
                || settings == null)
            {
                CancelPending(schedule);
                return;
            }

            if (schedule.nextTick < 0 && Find.TickManager != null)
            {
                ScheduleFullInterval(component, schedule, settings, Find.TickManager.TicksGame);
            }
        }

        public static void Tick(GameComponent_SymbiosisCovenantState component)
        {
            if (Find.TickManager == null
                || Find.TickManager.TicksGame % SchedulerIntervalTicks != 0)
            {
                return;
            }

            SymbiosisCovenantTradeDelegationScheduleState schedule = GetState(component);
            if (!SymbiosisCovenantTradeDelegationUtility.IsAvailableNow(
                    component,
                    out SymbiosisCovenantDelegationLevelSettings? settings)
                || settings == null)
            {
                CancelPending(schedule);
                return;
            }

            int now = Find.TickManager.TicksGame;
            if (schedule.nextTick < 0)
            {
                ScheduleFullInterval(component, schedule, settings, now);
                return;
            }

            if (now < schedule.nextTick)
            {
                return;
            }

            if (SymbiosisCovenantTradeDelegationUtility.TryExecuteOnAnyEligibleMap(
                    forced: false))
            {
                schedule.retryCount = 0;
                ScheduleFullInterval(component, schedule, settings, now);
                return;
            }

            SymbiosisCovenantTradeDelegationDef config =
                SymbiosisCovenantTradeDelegationDefOf.MAP_SymbiosisCovenant_TradeDelegationConfig;
            schedule.retryCount++;
            if (schedule.retryCount <= config.maxShortRetries)
            {
                schedule.nextTick = now + Math.Max(1, config.retryDelayTicks);
            }
            else
            {
                schedule.retryCount = 0;
                ScheduleFullInterval(component, schedule, settings, now);
            }
        }

        public static void ExposeData(GameComponent_SymbiosisCovenantState component)
        {
            SymbiosisCovenantTradeDelegationScheduleState schedule = GetState(component);
            Scribe_Values.Look(
                ref schedule.nextTick,
                "symbiosisCovenantNextTradeDelegationTick",
                -1);
            Scribe_Values.Look(
                ref schedule.retryCount,
                "symbiosisCovenantTradeDelegationRetryCount",
                0);
            Scribe_References.Look(
                ref schedule.lastLeadFaction,
                "symbiosisCovenantLastTradeDelegationLeadFaction");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (schedule.nextTick < -1)
                {
                    schedule.nextTick = -1;
                }
                schedule.retryCount = Math.Max(0, schedule.retryCount);
                if (schedule.lastLeadFaction != null && schedule.lastLeadFaction.defeated)
                {
                    schedule.lastLeadFaction = null;
                }
            }
        }

        public static bool DevSpawnNow()
        {
            if (!Prefs.DevMode)
            {
                return false;
            }
            return SymbiosisCovenantTradeDelegationUtility.TryExecuteOnMap(
                Find.CurrentMap,
                forced: true);
        }

        public static bool DevReschedule()
        {
            if (!Prefs.DevMode || Find.TickManager == null)
            {
                return false;
            }

            GameComponent_SymbiosisCovenantState? component =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (component == null
                || !SymbiosisCovenantTradeDelegationUtility.IsAvailableNow(
                    component,
                    out SymbiosisCovenantDelegationLevelSettings? settings)
                || settings == null)
            {
                return false;
            }

            SymbiosisCovenantTradeDelegationScheduleState schedule = GetState(component);
            schedule.retryCount = 0;
            ScheduleFullInterval(component, schedule, settings, Find.TickManager.TicksGame);
            return true;
        }

        public static bool DevMakeDueNow()
        {
            if (!Prefs.DevMode || Find.TickManager == null)
            {
                return false;
            }

            GameComponent_SymbiosisCovenantState? component =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (component == null
                || !SymbiosisCovenantTradeDelegationUtility.IsAvailableNow(component, out _))
            {
                return false;
            }

            SymbiosisCovenantTradeDelegationScheduleState schedule = GetState(component);
            schedule.nextTick = Find.TickManager.TicksGame;
            schedule.retryCount = 0;
            return true;
        }

        // M3：联合贸易代表团 DEV 实时状态快照。
        public static SymbiosisCovenantTradeDelegationDevSnapshot? GetDevSnapshot(
            GameComponent_SymbiosisCovenantState component)
        {
            if (!SymbiosisCovenantTradeDelegationUtility.IsAvailableNow(
                    component,
                    out SymbiosisCovenantDelegationLevelSettings? settings)
                || settings == null)
            {
                return null;
            }

            SymbiosisCovenantTradeDelegationDef config =
                SymbiosisCovenantTradeDelegationDefOf.MAP_SymbiosisCovenant_TradeDelegationConfig;

            return new SymbiosisCovenantTradeDelegationDevSnapshot
            {
                CurrentLevel = component.CovenantLevel,
                MemberCount = component.CovenantMemberCount,
                BaseIntervalDays = settings.intervalDays,
                MemberSpeedMultiplier = config.GetMemberFrequencyMultiplier(
                    component.CovenantMemberCount),
                NextTick = GetNextTick(component),
                DaysUntilNext = GetDaysUntilNext(component),
                RetryCount = GetRetryCount(component),
                LastLeadFaction = GetLastLeadFaction(component)
            };
        }

        private static void CancelPending(SymbiosisCovenantTradeDelegationScheduleState schedule)
        {
            schedule.nextTick = -1;
            schedule.retryCount = 0;
        }

        private static void ScheduleFullInterval(
            GameComponent_SymbiosisCovenantState component,
            SymbiosisCovenantTradeDelegationScheduleState schedule,
            SymbiosisCovenantDelegationLevelSettings settings,
            int now)
        {
            SymbiosisCovenantTradeDelegationDef config =
                SymbiosisCovenantTradeDelegationDefOf.MAP_SymbiosisCovenant_TradeDelegationConfig;
            float baseDays = settings.intervalDays.RandomInRange;
            float speedMultiplier = config.GetMemberFrequencyMultiplier(component.CovenantMemberCount);
            float actualDays = Math.Max(
                config.minimumIntervalDays,
                baseDays / Math.Max(0.01f, speedMultiplier));
            schedule.nextTick = now + Math.Max(1, Mathf.RoundToInt(actualDays * TicksPerDay));
            schedule.retryCount = 0;
        }
    }

    [HarmonyPatch(typeof(GameComponent_SymbiosisCovenantState), "RecalculateCovenantLevel")]
    public static class SymbiosisCovenantTradeDelegationLevelRecalculatedPatch
    {
        public static void Postfix(GameComponent_SymbiosisCovenantState __instance)
        {
            SymbiosisCovenantTradeDelegationScheduler.HandleLevelRecalculated(__instance);
        }
    }

    [HarmonyPatch(typeof(GameComponent_SymbiosisCovenantState), nameof(GameComponent_SymbiosisCovenantState.GameComponentTick))]
    public static class SymbiosisCovenantTradeDelegationGameComponentTickPatch
    {
        public static void Postfix(GameComponent_SymbiosisCovenantState __instance)
        {
            SymbiosisCovenantTradeDelegationScheduler.Tick(__instance);
        }
    }

    [HarmonyPatch(typeof(GameComponent_SymbiosisCovenantState), nameof(GameComponent_SymbiosisCovenantState.ExposeData))]
    public static class SymbiosisCovenantTradeDelegationExposeDataPatch
    {
        public static void Postfix(GameComponent_SymbiosisCovenantState __instance)
        {
            SymbiosisCovenantTradeDelegationScheduler.ExposeData(__instance);
        }
    }

    // M3：联合贸易代表团 DEV 状态快照数据结构。
    public sealed class SymbiosisCovenantTradeDelegationDevSnapshot
    {
        public int CurrentLevel;
        public int MemberCount;
        public FloatRange BaseIntervalDays;
        public float MemberSpeedMultiplier;
        public int NextTick;
        public float DaysUntilNext;
        public int RetryCount;
        public Faction? LastLeadFaction;
    }
}
