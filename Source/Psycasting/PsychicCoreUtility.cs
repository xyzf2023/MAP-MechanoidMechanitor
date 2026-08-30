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

        // Standard reference agreed for the implant:
        // a base 0.50/day meditation gain plus a 0.28 natural-focus reference.
        public const float StandardNaturalMeditationPsyfocusPerDay = 0.78f;

        private static readonly float[] PassivePsyfocusRecoveryMultipliers =
        {
            0f,
            0f,
            0.25f,
            0.50f,
            0.75f,
            1.00f,
            1.25f,
            1.50f,
            2.00f,
            2.50f,
            3.00f
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

        public static float GetPassivePsyfocusRecoveryMultiplier(int level)
        {
            int clampedLevel = Mathf.Clamp(
                level,
                0,
                PassivePsyfocusRecoveryMultipliers.Length - 1);
            return PassivePsyfocusRecoveryMultipliers[clampedLevel];
        }

        public static bool IsPsychicActivationActive(Pawn? pawn)
        {
            HediffDef? activationDef = PsychicActivationHediffDef;
            return pawn?.health?.hediffSet != null
                && activationDef != null
                && pawn.health.hediffSet.HasHediff(activationDef);
        }

        public static float GetPsychicActivationBonusMultiplier(
            Pawn? pawn,
            int coreLevel)
        {
            if (!IsPsychicActivationActive(pawn))
            {
                return 0f;
            }

            float baseMultiplier = GetPassivePsyfocusRecoveryMultiplier(coreLevel);
            return Mathf.Max(0f, Mathf.Max(1f, baseMultiplier * 2f) - baseMultiplier);
        }

        public static float GetTotalPsyfocusRecoveryMultiplier(
            Pawn? pawn,
            int coreLevel)
        {
            return GetPassivePsyfocusRecoveryMultiplier(coreLevel)
                + GetPsychicActivationBonusMultiplier(pawn, coreLevel);
        }

        public static float GetPsyfocusRecoveryPerHour(float multiplier)
        {
            return StandardNaturalMeditationPsyfocusPerDay * multiplier / 24f;
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
