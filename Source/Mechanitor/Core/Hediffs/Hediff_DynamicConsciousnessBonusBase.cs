using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 身份类健康状态的动态意识加成基类。
    /// 使用运行时创建的 HediffStage，使意识偏移可按条件变化，且不受 Def 固定 stages / Severity 限制。
    /// 科研与机械师数量变化由外部事件驱动刷新，本类不再做周期轮询。
    /// </summary>
    public abstract class Hediff_DynamicConsciousnessBonusBase : Hediff
    {
        // 缓存动态结果，避免每次访问 CurStage 都重新计算或新建 HediffStage。
        private bool cacheInitialized;
        private float cachedOffset;
        private int cachedVariantKey = int.MinValue;
        private HediffStage? cachedStage;

        /// <summary>
        /// 计算当前意识容量偏移。子类按各自条件覆写。
        /// </summary>
        protected virtual float CalculateConsciousnessOffset()
        {
            return 0f;
        }

        /// <summary>
        /// 阶段变体键。偏移值相同但附加效果或科研阶段变化时，用于使缓存阶段失效并重建。
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
                // 仅在尚未初始化时惰性计算一次；正常读取只返回已缓存阶段。
                EnsureDynamicCacheInitialized();
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

        public override void Notify_PawnDied(DamageInfo? dinfo, Hediff? culprit = null)
        {
            base.Notify_PawnDied(dinfo, culprit);

            // 有效机械师数量排除 Dead；死亡时只请求合并刷新，不在死亡流程中立即整批刷新。
            GameComponent_MechanoidMechanitorFeatureManager.NotifyMechanitorRosterChanged();
        }

        public override void Notify_Resurrected()
        {
            base.Notify_Resurrected();

            // 复活后重新计入有效数量；同样只设置 pending，由科研管理器安全时合并处理。
            GameComponent_MechanoidMechanitorFeatureManager.NotifyMechanitorRosterChanged();
        }

        /// <summary>
        /// 主动刷新动态意识效果。科研、机械师数量或其他条件改变时可立即调用以使数值生效。
        /// </summary>
        public void RefreshDynamicEffects(bool notifyHealth = true)
        {
            if (pawn == null || pawn.Destroyed || pawn.health?.hediffSet == null)
            {
                return;
            }

            float newOffset = CalculateConsciousnessOffset();
            int newVariantKey = GetStageVariantKey();
            if (cacheInitialized
                && OffsetsEqual(cachedOffset, newOffset)
                && newVariantKey == cachedVariantKey)
            {
                return;
            }

            cachedOffset = newOffset;
            cachedVariantKey = newVariantKey;
            cachedStage = null;
            cacheInitialized = true;

            if (notifyHealth && pawn.health.hediffSet.hediffs.Contains(this))
            {
                pawn.health.Notify_HediffChanged(this);
            }
        }

        /// <summary>
        /// 惰性初始化：仅在缓存尚未建立时计算一次，不在每次 CurStage 读取时核对科研或数量。
        /// </summary>
        private void EnsureDynamicCacheInitialized()
        {
            if (cacheInitialized)
            {
                return;
            }

            cachedOffset = CalculateConsciousnessOffset();
            cachedVariantKey = GetStageVariantKey();
            cachedStage = null;
            cacheInitialized = true;
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
            return left == right;
        }
    }
}
