using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 数据处理分配健康状态的动态效果基类。
    /// 分配档位以注册表为唯一权威来源；Hediff severity 仅用于维持健康状态本身，
    /// 实际意识修正通过动态生成的 CurStage.capMods 接入原版能力计算。
    /// </summary>
    public abstract class Hediff_DataProcessingAllocationBase : Hediff
    {
        private const int FallbackRecacheIntervalTicks = 60;

        private int cachedSteps = -1;
        private HediffStage? cachedStage;

        protected abstract int GetAllocationSteps();

        protected abstract bool IsPositiveOffset { get; }

        public override HediffStage CurStage
        {
            get
            {
                EnsureAllocationCached();
                if (cachedSteps <= 0)
                {
                    return null!;
                }

                if (cachedStage == null)
                {
                    float offset = cachedSteps * DataProcessingAllocationUtility.StepPercent;
                    if (!IsPositiveOffset)
                    {
                        offset = -offset;
                    }

                    cachedStage = new HediffStage
                    {
                        capMods = new List<PawnCapacityModifier>
                        {
                            new PawnCapacityModifier
                            {
                                capacity = PawnCapacityDefOf.Consciousness,
                                offset = offset
                            }
                        }
                    };
                }

                return cachedStage;
            }
        }

        public override bool ShouldRemove
        {
            get
            {
                // 注册表未就绪时不要移除，避免读档初始化阶段误删状态。
                if (GameComponent_DataProcessingAllocationRegistry.CurrentRegistry == null)
                {
                    return false;
                }

                return Mathf.Max(0, GetAllocationSteps()) <= 0;
            }
        }

        public override float Severity
        {
            get => base.Severity;
            set
            {
                base.Severity = value;

                // 注册表同步 Hediff 时会设置 severity。即使 severity 因 maxSeverity 被钳制，
                // 也仍以注册表 steps 为准检查动态效果是否需要更新。
                RecacheAllocation();
            }
        }

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            RecacheAllocation(notifyHealth: false);
        }

        public override void PostTickInterval(int delta)
        {
            base.PostTickInterval(delta);
            if (pawn != null && pawn.IsHashIntervalTick(FallbackRecacheIntervalTicks, delta))
            {
                RecacheAllocation();
            }
        }

        public void RecacheAllocation(bool notifyHealth = true)
        {
            int newSteps = Mathf.Max(0, GetAllocationSteps());
            if (newSteps == cachedSteps)
            {
                return;
            }

            cachedSteps = newSteps;
            cachedStage = null;

            if (notifyHealth
                && pawn?.health?.hediffSet != null
                && pawn.health.hediffSet.hediffs.Contains(this))
            {
                pawn.health.Notify_HediffChanged(this);
            }
        }

        private void EnsureAllocationCached()
        {
            int currentSteps = Mathf.Max(0, GetAllocationSteps());
            if (currentSteps != cachedSteps)
            {
                cachedSteps = currentSteps;
                cachedStage = null;
            }
        }
    }
}
