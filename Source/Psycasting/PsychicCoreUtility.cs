using System.Globalization;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class PsychicCoreUtility
    {
        public const string PsychicCoreHediffDefName = "MAP_PsychicCore";
        public const string PsychicActivationHediffDefName = "MAP_PsychicActivationActive";

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
        private static HediffDef? psychicActivationHediffDef;

        public static HediffDef? PsychicCoreHediffDef
        {
            get
            {
                psychicCoreHediffDef ??=
                    DefDatabase<HediffDef>.GetNamedSilentFail(PsychicCoreHediffDefName);
                return psychicCoreHediffDef;
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

        public static bool HasPsychicCore(Pawn? pawn)
        {
            return GetPsychicCore(pawn) != null;
        }

        public static int GetPsychicCoreLevel(Pawn? pawn)
        {
            return GetPsychicCore(pawn)?.level ?? 0;
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
