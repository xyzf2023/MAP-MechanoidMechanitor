using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械体部位耐久的通用换算入口。合体与建筑形态都把形态期间的
    /// 剩余耐久比例交给这里，由这里统一换算为受控的局部部位伤势。
    /// </summary>
    internal static class MechPartDurabilityUtility
    {
        private const float DamagePacketFraction = 0.1f;
        private const float MinimumWeight = 0.0001f;
        private const float InnerPartWeightFactor = 0.35f;
        private const float CriticalPartWeightFactor = 0.25f;
        private const float RepeatedPartWeightFactor = 0.25f;

        private sealed class DamageCandidate
        {
            public BodyPartRecord Part = null!;
            public float CurrentHealth;
            public float DirectCapacity;
            public float Weight;
        }

        /// <summary>
        /// 计算当前全部身体部位生命值相对于完整身体的比例。
        /// 缺失部位按 0 计算；只读取部位生命，不把疾病或能力状态混入结构耐久。
        /// </summary>
        internal static float GetStructuralIntegrity(Pawn? pawn)
        {
            HediffSet? hediffSet = pawn?.health?.hediffSet;
            List<BodyPartRecord>? parts = pawn?.RaceProps?.body?.AllParts;
            if (pawn == null || hediffSet == null || parts == null || parts.Count == 0)
            {
                return 1f;
            }

            float current = GetCurrentStructuralHitPoints(pawn, hediffSet);
            float maximum = 0f;
            for (int i = 0; i < parts.Count; i++)
            {
                float partMaximum = parts[i].def.GetMaxHealth(pawn);
                if (partMaximum > 0f)
                {
                    maximum += partMaximum;
                }
            }

            return maximum > 0f
                ? Mathf.Clamp01(current / maximum)
                : 1f;
        }

        /// <summary>
        /// 将当前结构总量压低到 ratio 对应的目标值。损失被拆成若干伤害包，
        /// 按部位覆盖率、深度与关键程度选择少量承伤部位；不再让全身每个部位
        /// 同时按比例掉血。ratio 大于 0 时保留关键部位至少 1 点生命，
        /// ratio 为 0 时确保 Pawn 在安全容器中死亡并留下尸体。
        /// </summary>
        internal static void SettleCurrentPartDurability(
            Pawn? source,
            float ratio,
            int settlementSeed)
        {
            if (source == null)
            {
                return;
            }

            if (source.Discarded)
            {
                throw new InvalidOperationException(
                    "源机械族已被永久丢弃，无法安全结算结构耐久：" +
                    $"pawn={source.LabelShort}（{source.ThingID}）。");
            }

            if (source.Destroyed)
            {
                if (HasValidCorpse(source))
                {
                    return;
                }

                throw new InvalidOperationException(
                    "源机械族已销毁且不存在合法尸体，无法安全结算结构耐久：" +
                    $"pawn={source.LabelShort}（{source.ThingID}）。");
            }

            if (!IsInDeathSafeContainer(source))
            {
                throw new InvalidOperationException(
                    "源机械族尚未恢复到可安全承载死亡结果的容器，禁止结算结构耐久：" +
                    $"pawn={source.LabelShort}（{source.ThingID}）。");
            }

            float clampedRatio = Mathf.Clamp01(ratio);
            HediffSet? hediffSet = source.health?.hediffSet;
            if (hediffSet != null)
            {
                SettleLocalizedDamage(
                    source,
                    hediffSet,
                    clampedRatio,
                    settlementSeed);
            }

            if (clampedRatio <= 0f && !source.Dead)
            {
                source.Kill(null);
            }
        }

        /// <summary>
        /// 为一次形态事务生成稳定的局部随机种子。使用自管散列，不依赖
        /// string.GetHashCode，也不消耗 Verse.Rand 的全局随机状态。
        /// </summary>
        internal static int CreateSettlementSeed(
            Pawn? source,
            string? contextId)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (source?.thingIDNumber ?? 0);
                if (!string.IsNullOrEmpty(contextId))
                {
                    for (int i = 0; i < contextId!.Length; i++)
                    {
                        hash = hash * 31 + contextId[i];
                    }
                }

                return hash & int.MaxValue;
            }
        }

        internal static int CreateSettlementSeed(
            Pawn? source,
            int contextThingId)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (source?.thingIDNumber ?? 0);
                hash = hash * 31 + contextThingId;
                return hash & int.MaxValue;
            }
        }

        private static void SettleLocalizedDamage(
            Pawn source,
            HediffSet hediffSet,
            float ratio,
            int settlementSeed)
        {
            float initialTotal = GetCurrentStructuralHitPoints(
                source,
                hediffSet);
            int roundedInitial = Mathf.Max(0, Mathf.RoundToInt(initialTotal));
            int targetTotal = Mathf.Clamp(
                Mathf.RoundToInt(roundedInitial * ratio),
                0,
                roundedInitial);
            int remainingLoss = roundedInitial - targetTotal;
            if (remainingLoss <= 0)
            {
                return;
            }

            int packetLimit = Mathf.Max(
                1,
                Mathf.RoundToInt(roundedInitial * DamagePacketFraction));
            List<BodyPartRecord>? allParts = source.RaceProps?.body?.AllParts;
            int partCount = allParts?.Count ?? 0;
            int iterationLimit = Math.Max(32, partCount * 8);
            HashSet<BodyPartRecord> blockedParts =
                new HashSet<BodyPartRecord>();
            Random random = new Random(settlementSeed & int.MaxValue);
            BodyPartRecord? lastPart = null;

            for (int iteration = 0;
                 iteration < iterationLimit
                    && remainingLoss > 0
                    && !source.Dead;
                 iteration++)
            {
                int liveTotal = Mathf.Max(
                    0,
                    Mathf.RoundToInt(
                        GetCurrentStructuralHitPoints(source, hediffSet)));
                remainingLoss = Mathf.Max(0, liveTotal - targetTotal);
                if (remainingLoss <= 0)
                {
                    break;
                }

                List<DamageCandidate> candidates = BuildCandidates(
                    source,
                    hediffSet,
                    ratio > 0f,
                    blockedParts,
                    lastPart);
                if (candidates.Count == 0)
                {
                    break;
                }

                DamageCandidate candidate =
                    SelectCandidate(candidates, random);
                int requestedLoss = Mathf.Min(
                    remainingLoss,
                    Mathf.Min(
                        packetLimit,
                        Mathf.FloorToInt(candidate.DirectCapacity)));
                if (requestedLoss <= 0)
                {
                    blockedParts.Add(candidate.Part);
                    continue;
                }

                bool wouldDestroyPart =
                    requestedLoss >= candidate.CurrentHealth
                    && candidate.Part.def.destroyableByDamage;
                if (wouldDestroyPart)
                {
                    float subtreeCost = GetSubtreeStructuralHitPoints(
                        source,
                        candidate.Part,
                        hediffSet);
                    if (subtreeCost > remainingLoss)
                    {
                        requestedLoss = Mathf.Min(
                            requestedLoss,
                            Mathf.FloorToInt(candidate.CurrentHealth - 1f));
                    }
                }

                if (requestedLoss <= 0)
                {
                    blockedParts.Add(candidate.Part);
                    continue;
                }

                float beforeTotal = GetCurrentStructuralHitPoints(
                    source,
                    hediffSet);
                if (!TryApplyExactPartLoss(
                        source,
                        candidate.Part,
                        requestedLoss,
                        out string? failureReason))
                {
                    blockedParts.Add(candidate.Part);
                    Log.Warning(
                        "[MAP-机械族机械师] 局部结构伤害无法写入候选部位，" +
                        "本次结算将改用其他部位：" +
                        $"pawn={source.LabelShort}（{source.ThingID}），" +
                        $"part={candidate.Part.def?.defName}，" +
                        $"reason={failureReason ?? "未知"}。");
                    continue;
                }

                float afterTotal = GetCurrentStructuralHitPoints(
                    source,
                    hediffSet);
                int actualLoss = Mathf.Max(
                    0,
                    Mathf.RoundToInt(beforeTotal - afterTotal));
                if (actualLoss <= 0)
                {
                    blockedParts.Add(candidate.Part);
                    Log.Warning(
                        "[MAP-机械族机械师] 局部结构伤害写入后未降低实际部位耐久，" +
                        "本次结算将改用其他部位：" +
                        $"pawn={source.LabelShort}（{source.ThingID}），" +
                        $"part={candidate.Part.def?.defName}。");
                    continue;
                }

                int currentTotal = Mathf.Max(
                    0,
                    Mathf.RoundToInt(afterTotal));
                remainingLoss = Mathf.Max(0, currentTotal - targetTotal);
                lastPart = candidate.Part;
            }

            int finalTotal = Mathf.Max(
                0,
                Mathf.RoundToInt(
                    GetCurrentStructuralHitPoints(source, hediffSet)));
            remainingLoss = Mathf.Max(0, finalTotal - targetTotal);
            if (remainingLoss > 0 && ratio > 0f && !source.Dead)
            {
                Log.Warning(
                    "[MAP-机械族机械师] 局部结构伤害未能完全达到目标耐久；" +
                    "已保留关键部位最低生命并停止结算：" +
                    $"pawn={source.LabelShort}（{source.ThingID}），" +
                    $"target={targetTotal}，remaining={remainingLoss}。");
            }
        }

        private static List<DamageCandidate> BuildCandidates(
            Pawn pawn,
            HediffSet hediffSet,
            bool survivalRequired,
            HashSet<BodyPartRecord> blockedParts,
            BodyPartRecord? lastPart)
        {
            List<DamageCandidate> result = new List<DamageCandidate>();
            List<BodyPartRecord>? parts = pawn.RaceProps?.body?.AllParts;
            if (parts == null)
            {
                return result;
            }

            for (int i = 0; i < parts.Count; i++)
            {
                BodyPartRecord part = parts[i];
                if (blockedParts.Contains(part)
                    || hediffSet.PartIsMissing(part))
                {
                    continue;
                }

                float currentHealth = hediffSet.GetPartHealth(part);
                if (currentHealth <= 0f)
                {
                    continue;
                }

                bool preserveAtOne =
                    !part.def.destroyableByDamage
                    || (survivalRequired && IsCriticalPart(part));
                float directCapacity = currentHealth
                    - (preserveAtOne ? 1f : 0f);
                if (directCapacity < 1f)
                {
                    continue;
                }

                float weight = Mathf.Max(
                        part.coverageAbs,
                        MinimumWeight)
                    * Mathf.Max(currentHealth, 1f);
                if (part.depth == BodyPartDepth.Inside)
                {
                    weight *= InnerPartWeightFactor;
                }

                if (IsCriticalPart(part))
                {
                    weight *= CriticalPartWeightFactor;
                }

                if (ReferenceEquals(part, lastPart) && parts.Count > 1)
                {
                    weight *= RepeatedPartWeightFactor;
                }

                result.Add(new DamageCandidate
                {
                    Part = part,
                    CurrentHealth = currentHealth,
                    DirectCapacity = directCapacity,
                    Weight = Mathf.Max(weight, MinimumWeight)
                });
            }

            return result;
        }

        private static DamageCandidate SelectCandidate(
            List<DamageCandidate> candidates,
            Random random)
        {
            double totalWeight = 0d;
            for (int i = 0; i < candidates.Count; i++)
            {
                totalWeight += candidates[i].Weight;
            }

            if (totalWeight <= 0d)
            {
                return candidates[random.Next(candidates.Count)];
            }

            double target = random.NextDouble() * totalWeight;
            double accumulated = 0d;
            for (int i = 0; i < candidates.Count; i++)
            {
                accumulated += candidates[i].Weight;
                if (target <= accumulated)
                {
                    return candidates[i];
                }
            }

            return candidates[candidates.Count - 1];
        }

        private static float GetCurrentStructuralHitPoints(
            Pawn pawn,
            HediffSet hediffSet)
        {
            List<BodyPartRecord>? parts = pawn.RaceProps?.body?.AllParts;
            if (parts == null)
            {
                return 0f;
            }

            float total = 0f;
            for (int i = 0; i < parts.Count; i++)
            {
                BodyPartRecord part = parts[i];
                float maximum = part.def.GetMaxHealth(pawn);
                if (hediffSet.PartIsMissing(part) || maximum <= 0f)
                {
                    continue;
                }

                total += Mathf.Clamp(
                    hediffSet.GetPartHealth(part),
                    0f,
                    maximum);
            }

            return total;
        }

        private static float GetSubtreeStructuralHitPoints(
            Pawn pawn,
            BodyPartRecord root,
            HediffSet hediffSet)
        {
            float total = 0f;
            foreach (BodyPartRecord part in root.GetPartAndAllChildParts())
            {
                float maximum = part.def.GetMaxHealth(pawn);
                if (!hediffSet.PartIsMissing(part) && maximum > 0f)
                {
                    total += Mathf.Clamp(
                        hediffSet.GetPartHealth(part),
                        0f,
                        maximum);
                }
            }

            return total;
        }

        private static bool IsCriticalPart(BodyPartRecord part)
        {
            if (part.IsCorePart)
            {
                return true;
            }

            List<BodyPartTagDef>? tags = part.def?.tags;
            return tags != null
                && tags.Contains(BodyPartTagDefOf.ConsciousnessSource);
        }

        private static bool TryApplyExactPartLoss(
            Pawn pawn,
            BodyPartRecord part,
            float loss,
            out string? failureReason)
        {
            failureReason = null;
            HediffDef? injuryDef = HealthUtility.GetHediffDefFromDamage(
                DamageDefOf.Cut,
                pawn,
                part);
            if (injuryDef == null)
            {
                failureReason = "找不到对应的机械伤势定义";
                return false;
            }

            Hediff hediff = HediffMaker.MakeHediff(
                injuryDef,
                pawn,
                part);
            if (hediff is not Hediff_Injury injury)
            {
                failureReason =
                    $"伤势定义 {injuryDef.defName} 不是 Hediff_Injury";
                return false;
            }

            try
            {
                injury.Severity = loss;
                pawn.health.AddHediff(injury, part, null, null);
                return true;
            }
            catch (Exception ex)
            {
                failureReason = ex.ToString();
                return false;
            }
        }

        private static bool IsInDeathSafeContainer(Pawn source)
        {
            return source.Spawned
                || source.GetCaravan() != null
                || HasValidCorpse(source);
        }

        private static bool HasValidCorpse(Pawn source)
        {
            Corpse? corpse = source.Corpse;
            return corpse != null
                && !corpse.Destroyed
                && (corpse.Spawned || corpse.ParentHolder != null);
        }
    }
}
