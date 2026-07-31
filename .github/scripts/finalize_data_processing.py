from pathlib import Path
import re

path = Path('Source/DataProcessing/GameComponent_DataProcessingAllocationRegistry.cs')
text = path.read_text(encoding='utf-8')


def replace_once(pattern: str, replacement: str, flags=re.S) -> None:
    global text
    updated, count = re.subn(pattern, replacement, text, count=1, flags=flags)
    if count != 1:
        raise RuntimeError(f'预期替换 1 处，实际 {count} 处：{pattern[:120]}')
    text = updated


text = text.replace(
    '// 动态分配开关独立于分配档数与特化；开启后按机械体类型自动校正特化（不改档数）。',
    '// 动态分配开关独立于分配档数与特化；开启后按单体规则、状态、优先级与安全预算动态调整模式和实际额度。')

replace_once(
    r'        public bool TrySetDynamicAllocationEnabled\(Pawn\? overseer, bool enabled\)\n        \{.*?\n        \}\n\n        /// <summary>\n        /// 清空指定监管者',
    '''        public bool TrySetDynamicAllocationEnabled(Pawn? overseer, bool enabled)
        {
            if (overseer == null
                || !ResearchFeatureUnlockUtility.IsDataProcessingAllocationUnlocked()
                || !IsDynamicAllocationOverseerValid(overseer)
                || overseer.mechanitor == null)
            {
                return false;
            }

            DataProcessingDynamicAllocationRecord? existing =
                FindDynamicAllocationRecord(overseer);
            if (existing != null)
            {
                if (existing.enabled == enabled)
                {
                    return true;
                }

                existing.enabled = enabled;
            }
            else
            {
                dynamicAllocationRecords.Add(
                    new DataProcessingDynamicAllocationRecord(overseer, enabled));
            }

            RebuildDynamicAllocationCaches();

            bool succeeded;
            if (enabled)
            {
                succeeded = ForceEvaluateAllDynamicTargets(overseer);
            }
            else
            {
                ClearDynamicRuntimeStateForOverseer(overseer);
                succeeded = RestoreDefaultsForOverseer(overseer);
            }

            if (succeeded)
            {
                pendingGlobalTransitionOverseers.Remove(overseer);
                if (pendingGlobalTransitionOverseers.Count == 0)
                {
                    globalTransitionRetryEarliestTick = 0;
                }
            }
            else
            {
                QueueGlobalTransitionRetry(overseer);
            }

            return true;
        }

        /// <summary>
        /// 清空指定监管者''')

replace_once(
    r'        private void QueueGlobalTransitionRetry\(Pawn overseer\)\n        \{.*?\n        \}\n\n        /// <summary>\n        /// 清空指定监管者所有目标的动态评估结果缓存',
    '''        private void QueueGlobalTransitionRetry(Pawn overseer)
        {
            if (overseer == null || overseer.Destroyed)
            {
                return;
            }

            pendingGlobalTransitionOverseers.Add(overseer);

            int requestedTick = Find.TickManager.TicksGame + 60;
            if (globalTransitionRetryEarliestTick <= 0
                || requestedTick < globalTransitionRetryEarliestTick)
            {
                globalTransitionRetryEarliestTick = requestedTick;
            }
        }

        /// <summary>
        /// 清空指定监管者所有目标的动态评估结果缓存''')

replace_once(
    r'        private void ForceEvaluateAllDynamicTargets\(Pawn overseer\)\n        \{.*?\n        \}\n\n        /// <summary>\n        /// 全局关闭动态分配',
    '''        private bool ForceEvaluateAllDynamicTargets(Pawn overseer)
        {
            if (overseer == null
                || overseer.Destroyed
                || !IsDynamicAllocationEnabled(overseer)
                || !IsDynamicAllocationOverseerValid(overseer))
            {
                return false;
            }

            List<Pawn> targets = new List<Pawn>();
            CollectDynamicAllocationTargets(overseer, targets);

            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < targets.Count; i++)
            {
                Pawn target = targets[i];
                if (target == null || target.Destroyed)
                {
                    continue;
                }

                DataProcessingDynamicTargetRecord config =
                    GetOrCreateDynamicTargetRecord(overseer, target);

                if (!config.enabled)
                {
                    cachedDynamicEvaluationByTarget.Remove(target);
                    nextDynamicCheckTickByTarget.Remove(target);
                    continue;
                }

                cachedDynamicEvaluationByTarget[target] =
                    EvaluateTarget(config, target);
                nextDynamicCheckTickByTarget[target] =
                    now + Mathf.Max(60, config.checkIntervalTicks);
            }

            return RunDynamicPlanForOverseer(overseer);
        }

        /// <summary>
        /// 全局关闭动态分配''')

