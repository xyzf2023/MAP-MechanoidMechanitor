using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 一次顶层 TakeDamage 事件对应一个结构结算上下文。
    /// 概率判定延迟到首个大于 0 的最终护甲后伤害出现时才执行，同一次顶层伤害
    /// 只随机一次；多个最终伤口累计扣除同一份结构稳定值。
    /// 上下文使用深度计数 + 完成队列，配合 Harmony Finalizer 保证异常安全：
    /// 最外层 TakeDamage 结束后才统一结算，绝不在 DamageWorker 内部解除合体。
    /// </summary>
    internal sealed class MechFusionDamageContext
    {
        [ThreadStatic]
        private static List<MechFusionDamageContext>? activeContexts;

        [ThreadStatic]
        private static List<MechFusionDamageContext>? completedContexts;

        [ThreadStatic]
        private static int takeDamageDepth;

        public Pawn Pawn = null!;
        public MechFusionSession Session = null!;
        public float PreHitFraction;
        public bool AllowDamageLeak;
        public bool ProbabilityRolled;
        public bool Ended;
        public float StabilityLoss;
        public bool StabilitySettled;
        public MechFusionExitReason? PendingExitReason;

        internal static MechFusionDamageContext? BeginTakeDamage(
            Thing thing,
            DamageInfo dinfo)
        {
            if (thing is not Pawn pawn)
            {
                return null;
            }

            // EMP 完全不创建合体伤害上下文：不随机、不扣结构稳定值、不改变原版行为。
            if (dinfo.Def == null || dinfo.Def == DamageDefOf.EMP)
            {
                return null;
            }

            if (!GameComponent_MechFusionSessionRegistry.HasAnySession)
            {
                return null;
            }

            if (!MechFusionEnergyUtility.TryGetActiveSessionForWearer(
                    pawn,
                    out MechFusionSession? session)
                || session == null)
            {
                return null;
            }

            if (TryGetForPawn(pawn, out _))
            {
                return null;
            }

            float preHitFraction = session.MaxStability > 0f
                ? Mathf.Clamp01(session.CurrentStability / session.MaxStability)
                : 0f;

            MechFusionDamageContext context = new MechFusionDamageContext
            {
                Pawn = pawn,
                Session = session,
                PreHitFraction = preHitFraction
            };
            activeContexts ??= new List<MechFusionDamageContext>();
            activeContexts.Add(context);
            if (takeDamageDepth < int.MaxValue)
            {
                takeDamageDepth++;
            }

            return context;
        }

        internal static void EndTakeDamage(MechFusionDamageContext? context)
        {
            // 深度只按“创建过上下文的伤害事件”计数，Postfix 与 Finalizer 的
            // 重复调用由 Ended 标记拦截，保证每个事件只减一次。
            if (context == null || context.Ended)
            {
                return;
            }

            context.Ended = true;
            activeContexts?.Remove(context);
            completedContexts ??= new List<MechFusionDamageContext>();
            completedContexts.Add(context);
            takeDamageDepth = Mathf.Max(0, takeDamageDepth - 1);
            if (takeDamageDepth == 0)
            {
                FlushCompletedContexts();
            }
        }

        /// <summary>
        /// 最外层伤害返回后才执行统一收束，确保结构稳定值已经累计完整，
        /// 且不会在 DamageWorker 尚未返回时移除会话。
        /// </summary>
        private static void FlushCompletedContexts()
        {
            if (activeContexts != null && activeContexts.Count > 0)
            {
                // 防御性兜底：任何未正常 End 的残留上下文一并收束并释放。
                completedContexts ??= new List<MechFusionDamageContext>();
                completedContexts.AddRange(activeContexts);
                activeContexts.Clear();
            }

            activeContexts = null;
            if (completedContexts == null || completedContexts.Count == 0)
            {
                completedContexts = null;
                return;
            }

            List<MechFusionDamageContext> completed = completedContexts;
            completedContexts = null;
            for (int i = 0; i < completed.Count; i++)
            {
                try
                {
                    MechFusionStabilityUtility.OnDamageContextClosed(completed[i]);
                }
                catch (Exception ex)
                {
                    Log.Error(
                        "[MAP-机械族机械师] 顶层伤害结束后的结构结算异常：" + ex);
                }
            }

            completed.Clear();
        }

        internal static bool TryGetForPawn(
            Pawn? pawn,
            out MechFusionDamageContext? context)
        {
            context = null;
            if (activeContexts == null || pawn == null)
            {
                return false;
            }

            for (int i = activeContexts.Count - 1; i >= 0; i--)
            {
                MechFusionDamageContext candidate = activeContexts[i];
                if (ReferenceEquals(candidate.Pawn, pawn))
                {
                    context = candidate;
                    return true;
                }
            }

            return false;
        }

        internal static bool TryRegisterExitRequest(
            Pawn? pawn,
            MechFusionExitReason reason)
        {
            if (!TryGetForPawn(pawn, out MechFusionDamageContext? context)
                || context == null)
            {
                return false;
            }

            context.RegisterExitRequest(reason);
            return true;
        }

        internal void RegisterExitRequest(MechFusionExitReason reason)
        {
            if (PendingExitReason == null
                || reason.GetPriority()
                    < PendingExitReason.Value.GetPriority())
            {
                PendingExitReason = reason;
            }
        }

        /// <summary>
        /// 同一顶层伤害只随机一次。稳定值高于 50% 时不随机且人类不受真实伤害；
        /// 低于或等于 50% 时按 (1 - 命中前稳定值比例) × 100 计算泄漏概率。
        /// </summary>
        internal bool EnsureProbabilityRolled()
        {
            if (ProbabilityRolled)
            {
                return AllowDamageLeak;
            }

            ProbabilityRolled = true;
            if (PreHitFraction > 0.5f)
            {
                AllowDamageLeak = false;
                return false;
            }

            int chance = Mathf.Clamp(
                Mathf.RoundToInt((1f - PreHitFraction) * 100f),
                0,
                100);
            AllowDamageLeak = chance > 0
                && Rand.RangeInclusive(1, 100) <= chance;
            return AllowDamageLeak;
        }
    }
}
