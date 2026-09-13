using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 结构稳定值的唯一结算入口。数值只保存在权威合体记录中；
    /// 伤害由 AddHediff 路由提供已经过全部护甲的最终伤害，
    /// 解除合体时按稳定值比例精确结算源机械族部位耐久。
    /// </summary>
    internal static class MechFusionStabilityUtility
    {
        internal const float BaseStability = 500f;
        private const float LossEpsilon = 0.001f;

        internal static void CaptureInitialStability(
            MechFusionSession session,
            Pawn source)
        {
            float max = BaseStability
                * GetWeightMultiplier(source)
                * (source?.RaceProps?.baseHealthScale ?? 1f);
            session.SetStability(max, max);
        }

        internal static float GetWeightMultiplier(Pawn? source)
        {
            MechWeightClassDef? weightClass = source?.RaceProps?.mechWeightClass;
            if (weightClass == null)
            {
                return 1f;
            }

            if (weightClass == MechWeightClassDefOf.Light)
            {
                return 0.5f;
            }

            if (weightClass == MechWeightClassDefOf.Medium)
            {
                return 1f;
            }

            if (weightClass == MechWeightClassDefOf.Heavy)
            {
                return 2f;
            }

            if (weightClass == MechWeightClassDefOf.UltraHeavy)
            {
                return 4f;
            }

            return 1f;
        }

        internal static void OnDamageContextClosed(
            MechFusionDamageContext? context)
        {
            if (context?.Session == null)
            {
                return;
            }

            MechFusionSession session = context.Session;
            MechFusionExitReason? reason = context.PendingExitReason;
            if (session.CurrentStability <= 0f
                && (reason == null
                    || MechFusionExitReason.StabilityDepleted.GetPriority()
                        < reason.Value.GetPriority()))
            {
                reason = MechFusionExitReason.StabilityDepleted;
            }

            if (reason == null)
            {
                return;
            }

            MechFusionTeardownService.TryTeardown(
                session,
                reason.Value,
                force: false);
        }

        private sealed class PlannedPartLoss
        {
            public BodyPartRecord Part = null!;
            public float Loss;
            public bool Critical;
        }

        /// <summary>
        /// 解除合体时对源机械族所有仍存在部位施加精确耐久损失：
        /// TargetHP = floor(CurrentPartHP × R)，R = 当前稳定值 / 最大稳定值。
        /// 先一次性计算全部目标，再按“非关键部位优先，可能直接致死的关键部位最后”
        /// 的顺序尽可能完成，绝不因中途死亡而跳过剩余部位的乘算。
        /// 该结算经过 AddHediff 健康入口而不是伤害管线，不会再进入结构路由。
        ///
        /// 任何可能致死的结算都必须在源 Pawn 已恢复到地图、远行队或合法尸体
        /// 容器后执行。后台 WorldPawns 只用于合体期间临时保管真实 Pawn，不能
        /// 在那里触发 Pawn.Kill，否则原版会把世界 Pawn 直接销毁而不留下地图尸体。
        /// </summary>
        internal static void SettleSourcePartDurability(
            MechFusionSession session,
            Pawn? source)
        {
            if (session == null || source == null)
            {
                return;
            }

            if (session.StabilitySettled)
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
                    // 正常死亡后的 Pawn 会处在 Corpse 内并带有 Destroyed 标记；
                    // 已死亡时无需再次施加结构伤害。
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

            float ratio = session.MaxStability > 0f
                ? Mathf.Clamp01(session.CurrentStability / session.MaxStability)
                : 0f;

            HediffSet? hediffSet = source.health?.hediffSet;
            if (hediffSet != null)
            {
                List<PlannedPartLoss> planned = BuildPlannedPartLosses(
                    hediffSet,
                    ratio);

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

            if (ratio <= 0f && !source.Dead)
            {
                source.Kill(null);
            }
        }

        private static bool IsInDeathSafeContainer(Pawn source)
        {
            if (source.Spawned || source.GetCaravan() != null)
            {
                return true;
            }

            return HasValidCorpse(source);
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

            // false 排在 true 前面：非关键部位优先，关键部位最后处理。
            planned.Sort((left, right) => left.Critical.CompareTo(right.Critical));
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