replace_once(
    r'        private void TriggerDynamicStateReevaluation\(\n            Pawn overseer,\n            Pawn target\)\n        \{.*?\n        \}\n\n        private DataProcessingDynamicTargetRecord\? FindDynamicTargetRecord',
    '''        private void TriggerDynamicStateReevaluation(
            Pawn overseer,
            Pawn target)
        {
            DataProcessingDynamicTargetRecord? config =
                FindDynamicTargetRecord(overseer, target);

            if (config == null)
            {
                return;
            }

            if (!IsDynamicAllocationEnabled(overseer)
                || !config.enabled)
            {
                cachedDynamicEvaluationByTarget.Remove(target);
                nextDynamicCheckTickByTarget.Remove(target);

                if (IsDynamicAllocationEnabled(overseer))
                {
                    TriggerDynamicBudgetRecompute(overseer);
                }

                return;
            }

            cachedDynamicEvaluationByTarget[target] =
                EvaluateTarget(config, target);

            nextDynamicCheckTickByTarget[target] =
                Find.TickManager.TicksGame
                + Mathf.Max(60, config.checkIntervalTicks);

            TriggerDynamicBudgetRecompute(overseer);
        }

        private DataProcessingDynamicTargetRecord? FindDynamicTargetRecord''')

replace_once(
    r'            RebuildDynamicTargetCaches\(\);\n        \}\n\n        private void RemoveDynamicTargetRecordAt\(int index, bool clearRuntimeState = true\)',
    '''            RebuildDynamicTargetCaches();

            foreach (KeyValuePair<Pawn, DataProcessingDynamicTargetRecord> pair
                     in retainedByTarget)
            {
                Pawn target = pair.Key;
                DataProcessingDynamicTargetRecord retained = pair.Value;

                if (cachedDynamicEvaluationByTarget.TryGetValue(
                        target,
                        out DynamicEvaluation? evaluation)
                    && evaluation != null
                    && !ReferenceEquals(evaluation.overseer, retained.overseer))
                {
                    cachedDynamicEvaluationByTarget.Remove(target);
                    nextDynamicCheckTickByTarget.Remove(target);

                    if (affectedOverseers != null
                        && retained.overseer != null)
                    {
                        affectedOverseers.Add(retained.overseer);
                    }
                }
            }
        }

        private void RemoveDynamicTargetRecordAt(int index, bool clearRuntimeState = true)''')

marker = '        public override void GameComponentTick()\n'
if marker not in text:
    raise RuntimeError('未找到 GameComponentTick 插入点')
retry_method = '''        private void RetryPendingGlobalTransitions()
        {
            if (pendingGlobalTransitionOverseers.Count == 0
                || Find.TickManager.TicksGame < globalTransitionRetryEarliestTick)
            {
                return;
            }

            List<Pawn> snapshot =
                new List<Pawn>(pendingGlobalTransitionOverseers);

            pendingGlobalTransitionOverseers.Clear();
            globalTransitionRetryEarliestTick = 0;

            for (int i = 0; i < snapshot.Count; i++)
            {
                Pawn overseer = snapshot[i];
                if (overseer == null
                    || overseer.Destroyed
                    || !ResearchFeatureUnlockUtility.IsDataProcessingAllocationUnlocked()
                    || !IsDynamicAllocationOverseerValid(overseer))
                {
                    continue;
                }

                bool succeeded = IsDynamicAllocationEnabled(overseer)
                    ? ForceEvaluateAllDynamicTargets(overseer)
                    : RestoreDefaultsForOverseer(overseer);

                if (!succeeded)
                {
                    QueueGlobalTransitionRetry(overseer);
                }
            }

            if (pendingGlobalTransitionOverseers.Count == 0)
            {
                globalTransitionRetryEarliestTick = 0;
            }
        }

'''
text = text.replace(marker, retry_method + marker, 1)

