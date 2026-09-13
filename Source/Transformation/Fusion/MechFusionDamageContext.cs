using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 一次顶层 TakeDamage 事件对应一个结构结算上下文。
    /// 记录结算前稳定值比例、是否允许伤害泄漏与递归/内部结算 guard；
    /// 一次顶层伤害事件只进行一次概率判定，多个部位伤害按实际严重度累加。
    /// </summary>
    internal sealed class MechFusionDamageContext
    {
        [ThreadStatic]
        private static List<MechFusionDamageContext>? activeContexts;

        [ThreadStatic]
        private static int takeDamageDepth;

        public Pawn Pawn = null!;
        public MechFusionSession Session = null!;
        public DamageInfo TopLevelDamage;
        public float PreHitFraction;
        public bool AllowDamageLeak;
        public int Depth;
        public float StabilityLoss;
        public bool StabilitySettled;

        internal static void BeginTakeDamage(Thing thing, DamageInfo dinfo)
        {
            takeDamageDepth++;
            if (thing is not Pawn pawn)
            {
                return;
            }

            if (!GameComponent_MechFusionSessionRegistry.HasAnySession)
            {
                return;
            }

            if (!MechFusionEnergyUtility.TryGetActiveSessionForWearer(
                    pawn,
                    out MechFusionSession? session)
                || session == null)
            {
                return;
            }

            if (TryGetForPawn(pawn, out _))
            {
                return;
            }

            float preHitFraction = session.MaxStability > 0f
                ? Mathf.Clamp01(session.CurrentStability / session.MaxStability)
                : 0f;
            bool allowDamageLeak = false;
            if (preHitFraction <= 0.5f)
            {
                int chance = Mathf.Clamp(
                    Mathf.RoundToInt((1f - preHitFraction) * 100f),
                    0,
                    100);
                allowDamageLeak = chance > 0
                    && Rand.RangeInclusive(1, 100) <= chance;
            }

            activeContexts ??= new List<MechFusionDamageContext>();
            activeContexts.Add(new MechFusionDamageContext
            {
                Pawn = pawn,
                Session = session,
                TopLevelDamage = dinfo,
                PreHitFraction = preHitFraction,
                AllowDamageLeak = allowDamageLeak,
                Depth = takeDamageDepth
            });
        }

        internal static void EndTakeDamage(Thing thing)
        {
            if (activeContexts != null && activeContexts.Count > 0)
            {
                int index = activeContexts.Count - 1;
                MechFusionDamageContext top = activeContexts[index];
                if (top.Depth == takeDamageDepth
                    && ReferenceEquals(top.Pawn, thing))
                {
                    activeContexts.RemoveAt(index);
                    MechFusionStabilityUtility.OnDamageContextClosed(top);
                }
            }

            takeDamageDepth = Mathf.Max(0, takeDamageDepth - 1);
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
    }
}
