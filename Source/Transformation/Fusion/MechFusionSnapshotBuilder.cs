using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 从源机械族最终 ThingDef 抽象基础值与合体瞬间的正面 Hediff 阶段
    /// 生成一次性实例快照。快照在本次合体期间固定，不随环境实时重算。
    /// </summary>
    internal static class MechFusionSnapshotBuilder
    {
        private const float Epsilon = 0.0001f;

        private static readonly Dictionary<string, string[]> WorkTypeStatMap =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                { "Doctor", new[] { "MedicalTendSpeed", "MedicalOperationSpeed" } },
                { "Handling", new[] { "AnimalGatherSpeed" } },
                { "Cooking", new[] { "CookSpeed", "ButcheryFleshSpeed" } },
                { "Construction", new[] { "ConstructionSpeed", "SmoothingSpeed" } },
                { "Growing", new[] { "PlantWorkSpeed" } },
                { "PlantCutting", new[] { "PlantWorkSpeed" } },
                { "Mining", new[] { "MiningSpeed", "DeepDrillingSpeed" } },
                {
                    "Crafting",
                    new[]
                    {
                        "SmeltingSpeed", "DrugCookingSpeed",
                        "ButcheryMechanoidSpeed"
                    }
                },
                {
                    "Smithing",
                    new[]
                    {
                        "MechRepairSpeed", "MechFormingSpeed",
                        "SubcoreEncodingSpeed"
                    }
                },
                { "Cleaning", new[] { "CleaningSpeed" } },
                {
                    "Research",
                    new[] { "ResearchSpeed", "HackingSpeed", "DrugSynthesisSpeed" }
                },
                { "Fishing", new[] { "FishingSpeed" } },
                { "Reading", new[] { "ReadingSpeed" } },
                { "GeneralLabor", new[] { "GeneralLaborSpeed" } }
            };

        internal static void Capture(MechFusionSession session, Pawn? source)
        {
            if (session == null || source == null)
            {
                return;
            }

            ThingDef? sourceDef = source.def;
            if (sourceDef == null)
            {
                return;
            }

            Dictionary<StatDef, float> offsets =
                new Dictionary<StatDef, float>();
            Dictionary<StatDef, float> factors =
                new Dictionary<StatDef, float>();

            AddOffset(offsets, StatDefOf.Insulation_Cold, 50f);
            AddOffset(offsets, StatDefOf.Insulation_Heat, 50f);
            AddOffset(offsets, StatDefOf.ToxicEnvironmentResistance, 1f);

            StatDef? vacuumResistance =
                DefDatabase<StatDef>.GetNamedSilentFail("VacuumResistance");
            if (vacuumResistance != null)
            {
                AddOffset(offsets, vacuumResistance, 1f);
            }

            float aimingDelay =
                sourceDef.GetStatValueAbstract(StatDefOf.AimingDelayFactor);
            if (Math.Abs(aimingDelay - 1f) > Epsilon)
            {
                AddOffset(
                    offsets,
                    StatDefOf.AimingDelayFactor,
                    aimingDelay - 1f);
            }

            float armorSharp = sourceDef.GetStatValueAbstract(
                StatDefOf.ArmorRating_Sharp);
            float armorBlunt = sourceDef.GetStatValueAbstract(
                StatDefOf.ArmorRating_Blunt);
            float armorHeat = sourceDef.GetStatValueAbstract(
                StatDefOf.ArmorRating_Heat);

            CapturePositiveHediffStages(source, offsets, factors);

            FoldArmor(
                offsets,
                factors,
                ref armorSharp,
                StatDefOf.ArmorRating_Sharp);
            FoldArmor(
                offsets,
                factors,
                ref armorBlunt,
                StatDefOf.ArmorRating_Blunt);
            FoldArmor(
                offsets,
                factors,
                ref armorHeat,
                StatDefOf.ArmorRating_Heat);

            float moveSpeedBase = sourceDef.GetStatValueAbstract(
                StatDefOf.MoveSpeed);
            BuildWorkSpeedOffsets(sourceDef, offsets);

            session.SetSnapshot(
                ToEntryList(offsets),
                ToEntryList(factors),
                armorSharp,
                armorBlunt,
                armorHeat,
                moveSpeedBase);
            MechFusionWhitelistUtility.CaptureMatches(session, source);
        }

        private static void CapturePositiveHediffStages(
            Pawn source,
            Dictionary<StatDef, float> offsets,
            Dictionary<StatDef, float> factors)
        {
            List<Hediff>? hediffs = source.health?.hediffSet?.hediffs;
            if (hediffs == null)
            {
                return;
            }

            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff? hediff = hediffs[i];
                if (hediff?.def == null || hediff.def.isBad)
                {
                    continue;
                }

                HediffStage? stage = hediff.CurStage;
                if (stage == null)
                {
                    continue;
                }

                float severity = hediff.Severity;
                CaptureStageOffsets(source, stage, severity, offsets);
                CaptureStageFactors(source, stage, severity, factors);
            }
        }

        private static void CaptureStageOffsets(
            Pawn source,
            HediffStage stage,
            float severity,
            Dictionary<StatDef, float> offsets)
        {
            if (stage.statOffsets != null)
            {
                for (int i = 0; i < stage.statOffsets.Count; i++)
                {
                    StatModifier? modifier = stage.statOffsets[i];
                    if (modifier?.stat == null)
                    {
                        continue;
                    }

                    float value = modifier.value;
                    if (stage.statOffsetEffectMultiplier != null)
                    {
                        value *= source.GetStatValue(
                            stage.statOffsetEffectMultiplier);
                    }

                    if (stage.multiplyStatChangesBySeverity)
                    {
                        value *= severity;
                    }

                    if (Math.Abs(value) > Epsilon)
                    {
                        AddOffset(offsets, modifier.stat, value);
                    }
                }
            }

            if (stage.statOffsetsBySeverity == null)
            {
                return;
            }

            for (int i = 0; i < stage.statOffsetsBySeverity.Count; i++)
            {
                StatModifierBySeverity? modifier =
                    stage.statOffsetsBySeverity[i];
                if (modifier?.stat == null || modifier.valueBySeverity == null)
                {
                    continue;
                }

                float value = modifier.valueBySeverity.Evaluate(severity);
                if (Math.Abs(value) > Epsilon)
                {
                    AddOffset(offsets, modifier.stat, value);
                }
            }
        }

        private static void CaptureStageFactors(
            Pawn source,
            HediffStage stage,
            float severity,
            Dictionary<StatDef, float> factors)
        {
            if (stage.statFactors != null)
            {
                for (int i = 0; i < stage.statFactors.Count; i++)
                {
                    StatModifier? modifier = stage.statFactors[i];
                    if (modifier?.stat == null)
                    {
                        continue;
                    }

                    float value = modifier.value;
                    if (stage.statFactorEffectMultiplier != null)
                    {
                        value = StatWorker.ScaleFactor(
                            value,
                            source.GetStatValue(
                                stage.statFactorEffectMultiplier));
                    }

                    if (stage.multiplyStatChangesBySeverity)
                    {
                        value = StatWorker.ScaleFactor(value, severity);
                    }

                    if (Math.Abs(value - 1f) > Epsilon)
                    {
                        AddFactor(factors, modifier.stat, value);
                    }
                }
            }

            if (stage.statFactorsBySeverity == null)
            {
                return;
            }

            for (int i = 0; i < stage.statFactorsBySeverity.Count; i++)
            {
                StatModifierBySeverity? modifier =
                    stage.statFactorsBySeverity[i];
                if (modifier?.stat == null || modifier.valueBySeverity == null)
                {
                    continue;
                }

                float value = modifier.valueBySeverity.Evaluate(severity);
                if (Math.Abs(value - 1f) > Epsilon)
                {
                    AddFactor(factors, modifier.stat, value);
                }
            }
        }

        private static void BuildWorkSpeedOffsets(
            ThingDef def,
            Dictionary<StatDef, float> offsets)
        {
            float workSpeedGlobal = def.GetStatValueAbstract(
                StatDefOf.WorkSpeedGlobal);
            List<WorkTypeDef>? workTypes = def.race?.mechEnabledWorkTypes;
            if (workTypes == null || workTypes.Count == 0)
            {
                AddOffset(offsets, StatDefOf.WorkSpeedGlobal, -0.5f);
                return;
            }

            HashSet<StatDef> mappedStats = new HashSet<StatDef>();
            for (int i = 0; i < workTypes.Count; i++)
            {
                string? workTypeName = workTypes[i]?.defName;
                if (string.IsNullOrEmpty(workTypeName)
                    || !WorkTypeStatMap.TryGetValue(
                        workTypeName!,
                        out string[]? statNames)
                    || statNames == null)
                {
                    continue;
                }

                for (int j = 0; j < statNames.Length; j++)
                {
                    StatDef? stat =
                        DefDatabase<StatDef>.GetNamedSilentFail(statNames[j]);
                    if (stat != null)
                    {
                        mappedStats.Add(stat);
                    }
                }
            }

            if (mappedStats.Count > 0)
            {
                float bonus = Math.Max(0f, workSpeedGlobal - 0.5f);
                if (bonus <= Epsilon)
                {
                    return;
                }

                foreach (StatDef stat in mappedStats)
                {
                    AddOffset(offsets, stat, bonus);
                }

                return;
            }

            float globalBonus = Math.Max(
                0f,
                (workSpeedGlobal - 0.5f) / 2f);
            if (globalBonus > Epsilon)
            {
                AddOffset(offsets, StatDefOf.WorkSpeedGlobal, globalBonus);
            }
        }

        private static void FoldArmor(
            Dictionary<StatDef, float> offsets,
            Dictionary<StatDef, float> factors,
            ref float armorValue,
            StatDef armorStat)
        {
            float offset = offsets.TryGetValue(armorStat, out float existing)
                ? existing
                : 0f;
            float factor = factors.TryGetValue(armorStat, out float existingFactor)
                ? existingFactor
                : 1f;
            armorValue = (armorValue + offset) * factor;
            offsets.Remove(armorStat);
            factors.Remove(armorStat);
        }

        private static void AddOffset(
            Dictionary<StatDef, float> offsets,
            StatDef stat,
            float value)
        {
            if (stat == null || Math.Abs(value) <= Epsilon)
            {
                return;
            }

            offsets[stat] = (offsets.TryGetValue(stat, out float existing)
                ? existing
                : 0f) + value;
        }

        private static void AddFactor(
            Dictionary<StatDef, float> factors,
            StatDef stat,
            float value)
        {
            if (stat == null || Math.Abs(value - 1f) <= Epsilon)
            {
                return;
            }

            factors[stat] = (factors.TryGetValue(stat, out float existing)
                ? existing
                : 1f) * value;
        }

        private static List<MechFusionStatEntry> ToEntryList(
            Dictionary<StatDef, float> values)
        {
            List<MechFusionStatEntry> result =
                new List<MechFusionStatEntry>();
            foreach (KeyValuePair<StatDef, float> pair in values)
            {
                result.Add(new MechFusionStatEntry(pair.Key, pair.Value));
            }

            result.Sort((left, right) => string.CompareOrdinal(
                left.stat?.defName,
                right.stat?.defName));
            return result;
        }
    }
}
