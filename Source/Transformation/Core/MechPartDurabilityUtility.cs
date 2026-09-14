using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 机械体部位耐久的通用换算入口。合体与建筑形态都通过这里按比例
    /// 压低现有部位生命值，避免两套系统产生不同的伤势结算结果。
    /// </summary>
    internal static class MechPartDurabilityUtility
    {
        private const float LossEpsilon = 0.001f;

        private sealed class PlannedPartLoss
        {
            public BodyPartRecord Part = null!;
            public float Loss;
            public bool Critical;
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

            float current = 0f;
            float maximum = 0f;
            for (int i = 0; i < parts.Count; i++)
            {
                BodyPartRecord part = parts[i];
                float partMaximum = part.def.GetMaxHealth(pawn);
                if (partMaximum <= 0f)
                {
                    continue;
                }

                maximum += partMaximum;
                if (!hediffSet.PartIsMissing(part))
                {
                    current += Mathf.Clamp(
                        hediffSet.GetPartHealth(part),
                        0f,
                        partMaximum);
                }
            }

            return maximum > 0f
                ? Mathf.Clamp01(current / maximum)
                : 1f;
        }

        /// <summary>
        /// TargetHP = floor(CurrentPartHP × ratio)。先结算非关键部位，再结算
        /// 核心/意识部位；ratio 为 0 时确保 Pawn 在安全容器中死亡并留下尸体。
        /// </summary>
        internal static void SettleCurrentPartDurability(
            Pawn? source,
            float ratio)
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
                List<PlannedPartLoss> planned = BuildPlannedPartLosses(
                    hediffSet,
                    clampedRatio);
                int unapplied = 0;
                for (int i = 0; i < planned.Count; i++)
                {
                    PlannedPartLoss plan = planned[i];
                    if (hediffSet.PartIsMissing(plan.Part)
                        || hediffSet.GetPartHealth(plan.Part) <= 0f)
                    {
                        continue;
                    }

                    try
                    {
                        ApplyExactPartLoss(source, plan.Part, plan.Loss);
                    }
                    catch (Exception ex)
                    {
                        unapplied++;
                        Log.Error(
                            "[MAP-机械族机械师] 源机械族部位耐久结算失败：" +
                            $"pawn={source.LabelShort}（{source.ThingID}），" +
                            $"part={plan.Part.def?.defName}：{ex}");
                    }
                }

                if (unapplied > 0)
                {
                    Log.Warning(
                        "[MAP-机械族机械师] 源机械族部位耐久结算未能完成全部部位，" +
                        "尸体部位状态需要进游戏确认：" +
                        $"pawn={source.LabelShort}（{source.ThingID}），" +
                        $"unapplied={unapplied}。");
                }
            }

            if (clampedRatio <= 0f && !source.Dead)
            {
                source.Kill(null);
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

        private static List<PlannedPartLoss> BuildPlannedPartLosses(
            HediffSet hediffSet,
            float ratio)
        {
            List<PlannedPartLoss> planned = new List<PlannedPartLoss>();
            List<BodyPartRecord> parts =
                new List<BodyPartRecord>(hediffSet.GetNotMissingParts());
            for (int i = 0; i < parts.Count; i++)
            {
                BodyPartRecord part = parts[i];
                if (hediffSet.PartIsMissing(part))
                {
                    continue;
                }

                float currentHp = hediffSet.GetPartHealth(part);
                if (currentHp <= 0f)
                {
                    continue;
                }

                float targetHp = Mathf.Floor(currentHp * ratio);
                float loss = currentHp - targetHp;
                if (loss <= LossEpsilon)
                {
                    continue;
                }

                planned.Add(new PlannedPartLoss
                {
                    Part = part,
                    Loss = loss,
                    Critical = IsCriticalPart(part)
                });
            }

            planned.Sort((left, right) =>
                left.Critical.CompareTo(right.Critical));
            return planned;
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

        private static void ApplyExactPartLoss(
            Pawn pawn,
            BodyPartRecord part,
            float loss)
        {
            HediffDef? injuryDef = HealthUtility.GetHediffDefFromDamage(
                DamageDefOf.Cut,
                pawn,
                part);
            if (injuryDef == null)
            {
                Log.Warning(
                    "[MAP-机械族机械师] 无法为部位耐久结算找到伤害 Hediff：" +
                    $"pawn={pawn.LabelShort}（{pawn.ThingID}），" +
                    $"part={part.def?.defName}。");
                return;
            }

            Hediff_Injury injury = (Hediff_Injury)HediffMaker.MakeHediff(
                injuryDef,
                pawn,
                part);
            injury.Severity = loss;
            pawn.health.AddHediff(injury, part, null, null);
        }
    }
}
