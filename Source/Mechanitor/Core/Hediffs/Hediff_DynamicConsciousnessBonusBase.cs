using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 身份类健康状态的动态意识加成基类。
    /// 使用运行时创建的 HediffStage，使意识偏移可按条件变化，且不受 Def 固定 stages / Severity 限制。
    /// </summary>
    public abstract class Hediff_DynamicConsciousnessBonusBase : Hediff
    {
        private const int FallbackRecacheIntervalTicks = 60;

        // 缓存上次动态结果，避免每次访问 CurStage 都新建 HediffStage。
        private float cachedOffset = float.NaN;
        private int cachedVariantKey = int.MinValue;
        private HediffStage? cachedStage;

        /// <summary>
        /// 计算当前意识容量偏移。子类按各自条件覆写；当前身份实现固定返回 1f。
        /// </summary>
        protected virtual float CalculateConsciousnessOffset()
        {
            return 0f;
        }

        /// <summary>
        /// 阶段变体键。偏移值相同但附加效果变化时，用于使缓存阶段失效并重建。
        /// </summary>
        protected virtual int GetStageVariantKey()
        {
            return 0;
        }

        /// <summary>
        /// 子类可在此追加能力修正、属性修正等额外阶段效果。
        /// </summary>
        protected virtual void ConfigureAdditionalStage(HediffStage stage)
        {
        }

        public override HediffStage CurStage
        {
            get
            {
                EnsureDynamicCached();
                if (cachedStage == null)
                {
                    cachedStage = BuildDynamicStage(cachedOffset);
                }

                return cachedStage;
            }
        }

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            RefreshDynamicEffects(notifyHealth: false);
        }

        public override void PostTickInterval(int delta)
        {
            base.PostTickInterval(delta);

            // 低频兜底：防止未来条件变化后遗漏主动刷新调用。
            if (pawn != null && !pawn.Destroyed && pawn.IsHashIntervalTick(FallbackRecacheIntervalTicks, delta))
            {
                RefreshDynamicEffects();
            }
        }

        /// <summary>
        /// 主动刷新动态意识效果。科研、身份或其他条件改变时可立即调用以使数值生效。
        /// </summary>
        public void RefreshDynamicEffects(bool notifyHealth = true)
        {
            if (pawn == null || pawn.Destroyed || pawn.health?.hediffSet == null)
            {
                return;
            }

            float newOffset = CalculateConsciousnessOffset();
            int newVariantKey = GetStageVariantKey();
            if (OffsetsEqual(cachedOffset, newOffset) && newVariantKey == cachedVariantKey)
            {
                return;
            }

            cachedOffset = newOffset;
            cachedVariantKey = newVariantKey;
            cachedStage = null;

            if (notifyHealth && pawn.health.hediffSet.hediffs.Contains(this))
            {
                pawn.health.Notify_HediffChanged(this);
            }
        }

        private void EnsureDynamicCached()
        {
            float currentOffset = CalculateConsciousnessOffset();
            int currentVariantKey = GetStageVariantKey();
            if (!OffsetsEqual(cachedOffset, currentOffset) || currentVariantKey != cachedVariantKey)
            {
                cachedOffset = currentOffset;
                cachedVariantKey = currentVariantKey;
                cachedStage = null;
            }
        }

        private HediffStage BuildDynamicStage(float offset)
        {
            // 偏移为 0 时仍返回阶段对象，但不附加有效意识修正；Hediff 本身由身份逻辑管理，不会因此移除。
            var stage = new HediffStage();
            if (offset != 0f)
            {
                stage.capMods = new List<PawnCapacityModifier>
                {
                    new PawnCapacityModifier
                    {
                        capacity = PawnCapacityDefOf.Consciousness,
                        offset = offset
                    }
                };
            }

            ConfigureAdditionalStage(stage);
            return stage;
        }

        private static bool OffsetsEqual(float left, float right)
        {
            if (float.IsNaN(left) && float.IsNaN(right))
            {
                return true;
            }

            return left == right;
        }
    }
}
