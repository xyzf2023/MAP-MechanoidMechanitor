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
        private bool removalCleanupCompleted;

        private CompProperties_ParallelThoughtArray Props
            => (CompProperties_ParallelThoughtArray)props;

        public Pawn? Target => target;

        public int ConfiguredBoostPercent => configuredBoostPercent;

        public int EffectiveBoostPercent
            => IsOperating ? configuredBoostPercent : 0;

        public float RequestedPowerConsumption
            => CalculateRequestedPowerConsumption();

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
            configuredBoostPercent = ClampConfiguredBoostPercent(configuredBoostPercent);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            removalCleanupCompleted = false;
            powerTrader = parent.TryGetComp<CompPowerTrader>();
            configuredBoostPercent = ClampConfiguredBoostPercent(configuredBoostPercent);
            UpdateRequestedPowerDraw();
            lastEffectiveBoostPercent = EffectiveBoostPercent;

            // 读档后也必须在下一次实际 tick 完成状态校正，不要因 respawningAfterLoad 永久跳过。
            ReevaluateOperatingState(true);
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
        ///     此时 parent.Spawned 已为 false，EffectiveBoostPercent/IsOperating/IsProvidingBoostTo
        ///     全部不可用于判断移除前是否正在提供增幅，必须依靠 lastEffectiveBoostPercent。
        /// </summary>
        private void HandleRemovalCleanup()
        {
            if (removalCleanupCompleted)
            {
                return;
            }

            removalCleanupCompleted = true;

            Pawn? oldTarget = target;
            int lostBoostPercent = Mathf.Max(0, lastEffectiveBoostPercent);

            if (oldTarget != null && lostBoostPercent > 0)
            {
                ReclaimConsciousnessLoss(
                    oldTarget,
                    lostBoostPercent / 100f);
            }

            target = null;
            lastEffectiveBoostPercent = 0;
            fallbackTickCounter = 0;

            if (oldTarget != null && !oldTarget.Destroyed)
            {
                ParallelThoughtArrayUtility
                    .RefreshTargetDynamicConsciousness(oldTarget);
            }
        }

        public override void ReceiveCompSignal(string signal)
        {
            base.ReceiveCompSignal(signal);

            switch (signal)
            {
                case "PowerTurnedOn":
                case "PowerTurnedOff":
                case "FlickedOn":
                case "FlickedOff":
                case "Breakdown":
                    ReevaluateOperatingState(true);
                    break;
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

            int oldEffective = lastEffectiveBoostPercent;
            int newEffective = EffectiveBoostPercent;

            if (newEffective < oldEffective && target != null)
            {
                float lost =
                    (oldEffective - newEffective) / 100f;

                ReclaimConsciousnessLoss(target, lost);
            }

            lastEffectiveBoostPercent = newEffective;

            if (target != null
                && (forceRefresh
                    || newEffective != oldEffective))
            {
                ParallelThoughtArrayUtility
                    .RefreshTargetDynamicConsciousness(target);
            }
        }

        /// <summary>
        ///     目标永久失效：先清理其作为监管者的实际分配，再解除建筑目标，再刷新旧目标。
        /// </summary>
        private void HandlePermanentInvalidation(Pawn invalidTarget)
        {
            Pawn? oldTarget = target;
            int lostBoostPercent = Mathf.Max(0, lastEffectiveBoostPercent);

            if (oldTarget != null && lostBoostPercent > 0)
            {
                ReclaimConsciousnessLoss(
                    oldTarget,
                    lostBoostPercent / 100f);
            }

            GameComponent_DataProcessingAllocationRegistry? registry =
                GameComponent_DataProcessingAllocationRegistry.CurrentRegistry;

            if (oldTarget != null)
            {
                registry?.ClearOverseer(oldTarget);
            }

            target = null;
            lastEffectiveBoostPercent = 0;
            fallbackTickCounter = 0;

            UpdateRequestedPowerDraw();

            if (oldTarget != null && !oldTarget.Destroyed)
            {
                ParallelThoughtArrayUtility
                    .RefreshTargetDynamicConsciousness(oldTarget);
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
            int next =
                ClampConfiguredBoostPercent(
                    configuredBoostPercent + delta);

            if (next == configuredBoostPercent)
            {
                return;
            }

            Pawn? currentTarget = target;
            int oldEffective = lastEffectiveBoostPercent;

            configuredBoostPercent = next;
            UpdateRequestedPowerDraw();

            int newEffective = EffectiveBoostPercent;

            if (currentTarget != null
                && newEffective < oldEffective)
            {
                ReclaimConsciousnessLoss(
                    currentTarget,
                    (oldEffective - newEffective) / 100f);
            }

            lastEffectiveBoostPercent = newEffective;

            if (currentTarget != null
                && !currentTarget.Destroyed)
            {
                ParallelThoughtArrayUtility
                    .RefreshTargetDynamicConsciousness(
                        currentTarget);
            }
        }

        private int ClampConfiguredBoostPercent(int value)
        {
            int min = Props.minBoostPercent;
            int max = Props.maxBoostPercent;
            int step = Mathf.Max(1, Props.boostStepPercent);

            int clamped = Mathf.Clamp(value, min, max);
            int relative = clamped - min;
            int alignedSteps =
                Mathf.RoundToInt(relative / (float)step);

            return Mathf.Clamp(
                min + alignedSteps * step,
                min,
                max);
        }

        private float CalculateRequestedPowerConsumption()
        {
            if (target == null || !IsTargetValid)
            {
                return Props.idlePowerConsumption;
            }

            int step = Mathf.Max(1, Props.boostStepPercent);
            int boostSteps =
                Mathf.Max(
                    0,
                    (configuredBoostPercent
                        - Props.minBoostPercent) / step);

            return Props.basePowerConsumption
                + boostSteps * Props.powerPerBoostStep;
        }

        /// <summary>
        ///     统一改绑方法。
        /// </summary>
        public void SetTarget(Pawn? newTarget)
        {
            Pawn? oldTarget = target;
            int oldEffectiveBoost = Mathf.Max(
                0,
                lastEffectiveBoostPercent);

            if (oldTarget != null && oldEffectiveBoost > 0)
            {
                ReclaimConsciousnessLoss(
                    oldTarget,
                    oldEffectiveBoost / 100f);
            }

            target = null;
            lastEffectiveBoostPercent = 0;

            if (oldTarget != null && !oldTarget.Destroyed)
            {
                ParallelThoughtArrayUtility
                    .RefreshTargetDynamicConsciousness(oldTarget);
            }

            target = newTarget;
            fallbackTickCounter = 0;

            UpdateRequestedPowerDraw();
            ReevaluateOperatingState(true);
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
