using System;
using System.Collections.Generic;
using HarmonyLib;
using MAP_MechanoidMechanitor;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    internal enum MechanoidMechanitorFactionOutpostWeightKind : byte
    {
        Hostile = 0,
        Neutral = 1,
        Ally = 2
    }

    /// <summary>
    /// 局内剧情配置的统一运行时切换入口。
    /// 与 SetStoryStyleForNewGame 严格分离：这里只替换 activeConfiguration，保留配置来源、
    /// 剧情运行进度、肃清额度/警告、共生信任/盟约、主脑名称和文化锁定等现有存档状态。
    /// </summary>
    internal static class MechanoidMechanitorStoryRuntimeConfigurationUtility
    {
        private static readonly AccessTools.FieldRef<
            GameComponent_MechanoidMechanitorStoryState,
            MechanoidMechanitorStoryConfiguration> ActiveConfigurationField =
                AccessTools.FieldRefAccess<
                    GameComponent_MechanoidMechanitorStoryState,
                    MechanoidMechanitorStoryConfiguration>("activeConfiguration");

        private static readonly AccessTools.FieldRef<
            GameComponent_MechanoidMechanitorStoryState,
            MechanoidMechanitorPurgeDirectiveRuntimeState> PurgeRuntimeStateField =
                AccessTools.FieldRefAccess<
                    GameComponent_MechanoidMechanitorStoryState,
                    MechanoidMechanitorPurgeDirectiveRuntimeState>("purgeDirectiveRuntimeState");

        private static readonly AccessTools.FieldRef<FactionOutpostManager, int>
            FactionOutpostNextGenerationAttemptTickField =
                AccessTools.FieldRefAccess<FactionOutpostManager, int>(
                    "nextGenerationAttemptTick");

        private static readonly AccessTools.FieldRef<MechHiveNodeManager, int>
            MechHiveNodeNextGenerationAttemptTickField =
                AccessTools.FieldRefAccess<MechHiveNodeManager, int>(
                    "nextGenerationAttemptTick");

        public static bool TrySetOrdinaryFactionRelationsMode(
            MechanoidMechanitorOrdinaryFactionRelationsMode mode,
            out string message)
        {
            return TryApplyMutation(
                "ordinaryFactionRelationsMode=" + mode,
                configuration => configuration.ordinaryFactionRelationsMode = mode,
                out message);
        }

        public static bool TrySetOrdinaryFactionRelationOption(
            Faction faction,
            MechanoidMechanitorFactionRelationOption option,
            out string message)
        {
            if (faction == null
                || !MechanoidMechanitorStoryConfigurationContext.IsOrdinaryFaction(faction))
            {
                message = "无法切换普通派系关系：目标派系不是当前有效的普通派系。";
                return false;
            }

            return TryApplyMutation(
                "ordinaryFactionRelation[" + faction.Name + "]=" + option,
                configuration =>
                {
                    configuration.ordinaryFactionRelationsMode =
                        MechanoidMechanitorOrdinaryFactionRelationsMode.Custom;
                    configuration.SetRelationOptionFor(faction, option);
                },
                out message);
        }

        public static bool TrySetFactionOutpostFrequency(
            MechanoidMechanitorFactionOutpostFrequency frequency,
            out string message)
        {
            return TryApplyMutation(
                "factionOutpostFrequency=" + frequency,
                configuration => configuration.factionOutpostFrequency = frequency,
                out message);
        }

        public static bool TrySetFactionOutpostWeight(
            MechanoidMechanitorFactionOutpostWeightKind kind,
            int value,
            out string message)
        {
            if (value < 0 || value > 100)
            {
                message = "无法切换派系前哨权重：权重必须位于 0～100。";
                return false;
            }

            return TryApplyMutation(
                "factionOutpostWeight[" + kind + "]=" + value,
                configuration =>
                {
                    switch (kind)
                    {
                        case MechanoidMechanitorFactionOutpostWeightKind.Hostile:
                            configuration.hostileFactionOutpostWeight = value;
                            break;
                        case MechanoidMechanitorFactionOutpostWeightKind.Neutral:
                            configuration.neutralFactionOutpostWeight = value;
                            break;
                        case MechanoidMechanitorFactionOutpostWeightKind.Ally:
                            configuration.allyFactionOutpostWeight = value;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
                    }
                },
                out message);
        }

        public static bool TrySetMechHiveRelationMode(
            MechanoidMechanitorMechHiveRelationMode mode,
            out string message)
        {
            return TryApplyMutation(
                "mechHiveRelationMode=" + mode,
                configuration => configuration.mechHiveRelationMode = mode,
                out message);
        }

        public static bool TrySetInsectRelationMode(
            MechanoidMechanitorInsectRelationMode mode,
            out string message)
        {
            return TryApplyMutation(
                "insectRelationMode=" + mode,
                configuration => configuration.insectRelationMode = mode,
                out message);
        }

        public static bool TrySetMechHiveNodeFrequency(
            MechanoidMechanitorMechHiveNodeFrequency frequency,
            out string message)
        {
            return TryApplyMutation(
                "mechHiveNodeFrequency=" + frequency,
                configuration => configuration.mechHiveNodeFrequency = frequency,
                out message);
        }

        public static bool TrySetPurgeDirectiveEnabled(bool enabled, out string message)
        {
            if (!TryGetActiveStoryState(
                    out GameComponent_MechanoidMechanitorStoryState storyState,
                    out MechanoidMechanitorStoryConfiguration current,
                    out MechanoidMechanitorStoryConfigurationContext context,
                    out message))
            {
                return false;
            }

            if (storyState.PurgeDirectiveFinalPenaltyTriggered
                && current.purgeDirectiveEnabled != enabled)
            {
                message = "无法切换肃清指令：最终惩罚已经触发，当前肃清路线不可再关闭或重启。";
                return false;
            }

            if (enabled)
            {
                if (!context.HasMechHive)
                {
                    message = "无法开启肃清指令：当前世界没有可用的机械巢派系。";
                    return false;
                }

                if (context.HasPursuingMechanoidsScenarioPart)
                {
                    message = "无法开启肃清指令：当前剧本包含追击机械族剧本部件。";
                    return false;
                }

                if (current.mechHiveRelationMode
                    != MechanoidMechanitorMechHiveRelationMode.Ally)
                {
                    message = "无法开启肃清指令：剧本配置中的机械巢关系必须为盟友。";
                    return false;
                }
            }

            return TryApplyMutation(
                "purgeDirectiveEnabled=" + enabled,
                configuration =>
                {
                    configuration.purgeDirectiveEnabled = enabled;
                    if (enabled)
                    {
                        configuration.symbiosisCovenantEnabled = false;
                    }
                },
                out message);
        }

        public static bool TrySetSymbiosisCovenantEnabled(bool enabled, out string message)
        {
            if (!TryGetActiveStoryState(
                    out GameComponent_MechanoidMechanitorStoryState storyState,
                    out MechanoidMechanitorStoryConfiguration current,
                    out MechanoidMechanitorStoryConfigurationContext context,
                    out message))
            {
                return false;
            }

            if (enabled && !current.IsSymbiosisCovenantAvailable(context))
            {
                message = "无法开启共生盟约：当前普通派系关系配置不允许启用该路线。";
                return false;
            }

            if (enabled
                && storyState.PurgeDirectiveFinalPenaltyTriggered
                && current.purgeDirectiveEnabled)
            {
                message = "无法开启共生盟约：肃清指令最终惩罚已经触发，不能关闭肃清路线。";
                return false;
            }

            return TryApplyMutation(
                "symbiosisCovenantEnabled=" + enabled,
                configuration =>
                {
                    configuration.symbiosisCovenantEnabled = enabled;
                    if (enabled)
                    {
                        configuration.purgeDirectiveEnabled = false;
                    }
                },
                out message);
        }

        public static bool TrySetIdeologyAdaptationLevel(
            MechanoidMechanitorIdeologyAdaptationLevel level,
            out string message)
        {
            if (!ModsConfig.IdeologyActive)
            {
                message = "无法切换文化适配等级：当前没有启用文化 DLC。";
                return false;
            }

            return TryApplyMutation(
                "ideologyAdaptationLevel=" + level,
                configuration => configuration.ideologyAdaptationLevel = level,
                out message);
        }

        private static bool TryApplyMutation(
            string operation,
            Action<MechanoidMechanitorStoryConfiguration> mutation,
            out string message)
        {
            if (!TryGetActiveStoryState(
                    out GameComponent_MechanoidMechanitorStoryState storyState,
                    out MechanoidMechanitorStoryConfiguration current,
                    out _,
                    out message))
            {
                return false;
            }

            bool configurationWritten = false;
            try
            {
                MechanoidMechanitorStoryConfiguration next = current.CreateCopy();
                MechanoidMechanitorStoryConfigurationContext preMutationContext =
                    MechanoidMechanitorStoryConfigurationContext.Create(next);
                next.SyncOrdinaryFactionEntries(preMutationContext);

                mutation(next);

                MechanoidMechanitorStoryConfigurationContext normalizedContext =
                    MechanoidMechanitorStoryConfigurationContext.Create(next);
                next.SyncOrdinaryFactionEntries(normalizedContext);
                next.Normalize(normalizedContext);

                if (next.purgeDirectiveEnabled && next.symbiosisCovenantEnabled)
                {
                    message = "无法切换剧本状态：肃清指令与共生盟约不能同时启用。";
                    return false;
                }

                if (storyState.PurgeDirectiveFinalPenaltyTriggered
                    && current.purgeDirectiveEnabled
                    && !next.purgeDirectiveEnabled)
                {
                    message = "无法切换剧本状态：本次修改会关闭已经触发最终惩罚的肃清指令。";
                    return false;
                }

                ConfigurationChanges changes = DetectChanges(current, next);
                if (!changes.Any)
                {
                    message = operation + "：当前已经是该状态。";
                    return true;
                }

                // 只替换活动配置。禁止调用 SetStoryStyleForNewGame，避免重置任何剧情运行状态。
                ActiveConfigurationField(storyState) = next.CreateCopy();
                configurationWritten = true;
                storyState.RebuildRuntimeCaches();

                List<string> warnings = new List<string>();
                ApplyRuntimeSideEffects(storyState, current, next, changes, warnings);

                if (warnings.Count == 0)
                {
                    message = operation + "：已应用。";
                }
                else
                {
                    string warningText = string.Join("；", warnings);
                    Log.Warning(
                        "[MAP-StoryStateDebug] "
                        + operation
                        + " 已写入配置，但存在运行时同步警告："
                        + warningText);
                    message = operation + "：配置已应用；部分运行时同步需要后续校准。";
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-StoryStateDebug] 执行局内剧情配置切换时发生异常。操作="
                    + operation
                    + "，configurationWritten="
                    + configurationWritten
                    + "\n"
                    + ex);
                message = configurationWritten
                    ? operation + "：配置已经写入，但运行时同步发生异常；请查看日志。"
                    : operation + "：切换失败；请查看日志。";
                return configurationWritten;
            }
        }

        private static bool TryGetActiveStoryState(
            out GameComponent_MechanoidMechanitorStoryState storyState,
            out MechanoidMechanitorStoryConfiguration configuration,
            out MechanoidMechanitorStoryConfigurationContext context,
            out string message)
        {
            storyState = null!;
            configuration = null!;
            context = null!;
            message = string.Empty;

            if (Current.Game == null)
            {
                message = "无法切换剧本状态：当前没有有效游戏。";
                return false;
            }

            if (!GameComponent_MechanoidMechanitorStoryState.IsStoryConfigurationActive)
            {
                message = "无法切换剧本状态：当前存档没有活动的机械族机械师剧情配置。";
                return false;
            }

            GameComponent_MechanoidMechanitorStoryState? resolvedState =
                Current.Game.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            MechanoidMechanitorStoryConfiguration? resolvedConfiguration =
                GameComponent_MechanoidMechanitorStoryState.CurrentConfiguration;
            if (resolvedState == null || resolvedConfiguration == null)
            {
                message = "无法切换剧本状态：剧情状态组件或活动配置不可用。";
                return false;
            }

            storyState = resolvedState;
            configuration = resolvedConfiguration;
            context = MechanoidMechanitorStoryConfigurationContext.Create(configuration);
            return true;
        }

        private static ConfigurationChanges DetectChanges(
            MechanoidMechanitorStoryConfiguration previous,
            MechanoidMechanitorStoryConfiguration next)
        {
            return new ConfigurationChanges
            {
                OrdinaryFactionRelations =
                    previous.ordinaryFactionRelationsMode != next.ordinaryFactionRelationsMode
                    || !SameOrdinaryFactionSettings(previous, next),
                FactionOutpostFrequency =
                    previous.factionOutpostFrequency != next.factionOutpostFrequency,
                FactionOutpostWeights =
                    previous.hostileFactionOutpostWeight != next.hostileFactionOutpostWeight
                    || previous.neutralFactionOutpostWeight != next.neutralFactionOutpostWeight
                    || previous.allyFactionOutpostWeight != next.allyFactionOutpostWeight,
                MechHiveRelation = previous.mechHiveRelationMode != next.mechHiveRelationMode,
                InsectRelation = previous.insectRelationMode != next.insectRelationMode,
                MechHiveNodeFrequency =
                    previous.mechHiveNodeFrequency != next.mechHiveNodeFrequency,
                PurgeDirective = previous.purgeDirectiveEnabled != next.purgeDirectiveEnabled,
                SymbiosisCovenant =
                    previous.symbiosisCovenantEnabled != next.symbiosisCovenantEnabled,
                IdeologyAdaptation =
                    previous.ideologyAdaptationLevel != next.ideologyAdaptationLevel
            };
        }

        private static bool SameOrdinaryFactionSettings(
            MechanoidMechanitorStoryConfiguration left,
            MechanoidMechanitorStoryConfiguration right)
        {
            if (left.ordinaryFactionRelationSettings.Count
                != right.ordinaryFactionRelationSettings.Count)
            {
                return false;
            }

            for (int i = 0; i < left.ordinaryFactionRelationSettings.Count; i++)
            {
                MechanoidMechanitorFactionRelationSetting? leftSetting =
                    left.ordinaryFactionRelationSettings[i];
                if (leftSetting?.faction == null)
                {
                    continue;
                }

                MechanoidMechanitorFactionRelationSetting? rightSetting =
                    right.FindSettingFor(leftSetting.faction);
                if (rightSetting == null
                    || rightSetting.relationOption != leftSetting.relationOption)
                {
                    return false;
                }
            }

            return true;
        }

        private static void ApplyRuntimeSideEffects(
            GameComponent_MechanoidMechanitorStoryState storyState,
            MechanoidMechanitorStoryConfiguration previous,
            MechanoidMechanitorStoryConfiguration next,
            ConfigurationChanges changes,
            List<string> warnings)
        {
            if (changes.OrdinaryFactionRelations
                && !ApplyCurrentOrdinaryFactionRelations(storyState))
            {
                warnings.Add("普通派系关系未全部即时写入");
            }

            if (changes.FactionOutpostFrequency
                && !TryRescheduleFactionOutpostGeneration(next.factionOutpostFrequency))
            {
                warnings.Add("派系前哨下一次生成时间未能重排");
            }

            if (changes.MechHiveNodeFrequency
                && !TryRescheduleMechHiveNodeGeneration(next.mechHiveNodeFrequency))
            {
                warnings.Add("机械巢节点下一次生成时间未能重排");
            }

            if (changes.PurgeDirective
                && !previous.purgeDirectiveEnabled
                && next.purgeDirectiveEnabled)
            {
                EnsurePurgeRuntimeReady(storyState, next.purgeDirectiveEnabled);
            }

            if (changes.SymbiosisCovenant
                || (changes.OrdinaryFactionRelations && next.symbiosisCovenantEnabled))
            {
                SynchronizeSymbiosisRuntime(next.symbiosisCovenantEnabled);
            }

            if (changes.MechHiveRelation || changes.SymbiosisCovenant)
            {
                if (!ApplyCurrentMechHiveRelation(storyState))
                {
                    warnings.Add("机械巢关系未能即时校准");
                }
            }

            if (changes.InsectRelation)
            {
                if (!ApplyCurrentInsectRelation(storyState))
                {
                    warnings.Add("虫巢关系未能即时校准");
                }
            }

            if (changes.IdeologyAdaptation)
            {
                SynchronizeIdeologyRuntime();
            }
        }

        private static bool ApplyCurrentOrdinaryFactionRelations(
            GameComponent_MechanoidMechanitorStoryState storyState)
        {
            if (!storyState.InitialOrdinaryFactionRelationsApplied)
            {
                MechanoidMechanitorOrdinaryFactionRelationApplier
                    .ApplyInitialOrdinaryFactionRelations(
                        storyState,
                        MechanoidMechanitorFactionRelationNotificationMode.Immediate);
                return storyState.InitialOrdinaryFactionRelationsApplied;
            }

            bool allSucceeded = true;
            List<Faction> factions =
                MechanoidMechanitorStoryConfigurationContext.GetOrdinaryFactionsSorted();
            for (int i = 0; i < factions.Count; i++)
            {
                Faction faction = factions[i];
                if (!storyState.TryResolveEffectiveOrdinaryFactionRelationOption(
                        faction,
                        out MechanoidMechanitorFactionRelationOption option))
                {
                    continue;
                }

                if (!MechanoidMechanitorOrdinaryFactionRelationApplier.TryApplyOption(
                        faction,
                        option,
                        MechanoidMechanitorFactionRelationNotificationMode.Immediate))
                {
                    allSucceeded = false;
                }
            }

            return allSucceeded;
        }

        private static bool ApplyCurrentMechHiveRelation(
            GameComponent_MechanoidMechanitorStoryState storyState)
        {
            if (!storyState.InitialMechHiveRelationApplied)
            {
                MechanoidMechanitorMechHiveRelationApplier.ApplyInitialMechHiveRelation(
                    storyState,
                    MechanoidMechanitorFactionRelationNotificationMode.Immediate);
                return storyState.InitialMechHiveRelationApplied;
            }

            Faction? mechHive = storyState.CachedMechHive;
            if (mechHive == null)
            {
                return true;
            }

            // 奥德赛主脑接管是比剧本配置更高优先级的运行时状态。
            // 此时只记录底层配置，实际关系继续由接管逻辑保持盟友。
            if (GameComponent_CerebrexTakeoverState.IsActive)
            {
                return CerebrexTakeoverRelationUtility.EnsureMutualAllies();
            }

            Faction? player = Faction.OfPlayerSilentFail;
            if (player != null
                && GameComponent_SymbiosisCovenantState.IsMechHiveHostileLocked(
                    player,
                    mechHive))
            {
                return MechanoidMechanitorMechHiveRelationApplier.ApplyExactMechHiveRelation(
                    mechHive,
                    FactionRelationKind.Hostile,
                    hostileOnHarmByPlayer: false,
                    MechanoidMechanitorFactionRelationNotificationMode.Immediate);
            }

            if (!MechanoidMechanitorMechHiveRelationPolicy.TryGetEffectiveInitialTarget(
                    storyState,
                    out FactionRelationKind relationKind,
                    out bool hostileOnHarmByPlayer))
            {
                // Default 模式：只释放剧情关系锁，不猜测或回滚此前已经发生的真实外交历史。
                return true;
            }

            return MechanoidMechanitorMechHiveRelationApplier.ApplyExactMechHiveRelation(
                mechHive,
                relationKind,
                hostileOnHarmByPlayer,
                MechanoidMechanitorFactionRelationNotificationMode.Immediate);
        }

        private static bool ApplyCurrentInsectRelation(
            GameComponent_MechanoidMechanitorStoryState storyState)
        {
            if (!storyState.InitialInsectRelationApplied)
            {
                MechanoidMechanitorInsectRelationApplier
                    .ApplyInitialInsectRelation(
                        storyState,
                        MechanoidMechanitorFactionRelationNotificationMode.Immediate);

                return storyState.InitialInsectRelationApplied;
            }

            Faction? insectFaction = storyState.CachedInsectFaction;
            if (insectFaction == null)
            {
                return true;
            }

            if (!storyState.TryGetInsectRelationMode(
                    out MechanoidMechanitorInsectRelationMode mode))
            {
                return false;
            }

            if (mode == MechanoidMechanitorInsectRelationMode.Default)
            {
                // Runtime 从 MOD 管理的虫巢关系模式切回原版 Default 时，
                // 虫族的原版稳定关系基线就是 Hostile。
                // 与 MechHive 不同，这里不能仅释放关系锁后保留 Ally/Neutral，
                // 否则 FactionRelation 与 FactionDef 永久敌对判定会互相矛盾。
                return MechanoidMechanitorInsectRelationApplier
                    .ApplyExactInsectRelation(
                        insectFaction,
                        FactionRelationKind.Hostile,
                        hostileOnHarmByPlayer: false,
                        MechanoidMechanitorFactionRelationNotificationMode.Immediate);
            }

            if (!MechanoidMechanitorInsectRelationPolicy.TryGetInitialTarget(
                    mode,
                    out FactionRelationKind relationKind,
                    out bool hostileOnHarmByPlayer))
            {
                return false;
            }

            return MechanoidMechanitorInsectRelationApplier.ApplyExactInsectRelation(
                insectFaction,
                relationKind,
                hostileOnHarmByPlayer,
                MechanoidMechanitorFactionRelationNotificationMode.Immediate);
        }

        private static bool TryRescheduleFactionOutpostGeneration(
            MechanoidMechanitorFactionOutpostFrequency frequency)
        {
            if (Find.World == null || Find.TickManager == null)
            {
                return false;
            }

            FactionOutpostManager? manager = Find.World.GetComponent<FactionOutpostManager>();
            if (manager == null)
            {
                return false;
            }

            int intervalTicks = MechanoidMechanitorFactionOutpostFrequencyExtensions.TicksPerDay;
            if (frequency.TryGetIntervalDays(out int minDays, out int maxDays))
            {
                intervalTicks = Rand.RangeInclusive(minDays, maxDays)
                    * MechanoidMechanitorFactionOutpostFrequencyExtensions.TicksPerDay;
            }

            FactionOutpostNextGenerationAttemptTickField(manager) =
                Find.TickManager.TicksGame + intervalTicks;
            return true;
        }

        private static bool TryRescheduleMechHiveNodeGeneration(
            MechanoidMechanitorMechHiveNodeFrequency frequency)
        {
            if (Find.World == null || Find.TickManager == null)
            {
                return false;
            }

            MechHiveNodeManager? manager = Find.World.GetComponent<MechHiveNodeManager>();
            if (manager == null)
            {
                return false;
            }

            int intervalTicks = MechanoidMechanitorMechHiveNodeFrequencyExtensions.TicksPerDay;
            if (frequency.TryGetIntervalDays(out int minDays, out int maxDays))
            {
                intervalTicks = Rand.RangeInclusive(minDays, maxDays)
                    * MechanoidMechanitorMechHiveNodeFrequencyExtensions.TicksPerDay;
            }

            MechHiveNodeNextGenerationAttemptTickField(manager) =
                Find.TickManager.TicksGame + intervalTicks;
            return true;
        }

        private static void EnsurePurgeRuntimeReady(
            GameComponent_MechanoidMechanitorStoryState storyState,
            bool enabled)
        {
            MechanoidMechanitorPurgeDirectiveRuntimeState? runtime =
                storyState.PurgeDirectiveRuntimeState;
            if (runtime == null)
            {
                runtime = new MechanoidMechanitorPurgeDirectiveRuntimeState();
                runtime.InitializeForNewGame(enabled);
                PurgeRuntimeStateField(storyState) = runtime;
                return;
            }

            // 局内重新开启只恢复计时，不清空额度、警告、追踪对象和一次性通讯状态。
            if (enabled
                && GameComponent_MechanoidMechanitorStoryState
                    .ShouldRunPurgeFleshColonistComplianceCheck)
            {
                runtime.ScheduleNextCheckFromNow();
            }
        }

        private static void SynchronizeSymbiosisRuntime(bool enabled)
        {
            GameComponent_SymbiosisCovenantState? state =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (enabled)
            {
                state?.SynchronizeNow();
            }

            // GoodwillSituation 会读取当前 IsActive；无论开或关都立即重算，避免旧加成残留。
            Find.GoodwillSituationManager?.RecalculateAll(
                canSendHostilityChangedLetter: false);
        }

        private static void SynchronizeIdeologyRuntime()
        {
            if (!ModsConfig.IdeologyActive)
            {
                return;
            }

            MechanoidMechanitorIdeologyAdaptationUtility.CalibrateAllRegisteredMechanitors();

            // 配置降低时同样立即刷新玩家主流/次要文化和信徒人数缓存。
            // Pawn 上已经创建的 tracker 不主动删除，只让新等级的统一判定决定其是否继续参与。
            Faction.OfPlayer?.ideos?.RecalculateIdeosBasedOnPlayerPawns();
            IdeoManager? manager = Find.IdeoManager;
            if (manager == null)
            {
                return;
            }

            List<Ideo> ideos = manager.IdeosListForReading;
            for (int i = 0; i < ideos.Count; i++)
            {
                ideos[i].RecacheColonistBelieverCount();
            }
        }

        private sealed class ConfigurationChanges
        {
            public bool OrdinaryFactionRelations;
            public bool FactionOutpostFrequency;
            public bool FactionOutpostWeights;
            public bool MechHiveRelation;
            public bool InsectRelation;
            public bool MechHiveNodeFrequency;
            public bool PurgeDirective;
            public bool SymbiosisCovenant;
            public bool IdeologyAdaptation;

            public bool Any =>
                OrdinaryFactionRelations
                || FactionOutpostFrequency
                || FactionOutpostWeights
                || MechHiveRelation
                || InsectRelation
                || MechHiveNodeFrequency
                || PurgeDirective
                || SymbiosisCovenant
                || IdeologyAdaptation;
        }
    }
}
