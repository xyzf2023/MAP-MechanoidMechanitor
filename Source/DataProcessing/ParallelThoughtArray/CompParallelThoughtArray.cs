using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    ///     并行思维阵列建筑组件：为指定机械师提供外部意识处理能力。
    ///     对机械族机械师表现为“数据处理增幅”，对人类机械师表现为“意识增幅”。
    ///     建筑引用只保存在本 Comp，不保存到任何健康状态。
    /// </summary>
    public sealed class CompParallelThoughtArray : ThingComp
    {
        private Pawn? target;
        private int configuredBoostPercent = ParallelThoughtArrayUtility.MinBoostPercent;
        private CompPowerTrader? powerTrader;
        private int lastEffectiveBoostPercent;
        private int fallbackTickCounter;

        private CompProperties_ParallelThoughtArray Props
            => (CompProperties_ParallelThoughtArray)props;

        public Pawn? Target => target;

        public int ConfiguredBoostPercent => configuredBoostPercent;

        public int EffectiveBoostPercent
            => IsOperating ? configuredBoostPercent : 0;

        public float RequestedPowerConsumption
            => target != null && IsTargetValid
                ? ParallelThoughtArrayUtility.GetPowerConsumptionForBoost(configuredBoostPercent)
                : Props.idlePowerConsumption;

        public bool IsTargetValid
            => target != null
               && DataProcessingAllocatorEligibilityUtility.IsEligibleParallelThoughtArrayTarget(target);

        public bool IsOperating
        {
            get
            {
                if (!parent.Spawned
                    || powerTrader == null
                    || !powerTrader.PowerOn
                    || !IsTargetValid)
                {
                    return false;
                }

                return parent.Faction != null && parent.Faction.IsPlayerSafe();
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref target, "target");
            Scribe_Values.Look(ref configuredBoostPercent, "configuredBoostPercent", ParallelThoughtArrayUtility.MinBoostPercent);

            // 存档中出现异常档位时自动规范化。
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                configuredBoostPercent = ParallelThoughtArrayUtility.ClampBoostPercent(configuredBoostPercent);
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerTrader = parent.TryGetComp<CompPowerTrader>();
            configuredBoostPercent = ParallelThoughtArrayUtility.ClampBoostPercent(configuredBoostPercent);
            UpdateRequestedPowerDraw();
            lastEffectiveBoostPercent = EffectiveBoostPercent;

            // 读档生成时不进行危险的全局大规模清理，依靠后续 60 tick 兜底刷新修正。
            if (!respawningAfterLoad)
            {
                ReevaluateOperatingState(false);
            }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            HandleRemovalCleanup();
            base.PostDeSpawn(map, mode);
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            HandleRemovalCleanup();
            base.PostDestroy(mode, previousMap);
        }

        /// <summary>
        ///     建筑移除（拆除/摧毁）时只执行一次回收与清理。
        /// </summary>
        private void HandleRemovalCleanup()
        {
            Pawn? old = target;
            if (old != null && IsProvidingBoostTo(old))
            {
                ReclaimConsciousnessLoss(old, EffectiveBoostPercent / 100f);
            }

            target = null;
            if (old != null)
            {
                ParallelThoughtArrayUtility.RefreshTargetDynamicConsciousness(old);
            }
        }

        /// <summary>
        ///     更新请求耗电。注意：消费电力时 PowerOutput 必须为负数。
        ///     PowerOn 只决定是否生效，不决定已设定目标的请求功率，避免振荡。
        /// </summary>
        public void UpdateRequestedPowerDraw()
        {
            if (powerTrader == null)
            {
                return;
            }

            float requested = RequestedPowerConsumption;
            powerTrader.PowerOutput = -requested;
        }

        public bool IsProvidingBoostTo(Pawn pawn)
        {
            return pawn != null
                   && ReferenceEquals(target, pawn)
                   && IsOperating
                   && EffectiveBoostPercent > 0;
        }

        /// <summary>
        ///     统一的状态刷新入口。
        /// </summary>
        public void ReevaluateOperatingState(bool forceRefresh)
        {
            UpdateRequestedPowerDraw();

            if (target != null && !IsTargetValid)
            {
                HandlePermanentInvalidation(target);
                return;
            }

            int newEffective = EffectiveBoostPercent;
            if (newEffective < lastEffectiveBoostPercent)
            {
                // 增幅下降：先安全回收超额分配，再刷新健康状态。
                if (target != null)
                {
                    float lost = (lastEffectiveBoostPercent - newEffective) / 100f;
                    ReclaimConsciousnessLoss(target, lost);
                }
            }

            lastEffectiveBoostPercent = newEffective;

            if (target != null && newEffective > 0)
            {
                ParallelThoughtArrayUtility.RefreshTargetDynamicConsciousness(target);
            }
            else if (forceRefresh && target != null)
            {
                ParallelThoughtArrayUtility.RefreshTargetDynamicConsciousness(target);
            }
        }

        /// <summary>
        ///     目标永久失效：先清理其作为监管者的实际分配，再解除建筑目标，再刷新旧目标。
        /// </summary>
        private void HandlePermanentInvalidation(Pawn invalidTarget)
        {
            Pawn? old = target;
            if (old != null && IsProvidingBoostTo(old))
            {
                ReclaimConsciousnessLoss(old, EffectiveBoostPercent / 100f);
            }

            target = null;
            lastEffectiveBoostPercent = 0;
            UpdateRequestedPowerDraw();

            if (old != null)
            {
                ParallelThoughtArrayUtility.RefreshTargetDynamicConsciousness(old);
            }
        }

        public override void CompTick()
        {
            base.CompTick();

            if (powerTrader != null)
            {
                UpdateRequestedPowerDraw();
            }

            int previousEffective = lastEffectiveBoostPercent;
            if (previousEffective != EffectiveBoostPercent || !IsTargetValid && target != null)
            {
                ReevaluateOperatingState(false);
            }

            fallbackTickCounter++;
            if (fallbackTickCounter >= Props.fallbackRefreshIntervalTicks)
            {
                fallbackTickCounter = 0;
                ReevaluateOperatingState(true);
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            if (parent.Faction == null || !parent.Faction.IsPlayerSafe())
            {
                yield break;
            }

            // 选择 / 解除增幅目标。
            yield return MakeSelectTargetCommand();

            if (target != null)
            {
                Command_Action clear = new Command_Action
                {
                    defaultLabel = "MAP_MechanoidMechanitor.ParallelThoughtArray.ClearTarget".Translate(),
                    defaultDesc = "MAP_MechanoidMechanitor.ParallelThoughtArray.ClearTargetDesc".Translate(),
                    icon = TexCommand.ClearPrioritizedWork,
                    action = () => SetTarget(null)
                };
                yield return clear;
            }

            // 降低增幅。
            Command_Action lower = new Command_Action
            {
                defaultLabel = "MAP_MechanoidMechanitor.ParallelThoughtArray.LowerBoost".Translate(),
                defaultDesc = "MAP_MechanoidMechanitor.ParallelThoughtArray.LowerBoostDesc".Translate(),
                icon = TexButton.Minus,
                Disabled = configuredBoostPercent <= Props.minBoostPercent,
                disabledReason = "MAP_MechanoidMechanitor.ParallelThoughtArray.AlreadyMinBoost".Translate(),
                action = () => AdjustBoost(-Props.boostStepPercent)
            };
            yield return lower;

            // 提高增幅。
            Command_Action raise = new Command_Action
            {
                defaultLabel = "MAP_MechanoidMechanitor.ParallelThoughtArray.RaiseBoost".Translate(),
                defaultDesc = "MAP_MechanoidMechanitor.ParallelThoughtArray.RaiseBoostDesc".Translate(),
                icon = TexButton.Plus,
                Disabled = configuredBoostPercent >= Props.maxBoostPercent,
                disabledReason = "MAP_MechanoidMechanitor.ParallelThoughtArray.AlreadyMaxBoost".Translate(),
                action = () => AdjustBoost(Props.boostStepPercent)
            };
            yield return raise;
        }

        private Command_Action MakeSelectTargetCommand()
        {
            List<Pawn> candidates = CollectCandidates();
            bool anyCandidate = candidates.Count > 0;
            Command_Action command = new Command_Action
            {
                defaultLabel = "MAP_MechanoidMechanitor.ParallelThoughtArray.SelectTarget".Translate(),
                defaultDesc = "MAP_MechanoidMechanitor.ParallelThoughtArray.SelectTargetDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Gizmos/BandNodeTuning"),
                Disabled = !anyCandidate && target == null,
                action = () =>
                {
                    List<FloatMenuOption> options = new List<FloatMenuOption>();
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        Pawn candidate = candidates[i];
                        options.Add(new FloatMenuOption(
                            candidate.LabelShortCap,
                            () => SetTarget(candidate)));
                    }

                    Find.WindowStack.Add(new FloatMenu(options));
                }
            };
            return command;
        }

        private List<Pawn> CollectCandidates()
        {
            List<Pawn> result = new List<Pawn>();
            if (parent.Map == null)
            {
                return result;
            }

            IReadOnlyList<Pawn> spawned = parent.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn pawn = spawned[i];
                if (DataProcessingAllocatorEligibilityUtility.IsEligibleParallelThoughtArrayTarget(pawn))
                {
                    result.Add(pawn);
                }
            }

            result.SortBy(p => p.LabelShortCap);
            return result;
        }

        private void AdjustBoost(int delta)
        {
            int next = ParallelThoughtArrayUtility.ClampBoostPercent(configuredBoostPercent + delta);
            if (next == configuredBoostPercent)
            {
                return;
            }

            Pawn? old = target;
            if (old != null && IsOperating && delta < 0)
            {
                // 降档且正在运行：先回收超额分配，再刷新。
                float lost = (configuredBoostPercent - next) / 100f;
                ReclaimConsciousnessLoss(old, lost);
            }

            configuredBoostPercent = next;
            UpdateRequestedPowerDraw();
            lastEffectiveBoostPercent = EffectiveBoostPercent;

            if (old != null)
            {
                ParallelThoughtArrayUtility.RefreshTargetDynamicConsciousness(old);
            }
        }

        /// <summary>
        ///     统一改绑方法。
        /// </summary>
        public void SetTarget(Pawn? newTarget)
        {
            Pawn? old = target;

            if (old != null && IsProvidingBoostTo(old))
            {
                ReclaimConsciousnessLoss(old, EffectiveBoostPercent / 100f);
            }

            target = null;
            if (old != null)
            {
                ParallelThoughtArrayUtility.RefreshTargetDynamicConsciousness(old);
            }

            target = newTarget;
            UpdateRequestedPowerDraw();
            ReevaluateOperatingState(true);

            if (newTarget != null)
            {
                ParallelThoughtArrayUtility.RefreshTargetDynamicConsciousness(newTarget);
            }
        }

        /// <summary>
        ///     外部永久失效清理入口。只有引用一致时才执行。
        /// </summary>
        public void ClearTargetFromExternalInvalidation(Pawn targetToClear)
        {
            if (targetToClear == null || !ReferenceEquals(target, targetToClear))
            {
                return;
            }

            SetTarget(null);
        }

        /// <summary>
        ///     通过注册表的安全回收入口，按即将失去的意识偏移回收分配。
        /// </summary>
        private void ReclaimConsciousnessLoss(Pawn overseer, float consciousnessOffsetLoss)
        {
            GameComponent_DataProcessingAllocationRegistry? registry =
                GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;
            if (registry != null)
            {
                registry.PrepareForExternalConsciousnessLoss(overseer, consciousnessOffsetLoss);
            }
        }

        public override string CompInspectStringExtra()
        {
            string term = target != null
                ? ParallelThoughtArrayUtility.GetCapacityTerm(target)
                : string.Empty;

            string targetLabel = target != null
                ? target.LabelShortCap
                : "MAP_MechanoidMechanitor.ParallelThoughtArray.TargetUnspecified".Translate();

            string status;
            if (!IsTargetValid && target != null)
            {
                status = "MAP_MechanoidMechanitor.ParallelThoughtArray.StatusInvalidTarget".Translate();
            }
            else if (!IsOperating && target != null)
            {
                status = "MAP_MechanoidMechanitor.ParallelThoughtArray.StatusNoPower".Translate();
            }
            else if (target == null)
            {
                status = "MAP_MechanoidMechanitor.ParallelThoughtArray.StatusIdle".Translate();
            }
            else
            {
                status = "MAP_MechanoidMechanitor.ParallelThoughtArray.StatusRunning".Translate();
            }

            List<string> lines = new List<string>
            {
                "MAP_MechanoidMechanitor.ParallelThoughtArray.Inspect.Target".Translate(targetLabel)
            };

            if (target != null)
            {
                lines.Add(
                    "MAP_MechanoidMechanitor.ParallelThoughtArray.Inspect.ConfiguredBoost".Translate(
                        term,
                        configuredBoostPercent));
                lines.Add(
                    "MAP_MechanoidMechanitor.ParallelThoughtArray.Inspect.CurrentBoost".Translate(
                        term,
                        EffectiveBoostPercent));
            }
            else
            {
                lines.Add(
                    "MAP_MechanoidMechanitor.ParallelThoughtArray.Inspect.CurrentBoostNoTarget".Translate(
                        EffectiveBoostPercent));
            }

            lines.Add(
                "MAP_MechanoidMechanitor.ParallelThoughtArray.Inspect.CurrentPower".Translate(
                    Mathf.RoundToInt(RequestedPowerConsumption)));
            lines.Add(
                "MAP_MechanoidMechanitor.ParallelThoughtArray.Inspect.Status".Translate(status));

            return string.Join("\n", lines);
        }
    }
}
