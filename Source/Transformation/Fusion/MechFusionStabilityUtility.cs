using System.Collections.Generic;
using RimWorld;
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

            if (context.Session.CurrentStability <= 0f)
            {
                MechFusionTeardownService.TryTeardown(
                    context.Session,
                    MechFusionExitReason.StabilityDepleted,
                    force: false);
            }
        }

        /// <summary>
        /// 解除合体时对源机械族所有仍存在部位施加精确耐久损失：
        /// TargetHP = floor(CurrentPartHP × R)，R = 当前稳定值 / 最大稳定值。
        /// 该结算经过内部健康入口而不是伤害管线，不会再进入结构路由。
        /// </summary>
        internal static void SettleSourcePartDurability(
            MechFusionSession session,
            Pawn? source)
        {
            if (session == null
                || source == null
                || source.Destroyed
                || source.Discarded)
            {
                return;
            }

            float ratio = session.MaxStability > 0f
                ? Mathf.Clamp01(session.CurrentStability / session.MaxStability)
                : 0f;

            HediffSet? hediffSet = source.health?.hediffSet;
            if (hediffSet != null && !source.Dead)
            {
                List<BodyPartRecord> parts =
                    new List<BodyPartRecord>(hediffSet.GetNotMissingParts());
                for (int i = 0; i < parts.Count; i++)
                {
                    if (source.Dead)
                    {
                        break;
                    }

                    BodyPartRecord part = parts[i];
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

                    ApplyExactPartLoss(source, part, loss);
                }
            }

            if (ratio <= 0f && !source.Dead)
            {
                source.Kill(null);
            }
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
