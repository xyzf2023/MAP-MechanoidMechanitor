using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    internal static class PurgeDirectiveTradingStateDebugUtility
    {
        private const int TicksPerDay = 60000;

        private static readonly AccessTools.FieldRef<GameComponent_CerebrexTakeoverState, bool>
            TakeoverActiveField =
                AccessTools.FieldRefAccess<GameComponent_CerebrexTakeoverState, bool>(
                    "takeoverActive");

        private static readonly AccessTools.FieldRef<GameComponent_CerebrexTakeoverState, int>
            ResourceCreditsField =
                AccessTools.FieldRefAccess<GameComponent_CerebrexTakeoverState, int>(
                    "resourceCredits");

        private static readonly AccessTools.FieldRef<GameComponent_CerebrexTakeoverState, int>
            LastProcessedGameDayField =
                AccessTools.FieldRefAccess<GameComponent_CerebrexTakeoverState, int>(
                    "lastProcessedGameDay");

        private static readonly AccessTools.FieldRef<GameComponent_CerebrexTakeoverState, int>
            TakeoverCompletedTickField =
                AccessTools.FieldRefAccess<GameComponent_CerebrexTakeoverState, int>(
                    "takeoverCompletedTick");

        public static bool TrySwitchToControlled(out string message)
        {
            message = string.Empty;
            if (Current.Game == null)
            {
                message = "无法切换为主脑受控状态：当前没有有效游戏。";
                return false;
            }

            if (!ModsConfig.OdysseyActive)
            {
                message = "无法切换为主脑受控状态：需要启用奥德赛DLC。";
                return false;
            }

            GameComponent_CerebrexTakeoverState? state =
                GameComponent_CerebrexTakeoverState.Current;
            if (state == null)
            {
                message = "无法切换为主脑受控状态：主脑接管存档组件不可用。";
                return false;
            }

            if (state.TakeoverActive)
            {
                message = "当前已经处于主脑受控状态。";
                return false;
            }

            CerebrexTakeoverPurgeUtility.DisableLegacyPurgeState();

            int ticksGame = Find.TickManager?.TicksGame ?? 0;
            TakeoverActiveField(state) = true;
            ResourceCreditsField(state) =
                GameComponent_CerebrexTakeoverState.InitialCredits;
            LastProcessedGameDayField(state) = ticksGame / TicksPerDay;
            TakeoverCompletedTickField(state) = ticksGame;
            state.ClearPendingTakeover();

            bool relationApplied =
                CerebrexTakeoverRelationUtility.EnsureMutualAllies();
            if (!relationApplied)
            {
                Log.Warning(
                    "[MAP-机械族机械师] 已通过开发者控制台切换为主脑受控状态，"
                    + "但机械巢盟友关系暂未成功应用；状态组件将继续低频重试。");
                message =
                    "已切换为主脑受控状态。当前额度已设置为2000；机械巢关系暂未完成校准，将自动重试。";
                return true;
            }

            message =
                "已切换为主脑受控状态。当前额度已设置为2000，机械巢关系已校准为盟友。";
            return true;
        }

        public static bool TrySwitchToNormal(out string message)
        {
            message = string.Empty;
            if (Current.Game == null)
            {
                message = "无法切换为常态：当前没有有效游戏。";
                return false;
            }

            GameComponent_CerebrexTakeoverState? state =
                GameComponent_CerebrexTakeoverState.Current;
            if (state == null)
            {
                message = "无法切换为常态：主脑接管存档组件不可用。";
                return false;
            }

            if (!state.TakeoverActive)
            {
                message = "当前已经处于常态。";
                return false;
            }

            // 必须先撤销接管标记，避免恢复剧本关系时仍被接管 Harmony 补丁强制改回盟友。
            TakeoverActiveField(state) = false;
            ResourceCreditsField(state) = 0;
            LastProcessedGameDayField(state) = -1;
            TakeoverCompletedTickField(state) = -1;
            state.ClearPendingTakeover();

            // 清除接管前后遗留的肃清运行状态，并按当前剧本配置重新建立常态肃清流程。
            CerebrexTakeoverPurgeUtility.DisableLegacyPurgeState();
            RestoreNormalPurgeState();
            RestoreConfiguredMechHiveRelation();

            message =
                "已切换为常态。主脑接管标记、受控额度和特殊通讯权限已撤销。";
            return true;
        }

        private static void RestoreNormalPurgeState()
        {
            if (!GameComponent_MechanoidMechanitorScenarioState.IsEnabled
                || Current.Game == null)
            {
                return;
            }

            GameComponent_MechanoidMechanitorStoryState? storyState =
                Current.Game.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            MechanoidMechanitorPurgeDirectiveRuntimeState? runtime =
                storyState?.PurgeDirectiveRuntimeState;
            if (storyState == null || runtime == null)
            {
                return;
            }

            // 重新开始肃清检查与奖励进度，但保留已经发送过的一次性通讯解锁提示。
            bool contactLetterSent = runtime.ContactOvermindUnlockedLetterSent;
            runtime.InitializeForNewGame(storyState.PurgeDirectiveEnabled);
            if (contactLetterSent)
            {
                runtime.MarkContactOvermindUnlockedLetterSent();
            }
        }

        private static void RestoreConfiguredMechHiveRelation()
        {
            if (!GameComponent_MechanoidMechanitorScenarioState.IsEnabled
                || Current.Game == null)
            {
                return;
            }

            GameComponent_MechanoidMechanitorStoryState? storyState =
                Current.Game.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            if (storyState == null)
            {
                return;
            }

            storyState.RebuildRuntimeCaches();
            if (!storyState.TryGetMechHiveRelationMode(
                    out MechanoidMechanitorMechHiveRelationMode mode)
                || !MechanoidMechanitorMechHiveRelationPolicy.TryGetInitialTarget(
                    mode,
                    out FactionRelationKind relationKind,
                    out bool hostileOnHarmByPlayer))
            {
                // Default 模式没有一个应被强制恢复的目标关系；只解除接管锁定。
                return;
            }

            Faction? mechHive = storyState.CachedMechHive;
            if (mechHive == null)
            {
                Log.Warning(
                    "[MAP-机械族机械师] 切换为常态后无法恢复剧本机械巢关系："
                    + "当前没有可用的机械巢派系。");
                return;
            }

            if (!MechanoidMechanitorMechHiveRelationApplier.ApplyExactMechHiveRelation(
                    mechHive,
                    relationKind,
                    hostileOnHarmByPlayer,
                    MechanoidMechanitorFactionRelationNotificationMode.Immediate))
            {
                Log.Warning(
                    "[MAP-机械族机械师] 切换为常态后未能恢复剧本配置的机械巢关系。"
                    + "目标关系="
                    + relationKind
                    + "，hostileOnHarmByPlayer="
                    + hostileOnHarmByPlayer
                    + "。");
            }
        }
    }
}

