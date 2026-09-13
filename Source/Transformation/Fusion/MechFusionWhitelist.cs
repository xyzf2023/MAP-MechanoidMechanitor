using System;
using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 白名单扩展接口。首个版本只建立接口，注册表初始为空，
    /// 不实现任何具体兼容效果；效果必须具有明确 ownership，
    /// 撤销时不得误删其他 MOD 或人类原有的同类效果。
    /// </summary>
    public interface IMechFusionWhitelistRule
    {
        string RuleId { get; }

        bool Matches(Pawn sourcePawn, Hediff? hediff);

        string CapturePayload(Pawn sourcePawn, Hediff matchedHediff);

        void Apply(MechFusionSession session, Pawn wearer, string payload);

        void Revoke(MechFusionSession session, Pawn wearer, string payload);

        void RepairAfterLoad(
            MechFusionSession session,
            Pawn wearer,
            string payload);
    }

    /// <summary>
    /// 白名单规则注册表。初始为空；未安装的 ruleId 不会导致读档失败。
    /// </summary>
    public static class MechFusionWhitelistRegistry
    {
        private static readonly Dictionary<string, IMechFusionWhitelistRule> Rules =
            new Dictionary<string, IMechFusionWhitelistRule>(StringComparer.Ordinal);

        public static IReadOnlyCollection<IMechFusionWhitelistRule> AllRules =>
            Rules.Values;

        public static void Register(IMechFusionWhitelistRule? rule)
        {
            if (rule == null || string.IsNullOrEmpty(rule.RuleId))
            {
                Log.Error(
                    "[MAP-机械族机械师] 拒绝注册缺少稳定 ruleId 的合体白名单规则。");
                return;
            }

            if (Rules.ContainsKey(rule.RuleId))
            {
                Log.Error(
                    "[MAP-机械族机械师] 合体白名单规则ID重复，已忽略后注册者：" +
                    rule.RuleId);
                return;
            }

            Rules[rule.RuleId] = rule;
        }

        public static bool TryGetRule(
            string? ruleId,
            out IMechFusionWhitelistRule? rule)
        {
            rule = null;
            return !string.IsNullOrEmpty(ruleId)
                && Rules.TryGetValue(ruleId!, out rule);
        }
    }

    internal static class MechFusionWhitelistUtility
    {
        internal static void CaptureMatches(
            MechFusionSession session,
            Pawn sourcePawn)
        {
            if (sourcePawn?.health?.hediffSet == null
                || MechFusionWhitelistRegistry.AllRules.Count == 0)
            {
                return;
            }

            List<Hediff> hediffs = sourcePawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff? hediff = hediffs[i];
                if (hediff?.def == null || hediff.def.isBad)
                {
                    continue;
                }

                foreach (IMechFusionWhitelistRule rule
                         in MechFusionWhitelistRegistry.AllRules)
                {
                    if (!rule.Matches(sourcePawn, hediff))
                    {
                        continue;
                    }

                    string payload;
                    try
                    {
                        // 捕获阶段直接收到已经命中的 Hediff，不再重复扫描全部 Hediff。
                        payload = rule.CapturePayload(sourcePawn, hediff!);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(
                            "[MAP-机械族机械师] 合体白名单规则捕获载荷失败：" +
                            $"rule={rule.RuleId}：" + ex);
                        continue;
                    }

                    // 每个匹配项生成自己的可保存条目；同一 RuleId 的不同植入体
                    // 不会被错误合并。
                    session.AddWhitelistEntry(new MechFusionWhitelistEntry
                    {
                        ruleId = rule.RuleId,
                        payload = payload ?? string.Empty
                    });
                }
            }
        }

        internal static void ApplyAll(
            MechFusionSession session,
            Pawn? wearer)
        {
            if (wearer == null)
            {
                return;
            }

            IReadOnlyList<MechFusionWhitelistEntry> entries =
                session.WhitelistEntries;
            for (int i = 0; i < entries.Count; i++)
            {
                MechFusionWhitelistEntry entry = entries[i];
                if (!MechFusionWhitelistRegistry.TryGetRule(
                        entry.ruleId,
                        out IMechFusionWhitelistRule? rule)
                    || rule == null)
                {
                    Log.Warning(
                        "[MAP-机械族机械师] 合体白名单规则未安装，已安全跳过并保留载荷：" +
                        $"rule={entry.ruleId}。");
                    continue;
                }

                try
                {
                    rule.Apply(session, wearer, entry.payload ?? string.Empty);
                }
                catch (Exception ex)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 应用合体白名单规则效果失败：" +
                        $"rule={entry.ruleId}：{ex}");
                }
            }
        }

        internal static void RevokeAll(
            MechFusionSession session,
            Pawn? wearer)
        {
            if (wearer == null)
            {
                return;
            }

            IReadOnlyList<MechFusionWhitelistEntry> entries =
                session.WhitelistEntries;
            for (int i = 0; i < entries.Count; i++)
            {
                MechFusionWhitelistEntry entry = entries[i];
                if (!MechFusionWhitelistRegistry.TryGetRule(
                        entry.ruleId,
                        out IMechFusionWhitelistRule? rule)
                    || rule == null)
                {
                    continue;
                }

                try
                {
                    rule.Revoke(session, wearer, entry.payload ?? string.Empty);
                }
                catch (Exception ex)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 撤销合体白名单规则效果失败：" +
                        $"rule={entry.ruleId}：{ex}");
                }
            }
        }

        internal static void RepairAfterLoad(
            MechFusionSession session,
            Pawn? wearer)
        {
            if (wearer == null)
            {
                return;
            }

            IReadOnlyList<MechFusionWhitelistEntry> entries =
                session.WhitelistEntries;
            for (int i = 0; i < entries.Count; i++)
            {
                MechFusionWhitelistEntry entry = entries[i];
                if (!MechFusionWhitelistRegistry.TryGetRule(
                        entry.ruleId,
                        out IMechFusionWhitelistRule? rule)
                    || rule == null)
                {
                    Log.Warning(
                        "[MAP-机械族机械师] 读档时合体白名单规则未安装，" +
                        "已保留可恢复载荷：" + $"rule={entry.ruleId}。");
                    continue;
                }

                try
                {
                    rule.RepairAfterLoad(
                        session,
                        wearer,
                        entry.payload ?? string.Empty);
                }
                catch (Exception ex)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 修复合体白名单规则效果失败：" +
                        $"rule={entry.ruleId}：{ex}");
                }
            }
        }
    }
}
