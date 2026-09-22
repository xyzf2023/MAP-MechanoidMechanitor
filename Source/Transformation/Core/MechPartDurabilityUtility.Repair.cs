using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    internal static partial class MechPartDurabilityUtility
    {
        private const double RepairRoundingTolerance = 0.0001;

        /// <summary>
        /// 建筑维修可修复的结构损伤：现存部位的伤势严重度，加上缺失部位的完整生命。
        /// 伤势保留小数，避免原版 GetPartHealth 的取整吞掉轻伤；人工替代部件造成的
        /// 子部位缺失不属于可再生结构。这里只读，不改变合体的结构完整度算法。
        /// </summary>
        internal static float GetRepairableStructuralDamage(Pawn? pawn)
        {
            HediffSet? hediffSet = pawn?.health?.hediffSet;
            List<BodyPartRecord>? parts = pawn?.RaceProps?.body?.AllParts;
            if (pawn == null || hediffSet == null || parts == null)
            {
                return 0f;
            }

            double total = 0;
            for (int i = 0; i < hediffSet.hediffs.Count; i++)
            {
                if (hediffSet.hediffs[i] is Hediff_Injury injury
                    && injury.Part != null
                    && !HasMissingPartOrAncestor(hediffSet, injury.Part))
                {
                    total += Mathf.Max(0f, injury.Severity);
                }
            }

            for (int i = 0; i < parts.Count; i++)
            {
                BodyPartRecord part = parts[i];
                if (hediffSet.PartIsMissing(part)
                    && !hediffSet.PartOrAnyAncestorHasDirectlyAddedParts(part))
                {
                    total += Mathf.Max(0f, part.def.GetMaxHealth(pawn));
                }
            }

            return (float)total;
        }

        /// <summary>
        /// 独立于损伤结算的治疗入口。先治疗现存部位（含永久伤），再按身体定义顺序
        /// 完整重建付得起的缺失子树。不复活、不治疗疾病、不移除植入物、不累计余款。
        /// 调用方必须先提交形态恢复，避免回滚或紧急恢复重试重复发放维修额度。
        /// </summary>
        internal static void RepairCurrentPartDurability(Pawn? source, float repairBudget)
        {
            if (source == null || source.Dead || source.Destroyed || source.Discarded
                || repairBudget <= 0f || float.IsNaN(repairBudget) || float.IsInfinity(repairBudget))
            {
                return;
            }

            if (!IsInDeathSafeContainer(source))
            {
                throw new InvalidOperationException(
                    "源机械族尚未恢复到安全容器，禁止结算建筑维修：" +
                    $"pawn={source.LabelShort}（{source.ThingID}）。");
            }

            Pawn_HealthTracker? health = source.health;
            HediffSet? hediffSet = health?.hediffSet;
            List<BodyPartRecord>? parts = source.RaceProps?.body?.AllParts;
            if (health == null || hediffSet == null || parts == null)
            {
                return;
            }

            double remaining = repairBudget;
            double roundingTolerance = Math.Max(RepairRoundingTolerance, repairBudget * 0.0000001d);
            // 健康回调可能移除伤势，因此遍历快照，并在写入前复查其仍然存在。
            List<Hediff> injuries = new List<Hediff>(hediffSet.hediffs);
            for (int i = 0; i < parts.Count && remaining > 0 && !source.Dead; i++)
            {
                BodyPartRecord part = parts[i];
                if (HasMissingPartOrAncestor(hediffSet, part))
                {
                    continue;
                }

                for (int j = 0; j < injuries.Count && remaining > 0 && !source.Dead; j++)
                {
                    if (injuries[j] is not Hediff_Injury injury
                        || injury.Part != part || injury.Severity <= 0f
                        || !hediffSet.hediffs.Contains(injury))
                    {
                        continue;
                    }

                    // 原版 Heal 可能先将普通伤势截成永久伤，再次治疗才消除疤痕。
                    // 最多两次，只扣实际降低的严重度，不因首次截断丢失已支付的维修额度。
                    for (int attempt = 0; attempt < 2 && remaining > 0 && !source.Dead
                        && injury.Severity > 0f && hediffSet.hediffs.Contains(injury); attempt++)
                    {
                        float before = injury.Severity;
                        float amount = before <= remaining + roundingTolerance
                            ? before
                            : (float)remaining;
                        injury.Heal(amount);
                        // 不等待下一个健康 tick，避免同一 tick 再次转换时带走零严重度伤势。
                        if (injury.Severity <= 0f && hediffSet.hediffs.Contains(injury))
                        {
                            health.RemoveHediff(injury);
                        }

                        float after = hediffSet.hediffs.Contains(injury)
                            ? Mathf.Max(0f, injury.Severity)
                            : 0f;
                        remaining = Math.Max(0, remaining - Math.Max(0, before - after));
                    }
                }
            }

            for (int i = 0; i < parts.Count && remaining > 0 && !source.Dead; i++)
            {
                BodyPartRecord root = parts[i];
                if (!hediffSet.PartIsMissing(root)
                    || HasMissingPartOrAncestor(hediffSet, root.parent)
                    || hediffSet.PartOrAnyAncestorHasDirectlyAddedParts(root))
                {
                    continue;
                }

                List<BodyPartRecord> missingParts = new List<BodyPartRecord>();
                double cost = 0;
                foreach (BodyPartRecord part in root.GetPartAndAllChildParts())
                {
                    if (hediffSet.PartIsMissing(part)
                        && !hediffSet.PartOrAnyAncestorHasDirectlyAddedParts(part))
                    {
                        missingParts.Add(part);
                        cost += Mathf.Max(0f, part.def.GetMaxHealth(source));
                    }
                }

                // 只容忍浮点往返误差；额度不足时跳过整棵子树，不借出后续转换的额度。
                if (cost > remaining + roundingTolerance)
                {
                    continue;
                }

                RestoreMissingStructure(source, health, hediffSet, missingParts);
                remaining = Math.Max(0, remaining - cost);
            }
        }

        private static bool HasMissingPartOrAncestor(HediffSet hediffSet, BodyPartRecord? part)
        {
            for (BodyPartRecord? current = part; current != null; current = current.parent)
            {
                if (hediffSet.PartIsMissing(current))
                {
                    return true;
                }
            }

            return false;
        }

        private static void RestoreMissingStructure(
            Pawn source, Pawn_HealthTracker health, HediffSet hediffSet, List<BodyPartRecord> missingParts)
        {
            // RestorePart 会清除子树中的其他 Hediff，不能用于需要保留植入物的建筑维修。
            // 只移除已付费部位的伤势与缺失标记；疾病、植入物和其他状态保留原实例。
            List<Hediff> snapshot = new List<Hediff>(hediffSet.hediffs);
            for (int i = 0; i < snapshot.Count && !source.Dead; i++)
            {
                Hediff hediff = snapshot[i];
                if (hediff is Hediff_Injury && hediff.Part != null
                    && missingParts.Contains(hediff.Part) && hediffSet.hediffs.Contains(hediff))
                {
                    health.RemoveHediff(hediff);
                }
            }

            // 先清除子部位标记，最后恢复父部位，减少健康状态回调观察到半棵子树的机会。
            for (int i = missingParts.Count - 1; i >= 0 && !source.Dead; i--)
            {
                for (int j = 0; j < snapshot.Count && !source.Dead; j++)
                {
                    Hediff hediff = snapshot[j];
                    if (hediff is Hediff_MissingPart && hediff.Part == missingParts[i]
                        && hediffSet.hediffs.Contains(hediff))
                    {
                        health.RemoveHediff(hediff);
                    }
                }
            }

            source.Drawer?.renderer?.SetAllGraphicsDirty();
        }
    }
}