replace_once(
    r'            // 全局开关切换（开启/关闭）失败后的真实重试：每 60 tick 触发一次，.*?\n            protectionTickCounter\+\+;',
    '''            RetryPendingGlobalTransitions();

            protectionTickCounter++;''')

replace_once(
    r'        private void RefreshDynamicTargetMembership\(\)\n        \{.*?\n        \}\n\n        private bool TryRunPostLoadDynamicReconciliation\(\)',
    '''        private void RefreshDynamicTargetMembership()
        {
            if (dynamicAllocationRecords == null || dynamicTargetRecords == null)
            {
                return;
            }

            HashSet<Pawn> affectedOverseers =
                new HashSet<Pawn>(new ReferencePawnEqualityComparer());

            for (int i = 0; i < dynamicAllocationRecords.Count; i++)
            {
                DataProcessingDynamicAllocationRecord? global =
                    dynamicAllocationRecords[i];
                Pawn? overseer = global?.overseer;

                if (global?.enabled != true
                    || overseer == null
                    || overseer.Destroyed)
                {
                    continue;
                }

                List<Pawn> targets = new List<Pawn>();
                CollectDynamicAllocationTargets(overseer, targets);

                for (int j = 0; j < targets.Count; j++)
                {
                    Pawn target = targets[j];
                    DataProcessingDynamicTargetRecord? existing =
                        FindDynamicTargetRecord(overseer: null, target);

                    bool isNew = false;
                    bool overseerChanged = false;
                    Pawn? oldOverseer = null;

                    if (existing == null)
                    {
                        existing = GetOrCreateDynamicTargetRecord(overseer, target);
                        isNew = true;
                    }
                    else if (!ReferenceEquals(existing.overseer, overseer))
                    {
                        oldOverseer = existing.overseer;
                        existing.overseer = overseer;
                        existing.Normalize();

                        cachedDynamicEvaluationByTarget.Remove(target);
                        nextDynamicCheckTickByTarget.Remove(target);
                        overseerChanged = true;

                        if (oldOverseer != null)
                        {
                            affectedOverseers.Add(oldOverseer);
                        }
                    }

                    bool membershipChanged = isNew || overseerChanged;

                    if (!existing.enabled)
                    {
                        cachedDynamicEvaluationByTarget.Remove(target);
                        nextDynamicCheckTickByTarget.Remove(target);

                        if (membershipChanged)
                        {
                            affectedOverseers.Add(overseer);
                        }

                        continue;
                    }

                    bool runtimeMissing =
                        !cachedDynamicEvaluationByTarget.ContainsKey(target)
                        || !nextDynamicCheckTickByTarget.ContainsKey(target);

                    if (membershipChanged || runtimeMissing)
                    {
                        cachedDynamicEvaluationByTarget[target] =
                            EvaluateTarget(existing, target);
                        nextDynamicCheckTickByTarget[target] =
                            Find.TickManager.TicksGame
                            + Mathf.Max(60, existing.checkIntervalTicks);
                        affectedOverseers.Add(overseer);
                    }
                }
            }

            CleanupInvalidDynamicTargetRecords(affectedOverseers);

            foreach (Pawn affectedOverseer in affectedOverseers)
            {
                if (affectedOverseer == null
                    || affectedOverseer.Destroyed
                    || !IsDynamicAllocationEnabled(affectedOverseer))
                {
                    continue;
                }

                if (!RunDynamicPlanForOverseer(affectedOverseer))
                {
                    QueueGlobalTransitionRetry(affectedOverseer);
                }
            }
        }

        private bool TryRunPostLoadDynamicReconciliation()''')

old_double_plan = '''                    if (IsDynamicAllocationEnabled(overseer))
                    {
                        ForceEvaluateAllDynamicTargets(overseer);

                        succeeded = RunDynamicPlanForOverseer(overseer);
                    }
                    else
                    {
                        succeeded = TryReconcileNonDynamicOverseerAfterLoad(overseer);
                    }'''
new_single_plan = '''                    if (IsDynamicAllocationEnabled(overseer))
                    {
                        succeeded = ForceEvaluateAllDynamicTargets(overseer);
                    }
                    else
                    {
                        succeeded = TryReconcileNonDynamicOverseerAfterLoad(overseer);
                    }'''
