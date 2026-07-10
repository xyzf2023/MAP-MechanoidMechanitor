using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 数据处理分配健康状态的动态效果基类。
    /// 分配档位以注册表为唯一权威来源；Hediff 本身负责标识状态和向原版能力系统提供动态效果。
    /// </summary>
    public abstract class Hediff_DataProcessingAllocationBase : Hediff
    {
        private const int FallbackRecacheIntervalTicks = 60;

        private int cachedSteps = -1;
        private HediffStage? cachedStage;

        protected abstract int GetAllocationSteps();

        protected abstract bool IsPositiveOffset { get; }

        /// <summary>
        /// 子类可在此追加 Stat 等额外阶段效果；意识修正始终使用未经封顶的真实档位。
        /// </summary>
        protected virtual void ConfigureAdditionalStage(HediffStage stage, int steps)
        {
        }

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
                    ConfigureAdditionalStage(cachedStage, cachedSteps);
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

                // 兼容现有注册表的同步入口：注册表会在档位变化后写入 Severity。
                // 此处只把这次写入作为即时刷新信号；实际档位和意识效果始终重新读取注册表，
                // 因而不会受到 HediffDef.maxSeverity 的限制。
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

        /// <summary>
        /// 从分配注册表重新读取档位。档位改变时丢弃动态阶段，并通过原版健康通知链刷新能力缓存。
        /// </summary>
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
