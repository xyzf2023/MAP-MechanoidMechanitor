using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 虫巢追杀（Pursuit）模式的专属事件管理器。
    /// 负责额外虫灾与虫族追猎两种事件的生命周期、共享冷却与持久化。
    /// 不负责剧情配置/关系状态（由 GameComponent_MechanoidMechanitorStoryState 负责）。
    ///
    /// 作为 GameComponent 自动随游戏实例化，并重写 GameComponentTick 进行周期性调度。
    /// </summary>
    public sealed class GameComponent_MechanoidMechanitorInsectPursuitManager : GameComponent
    {
        private int lastDailyCheckDay = -1;

        private MechanoidMechanitorInsectPursuitPendingEvent pendingEvent =
            MechanoidMechanitorInsectPursuitPendingEvent.None;

        private int pendingTriggerTick = -1;

        private int lastPursuitEventTriggerDay = -1;

        private Map? activeHuntMap;

        private int activeHuntAttackTick = -1;

        private bool activeHuntStartedWithGravEngine;

        public Map? ActiveHuntMap => activeHuntMap;

        public int ActiveHuntAttackTick => activeHuntAttackTick;

        public GameComponent_MechanoidMechanitorInsectPursuitManager(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(
                ref lastDailyCheckDay,
                "pursuitLastDailyCheckDay",
                -1);
            Scribe_Values.Look(
                ref pendingEvent,
                "pursuitPendingEvent",
                MechanoidMechanitorInsectPursuitPendingEvent.None);
            Scribe_Values.Look(
                ref pendingTriggerTick,
                "pursuitPendingTriggerTick",
                -1);
            Scribe_Values.Look(
                ref lastPursuitEventTriggerDay,
                "pursuitLastEventTriggerDay",
                -1);
            Scribe_References.Look(
                ref activeHuntMap,
                "pursuitActiveHuntMap");
            Scribe_Values.Look(
                ref activeHuntAttackTick,
                "pursuitActiveHuntAttackTick",
                -1);
            Scribe_Values.Look(
                ref activeHuntStartedWithGravEngine,
                "pursuitActiveHuntStartedWithGravEngine",
                false);
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager == null || Current.Game == null)
            {
                return;
            }

            // 每 250 ticks（约 0.1 游戏小时）更新一次，避免每 tick 扫描地图。
            if (Find.TickManager.TicksGame % 250 != 0)
            {
                return;
            }

            UpdateInternal();
        }

        private void UpdateInternal()
        {
            // A. 检查追杀模式是否仍启用
            if (!MechanoidMechanitorInsectPursuitUtility.IsPursuitActive())
            {
                // 离开追杀模式：立即取消隐藏预约与进行中的追猎，但保留共享冷却，
                // 防止运行时切出再切回时刷掉冷却。不发送取消信件。
                if (pendingEvent != MechanoidMechanitorInsectPursuitPendingEvent.None
                    || pendingTriggerTick >= 0
                    || activeHuntMap != null
                    || activeHuntAttackTick >= 0)
                {
                    ClearScheduledAndActiveState();
                }

                return;
            }

            // 先处理旧 pending / active，再考虑新的每日抽签，
            // 避免同一轮更新中“旧预约刚到期”又“立即产生新预约”。
            //
            // 记录本轮开始时是否存在 blocking 状态（隐藏预约或公开追猎），
            // 用于决定当天是否允许补抽：只要进入当天时存在，当天就不再补抽
            // （即使它在这一轮更新中刚好到期/被取消）。
            bool hadBlockingPursuitStateAtStart =
                pendingEvent != MechanoidMechanitorInsectPursuitPendingEvent.None
                || pendingTriggerTick >= 0
                || activeHuntMap != null
                || activeHuntAttackTick >= 0;

            // B. 清理失效的 active hunt
            CleanupInvalidActiveHunt();

            // C. 处理已到期的 pending
            if (pendingEvent != MechanoidMechanitorInsectPursuitPendingEvent.None
                && Find.TickManager.TicksGame >= pendingTriggerTick)
            {
                ProcessExpiredPending();
            }

            // D. 处理已到期的 active hunt 攻击
            if (activeHuntMap != null
                && Find.TickManager.TicksGame >= activeHuntAttackTick)
            {
                ProcessExpiredActiveHuntAttack();
            }

            // E. 检查是否跨入新的游戏日
            int currentDay = GenDate.DaysPassed;
            if (currentDay != lastDailyCheckDay)
            {
                lastDailyCheckDay = currentDay;

                // 只要进入当天时存在 blocking 状态，当天就不补抽新的 Pursuit 事件，
                // 即使它在本轮更新中已到期/被执行/被取消。
                if (!hadBlockingPursuitStateAtStart)
                {
                    TryDailyRoll(currentDay);
                }
            }
        }

        private void CleanupInvalidActiveHunt()
        {
            if (activeHuntMap == null)
            {
                return;
            }

            if (activeHuntMap.Parent == null
                || !Find.Maps.Contains(activeHuntMap))
            {
                ClearActiveHunt();
                return;
            }

            // 逆重飞船起飞后取消后续攻击
            if (activeHuntStartedWithGravEngine
                && ModsConfig.OdysseyActive
                && MechanoidMechanitorInsectPursuitUtility
                    .TryGetPlayerGravEngine(activeHuntMap) == null)
            {
                ClearActiveHunt();
            }
        }

        private void ProcessExpiredPending()
        {
            MechanoidMechanitorInsectPursuitPendingEvent ev = pendingEvent;
            pendingEvent = MechanoidMechanitorInsectPursuitPendingEvent.None;
            pendingTriggerTick = -1;

            switch (ev)
            {
                case MechanoidMechanitorInsectPursuitPendingEvent.ExtraInfestation:
                    ExecuteExtraInfestation();
                    break;
                case MechanoidMechanitorInsectPursuitPendingEvent.Hunt:
                    ExecuteHuntPending();
                    break;
            }
        }

        private void ProcessExpiredActiveHuntAttack()
        {
            Map? map = activeHuntMap;
            if (map == null || !Find.Maps.Contains(map))
            {
                ClearActiveHunt();
                return;
            }

            LaunchAttackOn(map);
            ClearActiveHunt();
        }

        private void ExecuteExtraInfestation()
        {
            Map? map = MechanoidMechanitorInsectPursuitUtility.ChooseEligibleMap();
            if (map == null)
            {
                return;
            }

            ExecuteExtraInfestationOn(map, refreshCooldown: true);
        }

        private void ExecuteHuntPending()
        {
            Map? map = MechanoidMechanitorInsectPursuitUtility.ChooseEligibleMap();
            if (map == null)
            {
                return;
            }

            ExecuteHuntPendingOn(map, refreshCooldown: true);
        }

        private void ExecuteExtraInfestationOn(Map map, bool refreshCooldown)
        {
            if (map == null)
            {
                return;
            }

            IncidentParms parms = new IncidentParms
            {
                target = map,
                forced = true,
                points = StorytellerUtility.DefaultThreatPointsNow(map),
                sendLetter = true
            };

            // 资格检查与实际执行使用同一次解析（TryPrepareManagedInfestation 内部
            // 找到的 fallback cell 直接写入 parms.infestationLocOverride），避免二次随机。
            if (!MechanoidMechanitorInsectPursuitInfestationUtility
                    .TryPrepareManagedInfestation(map, parms))
            {
                // 不满足原版虫灾资格（虫族缺失 / Hive>=30 / 无落点）：本次作废，不刷新共享冷却。
                return;
            }

            bool ok = IncidentDefOf.Infestation.Worker.TryExecute(parms);
            if (ok && refreshCooldown)
            {
                lastPursuitEventTriggerDay = GenDate.DaysPassed;
            }
        }

        private void ExecuteHuntPendingOn(Map map, bool refreshCooldown)
        {
            if (map == null)
            {
                return;
            }

            if (!MechanoidMechanitorInsectPursuitUtility
                    .CanCurrentlyHostManagedInfestation(map))
            {
                return;
            }

            Find.LetterStack.ReceiveLetter(
                "MAP_MechanoidMechanitor.InsectPursuit.Hunt.Warning.Label"
                    .Translate(),
                "MAP_MechanoidMechanitor.InsectPursuit.Hunt.Warning.Text".Translate(),
                LetterDefOf.ThreatBig);

            activeHuntMap = map;
            int warningHours = Rand.RangeInclusive(8, 12);
            activeHuntAttackTick =
                Find.TickManager.TicksGame + warningHours * GenDate.TicksPerHour;

            if (refreshCooldown)
            {
                // 共享冷却从第一封“虫族追猎”真正发出时开始。
                lastPursuitEventTriggerDay = GenDate.DaysPassed;
            }

            activeHuntStartedWithGravEngine = ModsConfig.OdysseyActive
                && MechanoidMechanitorInsectPursuitUtility
                    .TryGetPlayerGravEngine(map) != null;
        }

        private void LaunchAttackOn(Map map)
        {
            if (map == null)
            {
                return;
            }

            Find.LetterStack.ReceiveLetter(
                "MAP_MechanoidMechanitor.InsectPursuit.Attack.Label".Translate(),
                "MAP_MechanoidMechanitor.InsectPursuit.Attack.Text".Translate(),
                LetterDefOf.ThreatBig);

            float basePoints = StorytellerUtility.DefaultThreatPointsNow(map);

            MAPMechanitorModSettings? settings = MAPMechanitorMod.Settings;

            // 地面路线：虫族通过地图边缘步行进入
            float surfacePoints = basePoints
                * Mathf.Clamp(
                    settings?.pursuitHuntSurfacePointsPercent ?? 0, 0, 1000)
                / 100f;
            if (surfacePoints > 0f)
            {
                IncidentParms surfaceParms = new IncidentParms
                {
                    target = map,
                    forced = true,
                    points = surfacePoints,
                    faction = Faction.OfInsects,
                    raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn,
                    raidStrategy = RaidStrategyDefOf.ImmediateAttack,
                    sendLetter = false
                };

                if (!IncidentDefOf.RaidEnemy.Worker.TryExecute(surfaceParms))
                {
                    Log.Warning(
                        "[MAP-机械族机械师] 虫族追猎：地面虫群生成失败（地图="
                        + map
                        + "）。");
                }
            }

            // 地下路线：标准虫灾（复用无厚岩顶 fallback）
            float infestPoints = basePoints
                * Mathf.Clamp(
                    settings?.pursuitHuntInfestationPointsPercent ?? 0, 0, 1000)
                / 100f;
            if (infestPoints > 0f)
            {
                IncidentParms infestParms = new IncidentParms
                {
                    target = map,
                    forced = true,
                    points = infestPoints,
                    sendLetter = false
                };

                // 攻击阶段重新准备（地图状态可能已变化：Hive 增加 / fallback 格被占据等）。
                // 失败只记录 warning 并跳过地下路线，不阻止已经成功的地面路线。
                if (!MechanoidMechanitorInsectPursuitInfestationUtility
                        .TryPrepareManagedInfestation(map, infestParms))
                {
                    Log.Warning(
                        "[MAP-机械族机械师] 虫族追猎：地下虫灾准备失败（地图="
                        + map
                        + "）。");
                }
                else if (!IncidentDefOf.Infestation.Worker.TryExecute(infestParms))
                {
                    Log.Warning(
                        "[MAP-机械族机械师] 虫族追猎：地下虫灾生成失败（地图="
                        + map
                        + "）。");
                }
            }
        }

        private void SchedulePending(MechanoidMechanitorInsectPursuitPendingEvent ev)
        {
            pendingEvent = ev;
            pendingTriggerTick =
                Find.TickManager.TicksGame + Rand.Range(0, GenDate.TicksPerDay);
        }

        private void TryDailyRoll(int currentDay)
        {
            MAPMechanitorModSettings? settings = MAPMechanitorMod.Settings;
            if (settings == null)
            {
                return;
            }

            // 开局保护期：currentDay < grace 时不判定。
            if (currentDay < Mathf.Clamp(settings.pursuitGracePeriodDays, 0, 30))
            {
                return;
            }

            // 全局互斥：pending 或 active 存在时当天不再安排。
            if (pendingEvent != MechanoidMechanitorInsectPursuitPendingEvent.None)
            {
                return;
            }

            if (activeHuntMap != null)
            {
                return;
            }

            // 共享冷却（两种事件共用同一字段）
            int cooldown = Mathf.Clamp(settings.pursuitSharedCooldownDays, 0, 15);
            if (cooldown > 0
                && lastPursuitEventTriggerDay >= 0
                && currentDay - lastPursuitEventTriggerDay < cooldown)
            {
                return;
            }

            // 至少存在一个潜在合法玩家地图
            if (!MechanoidMechanitorInsectPursuitUtility.HasAnyEligibleTargetMap())
            {
                return;
            }

            // 追猎优先：命中后不再判定额外虫灾。
            float huntChance = Mathf.Clamp(
                settings.pursuitHuntDailyChancePercent, 0, 100) / 100f;
            if (Rand.Chance(huntChance))
            {
                SchedulePending(MechanoidMechanitorInsectPursuitPendingEvent.Hunt);
                return;
            }

            // 追猎失败后才判定额外虫灾。
            float infestationChance = Mathf.Clamp(
                settings.pursuitExtraInfestationDailyChancePercent, 0, 100)
                / 100f;
            if (Rand.Chance(infestationChance))
            {
                SchedulePending(
                    MechanoidMechanitorInsectPursuitPendingEvent.ExtraInfestation);
            }
        }

        private void ClearActiveHunt()
        {
            activeHuntMap = null;
            activeHuntAttackTick = -1;
            activeHuntStartedWithGravEngine = false;
        }

        /// <summary>
        /// 统一清除“隐藏预约 + 公开追猎倒计时”的全部状态字段。
        /// 用于离开 Pursuit 模式与 DEV 清除运行时状态。
        ///
        /// 绝对不能修改 lastPursuitEventTriggerDay：运行时切出 Pursuit 再切回
        /// 不应刷掉共享冷却。
        /// </summary>
        private void ClearScheduledAndActiveState()
        {
            pendingEvent =
                MechanoidMechanitorInsectPursuitPendingEvent.None;
            pendingTriggerTick = -1;

            activeHuntMap = null;
            activeHuntAttackTick = -1;
            activeHuntStartedWithGravEngine = false;
        }

        public int GetActiveHuntRemainingHours()
        {
            if (activeHuntMap == null)
            {
                return 0;
            }

            int remainingTicks = Mathf.Max(
                0,
                activeHuntAttackTick - Find.TickManager.TicksGame);
            return Mathf.CeilToInt(remainingTicks / (float)GenDate.TicksPerHour);
        }

        public static GameComponent_MechanoidMechanitorInsectPursuitManager? GetManager()
        {
            if (Current.Game == null)
            {
                return null;
            }

            return Current.Game
                .GetComponent<GameComponent_MechanoidMechanitorInsectPursuitManager>();
        }

        // ===== 开发者测试入口（绕过概率/保护期/冷却，复用正式执行函数） =====

        public void DevTriggerExtraInfestation()
        {
            Map? map = Find.CurrentMap;
            if (map == null)
            {
                Messages.Message(
                    "需要有效的当前地图才能触发额外虫灾。",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            ExecuteExtraInfestationOn(map, refreshCooldown: false);
        }

        public void DevStartHunt()
        {
            Map? map = Find.CurrentMap;
            if (map == null)
            {
                Messages.Message(
                    "需要有效的当前地图才能开始虫族追猎。",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            ExecuteHuntPendingOn(map, refreshCooldown: false);
        }

        public void DevLaunchCurrentHunt()
        {
            if (activeHuntMap == null)
            {
                Messages.Message(
                    "当前没有进行中的虫族追猎。",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            Map? map = activeHuntMap;
            LaunchAttackOn(map);
            ClearActiveHunt();
        }

        public void DevClearRuntimeState()
        {
            ClearScheduledAndActiveState();
        }
    }
}
