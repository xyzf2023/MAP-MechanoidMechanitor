using System.Collections.Generic;
using System.Globalization;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public static class PsychicCoreUtility
    {
        public const string PsychicCoreHediffDefName = "MAP_PsychicCore";
        public const string PsychicActivationHediffDefName = "MAP_PsychicActivationActive";
        public const string PsychicActivationAbilityDefName = "MAP_PsychicActivation";
        public const int MaxPsychicCoreLevel = 10;

        internal const int PsyfocusRecoverySettlementTicks = 60;

        // 心灵中枢基础精神力恢复量表（按等级索引）：数值为「每小时」恢复的精神力条
        // 占比（0~1 口径）。索引 0 不受调用（等级从 1 起）；索引 1 即 1 级固定为 0；
        // 最高等级 10 对应 10%/小时。
        private static readonly float[] PassivePsyfocusRecoveryPerHour =
        {
            0f,
            0f,
            0.01f,
            0.02f,
            0.03f,
            0.04f,
            0.05f,
            0.06f,
            0.07f,
            0.08f,
            0.10f
        };

        private static HediffDef? psychicCoreHediffDef;
        private static HediffDef? psychicSynchronizationHediffDef;
        private static HediffDef? psychicActivationHediffDef;
        private static AbilityDef? psychicActivationAbilityDef;

        public static HediffDef? PsychicCoreHediffDef
        {
            get
            {
                psychicCoreHediffDef ??=
                    DefDatabase<HediffDef>.GetNamedSilentFail(PsychicCoreHediffDefName);
                return psychicCoreHediffDef;
            }
        }

        public static HediffDef? PsychicSynchronizationHediffDef
        {
            get
            {
                psychicSynchronizationHediffDef ??=
                    DefDatabase<HediffDef>.GetNamedSilentFail(
                        MechFusionDefNames.PsychicSynchronizationHediffDefName);
                return psychicSynchronizationHediffDef;
            }
        }

        public static HediffDef? PsychicActivationHediffDef
        {
            get
            {
                psychicActivationHediffDef ??=
                    DefDatabase<HediffDef>.GetNamedSilentFail(
                        PsychicActivationHediffDefName);
                return psychicActivationHediffDef;
            }
        }

        public static AbilityDef? PsychicActivationAbilityDef
        {
            get
            {
                psychicActivationAbilityDef ??=
                    DefDatabase<AbilityDef>.GetNamedSilentFail(
                        PsychicActivationAbilityDefName);
                return psychicActivationAbilityDef;
            }
        }

        public static Hediff_PsychicCore? GetPsychicCore(Pawn? pawn)
        {
            HediffDef? coreDef = PsychicCoreHediffDef;
            if (pawn?.health?.hediffSet == null || coreDef == null)
            {
                return null;
            }

            return pawn.health.hediffSet.GetFirstHediffOfDef(coreDef)
                as Hediff_PsychicCore;
        }

        public static Hediff_PsychicSynchronization? GetPsychicSynchronization(
            Pawn? pawn)
        {
            HediffDef? synchronizationDef = PsychicSynchronizationHediffDef;
            if (pawn?.health?.hediffSet == null || synchronizationDef == null)
            {
                return null;
            }

            return pawn.health.hediffSet.GetFirstHediffOfDef(synchronizationDef)
                as Hediff_PsychicSynchronization;
        }

        public static bool HasPsychicCore(Pawn? pawn)
        {
            return GetPsychicCore(pawn) != null;
        }

        public static bool HasPsychicCoreEffect(Pawn? pawn)
        {
            return GetPsychicCore(pawn) != null
                || GetPsychicSynchronization(pawn) != null;
        }

        public static int GetPsychicCoreLevel(Pawn? pawn)
        {
            return GetPsychicCore(pawn)?.level ?? 0;
        }

        public static int GetPsychicSynchronizationLevel(Pawn? pawn)
        {
            return GetPsychicSynchronization(pawn)?.MappedLevel ?? 0;
        }

        public static int GetEffectivePsychicCoreLevel(Pawn? pawn)
        {
            return Mathf.Max(
                GetPsychicCoreLevel(pawn),
                GetPsychicSynchronizationLevel(pawn));
        }

        public static float GetPassivePsyfocusRecoveryPerHour(int level)
        {
            int clampedLevel = Mathf.Clamp(
                level,
                0,
                PassivePsyfocusRecoveryPerHour.Length - 1);
            return PassivePsyfocusRecoveryPerHour[clampedLevel];
        }

        public static bool IsPsychicActivationActive(Pawn? pawn)
        {
            HediffDef? activationDef = PsychicActivationHediffDef;
            return pawn?.health?.hediffSet != null
                && activationDef != null
                && pawn.health.hediffSet.HasHediff(activationDef);
        }

        public static float GetPsychicActivationBonusPerHour(
            Pawn? pawn,
            int coreLevel)
        {
            if (!IsPsychicActivationActive(pawn))
            {
                return 0f;
            }

            float basePerHour = GetPassivePsyfocusRecoveryPerHour(coreLevel);
            return Mathf.Max(0f, Mathf.Max(0.03f, basePerHour * 2f) - basePerHour);
        }

        public static float GetTotalPsyfocusRecoveryPerHour(
            Pawn? pawn,
            int coreLevel)
        {
            return GetPassivePsyfocusRecoveryPerHour(coreLevel)
                + GetPsychicActivationBonusPerHour(pawn, coreLevel);
        }

        public static string FormatPsyfocusPercent(float fraction)
        {
            return (fraction * 100f).ToString("0.####", CultureInfo.CurrentCulture) + "%";
        }

        /// <summary>
        /// 心灵中枢与灵能同调共用的 C# 效果只允许一个 Hediff 执行。
        /// 等级更高者执行；同级固定由真实心灵中枢执行。
        /// </summary>
        public static bool IsRuntimeEffectExecutor(Hediff? effectSource)
        {
            Pawn? pawn = effectSource?.pawn;
            if (pawn == null)
            {
                return false;
            }

            Hediff_PsychicCore? core = GetPsychicCore(pawn);
            Hediff_PsychicSynchronization? synchronization =
                GetPsychicSynchronization(pawn);
            if (core == null)
            {
                return object.ReferenceEquals(effectSource, synchronization);
            }

            if (synchronization == null)
            {
                return object.ReferenceEquals(effectSource, core);
            }

            return core.level >= synchronization.MappedLevel
                ? object.ReferenceEquals(effectSource, core)
                : object.ReferenceEquals(effectSource, synchronization);
        }

        /// <summary>
        /// 统一结算两种健康状态共用的 C# 效果。Def 中的 statOffsets/statFactors
        /// 不经过这里，仍由游戏原生 Stat 系统分别计算并允许叠加。
        /// </summary>
        public static void TickRuntimeEffects(
            Hediff? effectSource,
            ref int pendingPsyfocusRecoveryTicks,
            int delta)
        {
            Pawn? pawn = effectSource?.pawn;
            if (pawn == null
                || pawn.Destroyed
                || pawn.Dead
                || !IsRuntimeEffectExecutor(effectSource))
            {
                pendingPsyfocusRecoveryTicks = 0;
                return;
            }

            int effectiveLevel = GetEffectivePsychicCoreLevel(pawn);
            if (effectiveLevel <= 0)
            {
                pendingPsyfocusRecoveryTicks = 0;
                return;
            }

            if (pawn.IsHashIntervalTick(600, delta))
            {
                // 同时承担低频自修复：旧存档能力列表异常或第三方移除能力后，
                // 只根据已冻结的授权事实恢复，不重新检查当前启灵神经。
                SyncPsychicActivationAbility(pawn);
                TryRecoverMentalStateAtMaxLevel(pawn, effectiveLevel);
            }

            if (delta > 0)
            {
                pendingPsyfocusRecoveryTicks += delta;
            }

            if (pendingPsyfocusRecoveryTicks < PsyfocusRecoverySettlementTicks)
            {
                return;
            }

            // 结算前重新校验：第三方状态结束回调可能改变角色状态与灵能基础设施。
            if (pawn.Destroyed
                || pawn.Dead
                || pawn.health?.hediffSet == null
                || effectSource == null
                || !pawn.health.hediffSet.hediffs.Contains(effectSource)
                || pawn.psychicEntropy == null
                || !pawn.HasPsylink)
            {
                pendingPsyfocusRecoveryTicks = 0;
                return;
            }

            float totalPerHour =
                GetTotalPsyfocusRecoveryPerHour(pawn, effectiveLevel);
            if (totalPerHour <= 0f)
            {
                pendingPsyfocusRecoveryTicks = 0;
                return;
            }

            int elapsedTicks = pendingPsyfocusRecoveryTicks;
            pendingPsyfocusRecoveryTicks = 0;
            float offset =
                totalPerHour * elapsedTicks / GenDate.TicksPerHour;
            pawn.psychicEntropy.OffsetPsyfocusDirectly(offset);
        }

        public static bool HasAuthorizedPsychicActivationSource(Pawn? pawn)
        {
            if (HasPsychicCore(pawn))
            {
                return true;
            }

            return HasAuthorizedPsychicSynchronization(pawn);
        }

        public static void SyncPsychicActivationAbility(Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return;
            }

            AbilityDef? abilityDef = PsychicActivationAbilityDef;
            bool shouldHave = HasAuthorizedPsychicActivationSource(pawn);
            if (shouldHave)
            {
                if (abilityDef == null)
                {
                    return;
                }

                pawn.abilities ??= new Pawn_AbilityTracker(pawn);
                pawn.abilities.GainAbility(abilityDef);
                return;
            }

            if (abilityDef != null)
            {
                pawn.abilities?.RemoveAbility(abilityDef);
            }

            ClearPsychicActivation(pawn);
        }

        public static void ClearExistingDisruptorFlash(Pawn? pawn)
        {
            if (pawn?.health?.hediffSet == null || HediffDefOf.DisruptorFlash == null)
            {
                return;
            }

            Hediff? existing =
                pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.DisruptorFlash);
            while (existing != null)
            {
                pawn.health.RemoveHediff(existing);
                existing =
                    pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.DisruptorFlash);
            }
        }

        public static void TryGainPsylinkLevel(Pawn? pawn)
        {
            if (!ModsConfig.RoyaltyActive
                || pawn?.health?.hediffSet == null
                || pawn.Destroyed
                || pawn.Dead)
            {
                return;
            }

            Hediff_Psylink? psylink = pawn.GetMainPsylinkSource();
            if (psylink != null && psylink.level >= psylink.def.maxSeverity)
            {
                return;
            }

            if (pawn.RaceProps.IsMechanoid)
            {
                MechanoidMechanitorPsycastUtility.EnsurePsycastInfrastructure(pawn);
            }
            else
            {
                pawn.abilities ??= new Pawn_AbilityTracker(pawn);
                if (pawn.psychicEntropy == null)
                {
                    pawn.psychicEntropy = new Pawn_PsychicEntropyTracker(pawn);
                    pawn.psychicEntropy.SetInitialPsyfocusLevel();
                }
            }

            psylink = pawn.GetMainPsylinkSource();
            if (psylink != null)
            {
                psylink.ChangeLevel(1);
                return;
            }

            BodyPartRecord? installPart = ResolvePsylinkInstallPart(pawn);
            if (installPart == null)
            {
                Log.Warning(
                    "[MAP-机械族机械师] 心灵中枢无法为 " + pawn
                    + " 添加启灵神经：未找到意识来源/大脑部位。");
                return;
            }

            pawn.health.AddHediff(HediffDefOf.PsychicAmplifier, installPart);
        }

        private static bool HasAuthorizedPsychicSynchronization(Pawn? pawn)
        {
            if (GetPsychicSynchronization(pawn) == null
                || !GameComponent_MechFusionSessionRegistry.TryGetSessionForWearer(
                    pawn,
                    out MechFusionSession? session)
                || session == null)
            {
                return false;
            }

            IReadOnlyList<MechFusionHealthEffectEntry> entries =
                session.HealthEffectEntries;
            for (int i = 0; i < entries.Count; i++)
            {
                MechFusionHealthEffectEntry? entry = entries[i];
                if (entry?.ruleId ==
                    MechFusionHealthEffectRegistry.PsychicSynchronizationRuleId)
                {
                    return entry.psychicActivationAuthorizationCaptured
                        && entry.psychicActivationAuthorized;
                }
            }

            return false;
        }

        private static void TryRecoverMentalStateAtMaxLevel(
            Pawn pawn,
            int effectiveLevel)
        {
            if (MAPMechanitorMod.Settings == null
                || !MAPMechanitorMod.Settings
                    .enableMaxLevelPsychicCoreMentalStateRecovery
                || effectiveLevel < MaxPsychicCoreLevel)
            {
                return;
            }

            MentalState? mentalState =
                pawn.mindState?.mentalStateHandler?.CurState;
            mentalState?.RecoverFromState();
        }

        private static void ClearPsychicActivation(Pawn? pawn)
        {
            HediffDef? activationDef = PsychicActivationHediffDef;
            if (pawn?.health?.hediffSet == null || activationDef == null)
            {
                return;
            }

            Hediff? active = pawn.health.hediffSet.GetFirstHediffOfDef(activationDef);
            while (active != null)
            {
                pawn.health.RemoveHediff(active);
                active = pawn.health.hediffSet.GetFirstHediffOfDef(activationDef);
            }
        }

        private static BodyPartRecord? ResolvePsylinkInstallPart(Pawn pawn)
        {
            BodyPartRecord? consciousnessSource =
                MechanoidMechanitorImplantUtility.GetPrimaryConsciousnessSourcePart(pawn);
            if (consciousnessSource != null)
            {
                return consciousnessSource;
            }

            BodyPartDef? brainDef = DefDatabase<BodyPartDef>.GetNamedSilentFail("Brain");
            if (brainDef == null)
            {
                return null;
            }

            return pawn.RaceProps.body.GetPartsWithDef(brainDef).FirstOrFallback();
        }
    }
}