if old_double_plan not in text:
    raise RuntimeError('未找到读档重复计划代码')
text = text.replace(old_double_plan, new_single_plan, 1)

restore_anchor = '''            // 移除负面只会提高意识，不会导致读档死亡；下一 tick 再按安全预算恢复。
            foreach (Pawn overseer in overseers)'''
restore_replacement = '''            // 将已无有效记录但仍残留数据流分发的监管者也纳入，
            // 避免残留负面保留到下一实际 tick。
            CollectOverseersNeedingDataStreamResync(overseers);

            // 移除负面只会提高意识，不会导致读档死亡；下一 tick 再按安全预算恢复。
            foreach (Pawn overseer in overseers)'''
if restore_anchor not in text:
    raise RuntimeError('未找到读档负面清理插入点')
text = text.replace(restore_anchor, restore_replacement, 1)

loaded_anchor = '''        public override void LoadedGame()
        {
            base.LoadedGame();

            // 仅清理失效记录'''
loaded_replacement = '''        public override void LoadedGame()
        {
            base.LoadedGame();

            pendingGlobalTransitionOverseers.Clear();
            globalTransitionRetryEarliestTick = 0;

            // 仅清理失效记录'''
if loaded_anchor not in text:
    raise RuntimeError('未找到 LoadedGame 插入点')
text = text.replace(loaded_anchor, loaded_replacement, 1)

remove_anchor = '''            RebuildDynamicAllocationCaches();
        }

        // ===== 单体动态目标配置 ====='''
remove_replacement = '''            RebuildDynamicAllocationCaches();

            pendingGlobalTransitionOverseers.Remove(overseer);
            if (pendingGlobalTransitionOverseers.Count == 0)
            {
                globalTransitionRetryEarliestTick = 0;
            }
        }

        // ===== 单体动态目标配置 ====='''
if remove_anchor not in text:
    raise RuntimeError('未找到动态监管者记录清理插入点')
text = text.replace(remove_anchor, remove_replacement, 1)

clear_anchor = '''            cachedDynamicEvaluationByTarget.Clear();
            pendingPostLoadDynamicReconciliation = false;

            bool allSucceeded = true;'''
clear_replacement = '''            cachedDynamicEvaluationByTarget.Clear();
            pendingPostLoadDynamicReconciliation = false;
            pendingGlobalTransitionOverseers.Clear();
            globalTransitionRetryEarliestTick = 0;

            bool allSucceeded = true;'''
if clear_anchor not in text:
    raise RuntimeError('未找到全部清理插入点')
text = text.replace(clear_anchor, clear_replacement, 1)

replace_once(
    r'        public void SyncHediffForTarget\(Pawn\? target\)\n        \{.*?\n        \}\n\n        public void SyncAllHediffs\(\)',
    '''        public void SyncHediffForTarget(Pawn? target)
        {
            if (target == null
                || target.Destroyed
                || target.health?.hediffSet == null)
            {
                return;
            }

            int steps = GetStepsForTarget(target);
            if (steps <= 0)
            {
                RemoveAllCommandFocusHediffs(target);
                return;
            }

            DataProcessingSpecialization specialization =
                GetSpecializationForTarget(target);

            TryApplyCommandFocusHediffForTarget(
                target,
                specialization,
                steps,
                cleanupOtherCommandFocusHediffs: true);
        }

        public void SyncAllHediffs()''')

required = [
    'private bool ForceEvaluateAllDynamicTargets(Pawn overseer)',
    'RetryPendingGlobalTransitions();',
    'succeeded = ForceEvaluateAllDynamicTargets(overseer);',
    'CollectOverseersNeedingDataStreamResync(overseers);',
    'bool runtimeMissing =',
    '!nextDynamicCheckTickByTarget.ContainsKey(target)',
]
for token in required:
    if token not in text:
        raise RuntimeError(f'最终源码缺少预期标记：{token}')

forbidden = [
    'ForceEvaluateAllDynamicTargets(overseer);\n                        ok = true;',
    'ForceEvaluateAllDynamicTargets(overseer);\n\n                        succeeded = RunDynamicPlanForOverseer(overseer);',
]
for token in forbidden:
    if token in text:
        raise RuntimeError(f'最终源码仍包含旧错误逻辑：{token}')

path.write_text(text, encoding='utf-8', newline='\n')
