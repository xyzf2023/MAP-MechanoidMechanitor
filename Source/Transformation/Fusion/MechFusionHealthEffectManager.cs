using System;
using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体专属健康状态规则。每条规则在合体瞬间检查完整的源机械族，
    /// 命中后把目标 HediffDef 与可选严重度保存到本次合体会话中。
    /// 一条规则对应一个合体专属 Hediff；同一条件需要添加多个 Hediff 时，
    /// 应注册多条具有不同稳定 RuleId 的规则。
    /// </summary>
    public interface IMechFusionHealthEffectRule
    {
        string RuleId { get; }

        bool TryCapture(
            Pawn sourcePawn,
            out HediffDef? hediffDef,
            out float severity);
    }

    /// <summary>
    /// 合体健康状态规则注册表。内置“机体同调”无条件规则；后续规则可以
    /// 直接判断机械族机械师身份、源 Pawn 的 Hediff 或其他稳定状态。
    /// </summary>
    public static class MechFusionHealthEffectRegistry
    {
        internal const string BodySynchronizationRuleId =
            "MAP.BodySynchronization";

        private static readonly Dictionary<string, IMechFusionHealthEffectRule>
            Rules = new Dictionary<string, IMechFusionHealthEffectRule>(
                StringComparer.Ordinal);

        static MechFusionHealthEffectRegistry()
        {
            Register(new BodySynchronizationRule());
        }

        public static IReadOnlyCollection<IMechFusionHealthEffectRule> AllRules =>
            Rules.Values;

        public static void Register(IMechFusionHealthEffectRule? rule)
        {
            if (rule == null || string.IsNullOrEmpty(rule.RuleId))
            {
                Log.Error(
                    "[MAP-机械族机械师] 拒绝注册缺少稳定 RuleId 的合体健康状态规则。");
                return;
            }

            if (Rules.ContainsKey(rule.RuleId))
            {
                Log.Error(
                    "[MAP-机械族机械师] 合体健康状态规则ID重复，已忽略后注册者：" +
                    rule.RuleId);
                return;
            }

            Rules[rule.RuleId] = rule;
        }

        private sealed class BodySynchronizationRule
            : IMechFusionHealthEffectRule
        {
            public string RuleId => BodySynchronizationRuleId;

            public bool TryCapture(
                Pawn sourcePawn,
                out HediffDef? hediffDef,
                out float severity)
            {
                hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(
                    MechFusionDefNames.BodySynchronizationHediffDefName);
                severity = -1f;
                return true;
            }
        }
    }

    /// <summary>
    /// 合体专属 Hediff 的统一捕获、添加、解除与读档修复入口。
    /// 所有操作均按 Def 幂等执行；单条添加失败只记录警告，不中断合体。
    /// </summary>
    internal static class MechFusionHealthEffectManager
    {
        internal static void CaptureFromSource(
            MechFusionSession session,
            Pawn? sourcePawn)
        {
            if (session == null || sourcePawn == null)
            {
                return;
            }

            foreach (IMechFusionHealthEffectRule rule
                     in MechFusionHealthEffectRegistry.AllRules)
            {
                if (session.HasHealthEffectRule(rule.RuleId))
                {
                    continue;
                }

                try
                {
                    if (!rule.TryCapture(
                            sourcePawn,
                            out HediffDef? hediffDef,
                            out float severity))
                    {
                        continue;
                    }

                    if (hediffDef == null)
                    {
                        Log.Warning(
                            "[MAP-机械族机械师] 合体健康状态规则命中，但未能解析" +
                            $"目标 HediffDef：rule={rule.RuleId}。");
                        continue;
                    }

                    session.AddHealthEffectEntry(new MechFusionHealthEffectEntry
                    {
                        ruleId = rule.RuleId,
                        hediffDef = hediffDef,
                        severity = severity
                    });
                }
                catch (Exception ex)
                {
                    Log.Warning(
                        "[MAP-机械族机械师] 捕获合体健康状态规则失败，已跳过：" +
                        $"rule={rule.RuleId}：{ex}");
                }
            }
        }

        internal static void ApplyAll(
            MechFusionSession session,
            Pawn? wearer)
        {
            EnsureBodySynchronizationEntry(session);
            if (wearer?.health?.hediffSet == null)
            {
                Log.Warning(
                    "[MAP-机械族机械师] 合体目标缺少可用健康组件，" +
                    $"无法添加合体专属健康状态：session={session.SessionId}。");
                return;
            }

            IReadOnlyList<MechFusionHealthEffectEntry> entries =
                session.HealthEffectEntries;
            for (int i = 0; i < entries.Count; i++)
            {
                MechFusionHealthEffectEntry? entry = entries[i];
                HediffDef? def = entry?.hediffDef;
                if (entry == null || def == null)
                {
                    Log.Warning(
                        "[MAP-机械族机械师] 合体健康状态记录缺少 HediffDef，" +
                        $"已跳过：rule={entry?.ruleId ?? "<null>"}。");
                    continue;
                }

                if (wearer.health.hediffSet.HasHediff(def))
                {
                    continue;
                }

                try
                {
                    Hediff hediff = HediffMaker.MakeHediff(def, wearer);
                    if (entry.severity >= 0f)
                    {
                        hediff.Severity = entry.severity;
                    }

                    wearer.health.AddHediff(hediff);
                    if (!wearer.health.hediffSet.HasHediff(def))
                    {
                        Log.Warning(
                            "[MAP-机械族机械师] 添加合体专属健康状态后未找到" +
                            $"对应实例：rule={entry.ruleId}，" +
                            $"hediff={def.defName}。");
                    }
                }
                catch (Exception ex)
                {
                    // 健康状态属于合体的附加表现；失败不得破坏已提交的合体事务。
                    Log.Warning(
                        "[MAP-机械族机械师] 添加合体专属健康状态失败，" +
                        $"已保持合体继续：rule={entry.ruleId}，" +
                        $"hediff={def.defName}：{ex}");
                }
            }
        }

        internal static bool RevokeAll(
            MechFusionSession session,
            Pawn? wearer)
        {
            EnsureBodySynchronizationEntry(session);
            if (wearer?.health?.hediffSet == null)
            {
                return true;
            }

            bool allSucceeded = true;
            HashSet<HediffDef> handledDefs = new HashSet<HediffDef>();
            IReadOnlyList<MechFusionHealthEffectEntry> entries =
                session.HealthEffectEntries;
            for (int i = 0; i < entries.Count; i++)
            {
                HediffDef? def = entries[i]?.hediffDef;
                if (def == null || !handledDefs.Add(def))
                {
                    continue;
                }

                try
                {
                    List<Hediff> hediffs =
                        wearer.health.hediffSet.hediffs;
                    for (int j = hediffs.Count - 1; j >= 0; j--)
                    {
                        Hediff? hediff = hediffs[j];
                        if (hediff?.def == def)
                        {
                            wearer.health.RemoveHediff(hediff);
                        }
                    }
                }
                catch (Exception ex)
                {
                    allSucceeded = false;
                    Log.Warning(
                        "[MAP-机械族机械师] 移除合体专属健康状态失败，" +
                        "将保留会话等待后续重试：" +
                        $"hediff={def.defName}：{ex}");
                }
            }

            return allSucceeded;
        }

        internal static void RepairAfterLoad(
            MechFusionSession session,
            Pawn? wearer)
        {
            ApplyAll(session, wearer);
        }

        private static void EnsureBodySynchronizationEntry(
            MechFusionSession session)
        {
            if (session.HasHealthEffectRule(
                    MechFusionHealthEffectRegistry.BodySynchronizationRuleId))
            {
                return;
            }

            HediffDef? def = DefDatabase<HediffDef>.GetNamedSilentFail(
                MechFusionDefNames.BodySynchronizationHediffDefName);
            if (def == null)
            {
                Log.Warning(
                    "[MAP-机械族机械师] 无法解析机体同调 HediffDef，" +
                    $"session={session.SessionId}。");
                return;
            }

            // 兼容旧存档：旧会话在管理层建立前没有基础规则记录。
            session.AddHealthEffectEntry(new MechFusionHealthEffectEntry
            {
                ruleId =
                    MechFusionHealthEffectRegistry.BodySynchronizationRuleId,
                hediffDef = def,
                severity = -1f
            });
        }
    }
}
