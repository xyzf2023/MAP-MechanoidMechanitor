using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.PerspectiveShift
{
    internal sealed class PerspectiveShiftScenarioCompatibility : PerspectiveShiftCompatibilityModule
    {
        public override string ModuleId => "PerspectiveShift.MechanitorScenario";
        public override string DisplayName => "Perspective Shift：机械师专属剧本开局";
        protected override string AppliedDetail => "专属剧本加入上游视角选择页，生成完成后接管实际机械意识宿主，不重复生成 Pawn。";
        protected override void SetEnabled(bool enabled)
        {
            PerspectiveShiftScenarioPatches.Enabled = enabled;
            if (!enabled) PerspectiveShiftScenarioPatches.ClearPendingGame();
        }

        protected override void Resolve(ModContentPack mod, List<Binding> bindings)
        {
            Type page = ResolveType(mod, "Page_ChoosePerspective");
            if (!typeof(Page).IsAssignableFrom(page) || page.IsAbstract || page.ContainsGenericParameters)
                throw new InvalidOperationException("Perspective Shift：视角选择页面类型已变化。");
            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueConstructor(page, Type.EmptyTypes,
                    out ConstructorInfo? constructor, out string failure))
                throw new InvalidOperationException(failure);
            // 页面需要后续 Ready 页以继续配置，不依赖上游在 next == null 时提前开角色页的路径。
            Method(page, "DoNext", false, typeof(void));
            Type state = PerspectiveShiftRuntime.StateType;
            Type mode = ResolveType(mod, "PlaystyleMode");
            if (!mode.IsEnum || !Enum.GetNames(mode).OrderBy(n => n).SequenceEqual(
                    new[] { "Authentic", "Director", "Dynamic", "Swap" }))
                throw new InvalidOperationException("Perspective Shift：玩法模式枚举已变化。");
            FieldInfo currentMode = Field(state, "CurrentMode", mode, true);
            MethodInfo setAvatar = Method(state, "SetAvatar", true, typeof(void), typeof(Pawn), typeof(bool));
            MethodInfo upstreamScenario = Method(ResolveType(mod, "Scenario_GetFirstConfigPage_Patch"), "Postfix", true,
                typeof(void), typeof(Page).MakeByRefType());
            RequirePatch(Method(typeof(Scenario), nameof(Scenario.GetFirstConfigPage), false, typeof(Page)),
                upstreamScenario, HarmonyPatchType.Postfix);

            MethodInfo readyPagePatch = Method(typeof(MechanoidMechanitorScenario_Scenario_GetFirstConfigPage_Patch),
                "Postfix", true, typeof(void), typeof(Scenario), typeof(Page).MakeByRefType());
            if (Harmony.GetPatchInfo(AccessTools.DeclaredMethod(typeof(Scenario), nameof(Scenario.GetFirstConfigPage)))
                    ?.Postfixes.Any(p => p.owner == ModInit.HarmonyId && p.PatchMethod == readyPagePatch) != true)
                throw new InvalidOperationException("机械师专属剧本准备页面补丁尚未绑定，跳过 Perspective Shift 开局兼容。");

            PerspectiveShiftScenarioPatches.Configure(page, constructor!, currentMode,
                Enum.Parse(mode, "Director"), setAvatar);
            bindings.Add(new Binding(readyPagePatch, typeof(PerspectiveShiftScenarioPatches),
                nameof(PerspectiveShiftScenarioPatches.ReadyPagePostfix), HarmonyPatchType.Postfix));
            bindings.Add(new Binding(Method(typeof(Game), nameof(Game.InitNewGame), false, typeof(void)),
                typeof(PerspectiveShiftScenarioPatches), nameof(PerspectiveShiftScenarioPatches.NewGamePostfix),
                HarmonyPatchType.Postfix));
            bindings.Add(new Binding(Method(typeof(GameComponent_MechanoidMechanitorRegistry),
                nameof(GameComponent_MechanoidMechanitorRegistry.GameComponentUpdate), false, typeof(void)),
                typeof(PerspectiveShiftScenarioPatches), nameof(PerspectiveShiftScenarioPatches.RegistryUpdatePostfix),
                HarmonyPatchType.Postfix));
        }
    }

    internal static class PerspectiveShiftScenarioPatches
    {
        internal static bool Enabled;
        private static Type? perspectivePageType;
        private static ConstructorInfo? pageConstructor;
        private static FieldInfo? currentMode;
        private static object? directorMode;
        private static MethodInfo? setAvatar;
        // 只记录本次插入页面所对应的 Game，不持有旧游戏，不新增存档字段。
        private static System.WeakReference<Game>? pendingGame;
        private static System.WeakReference<Game>? bindingGame;
        private const int BindingTimeoutTicks = 1800;
        private const int RetryIntervalFrames = 30;
        private static int bindingDeadlineTick;
        private static int nextBindingFrame;

        internal static void Configure(Type pageType, ConstructorInfo constructor, FieldInfo mode,
            object director, MethodInfo setter)
        {
            perspectivePageType = pageType;
            pageConstructor = constructor;
            currentMode = mode;
            directorMode = director;
            setAvatar = setter;
        }

        internal static void ClearPendingGame()
        {
            pendingGame = null;
            ClearPendingBinding();
        }

        private static void ClearPendingBinding()
        {
            bindingGame = null;
            bindingDeadlineTick = 0;
            nextBindingFrame = 0;
        }

        // 补丁作用于本 MOD 自己的 Postfix，确保 Ready 页已经构造完毕。
        public static void ReadyPagePostfix(Scenario __0, ref Page __1)
        {
            if (!Enabled || !PerspectiveShiftRuntime.CanQuery || Current.Game == null || __1 == null
                || pageConstructor == null || perspectivePageType == null
                || !MechanoidMechanitorScenarioUtility.ScenarioContainsMarker(__0))
                return;
            var visited = new HashSet<Page>();
            Page? ready = null;
            for (Page? page = __1; page != null; page = page.next)
            {
                if (!visited.Add(page))
                {
                    Log.Error("[MAP-机械族机械师] Perspective Shift：开局页面链存在循环，未追加视角页面。");
                    return;
                }
                if (perspectivePageType.IsInstanceOfType(page))
                    return;
                if (page is Page_MechanoidMechanitorScenarioReady)
                    ready = page;
            }
            if (ready == null)
                return;
            Page? previous = ready.prev;
            if (previous != null && (previous.next != ready || previous.nextAct != null))
            {
                Log.Error("[MAP-机械族机械师] Perspective Shift：准备页面连接已变化，未追加视角页面。");
                return;
            }
            Page perspective = (Page)pageConstructor.Invoke(Array.Empty<object>());
            perspective.prev = previous;
            perspective.next = ready;
            if (previous == null)
                __1 = perspective;
            else
                previous.next = perspective;
            ready.prev = perspective;
            pendingGame = new System.WeakReference<Game>(Current.Game);
        }

        public static void NewGamePostfix(Game __instance)
        {
            System.WeakReference<Game>? pending = pendingGame;
            ClearPendingGame();
            if (!Enabled || pending == null || !pending.TryGetTarget(out Game game)
                || !ReferenceEquals(game, __instance))
                return;
            // 地图生成完成不代表运输舱已经打开；之后还需等待宿主入场及现有初始化队列。
            LongEventHandler.ExecuteWhenFinished(() => BeginHostBinding(game));
        }

        private static void BeginHostBinding(Game game)
        {
            if (!Enabled || !ReferenceEquals(Current.Game, game))
                return;
            bindingGame = new System.WeakReference<Game>(game);
            // 按游戏 Tick 计时：暂停开局等待运输舱时，不消耗等待期限。
            bindingDeadlineTick = (Find.TickManager?.TicksGame ?? 0) + BindingTimeoutTicks;
            nextBindingFrame = RealTime.frameCount + RetryIntervalFrames;
            TryBindGeneratedHost(game);
        }

        public static void RegistryUpdatePostfix()
        {
            System.WeakReference<Game>? pending = bindingGame;
            if (!Enabled || pending == null)
                return;
            if (!pending.TryGetTarget(out Game game) || !ReferenceEquals(Current.Game, game))
            {
                ClearPendingBinding();
                return;
            }
            if (RealTime.frameCount < nextBindingFrame)
                return;
            nextBindingFrame = RealTime.frameCount + RetryIntervalFrames;
            // 在注册表本帧的初始化队列处理之后，仅重查本次开局的宿主，不扫描 Pawn 名单。
            TryBindGeneratedHost(game);
        }

        private static void TryBindGeneratedHost(Game game)
        {
            if (!Enabled || !ReferenceEquals(Current.Game, game)
                || !MechanoidMechanitorScenarioUtility.IsScenarioActive || currentMode == null || setAvatar == null
                || Equals(currentMode.GetValue(null), directorMode) || PerspectiveShiftRuntime.CurrentPawn != null)
            {
                ClearPendingBinding();
                return;
            }
            if (Current.ProgramState != ProgramState.Playing || !PerspectiveShiftRuntime.CanQuery
                || LongEventHandler.AnyEventNowOrWaiting)
                return;
            Pawn? host = GameComponent_MechanoidMechanitorRegistry.CurrentMechanicalConsciousnessHost;
            if (host != null && host.Spawned && host.Map != null && PerspectiveShiftRuntime.CanOperate(host))
            {
                // 先结束待绑定状态，避免上游调用期间的重入或异常造成重复接管。
                ClearPendingBinding();
                setAvatar.Invoke(null, new object[] { host, true });
                if (!ReferenceEquals(PerspectiveShiftRuntime.CurrentPawn, host))
                    Log.Warning("[MAP-机械族机械师] Perspective Shift：上游接管入口未接受开局宿主 "
                        + host.ThingID + "，保留当前视角。");
                return;
            }
            if ((Find.TickManager?.TicksGame ?? 0) < bindingDeadlineTick)
                return;
            ClearPendingBinding();
            Log.Warning("[MAP-机械族机械师] Perspective Shift：等待开局宿主入场及初始化超时，保留当前视角。"
                + $"宿主={host?.ThingID ?? "null"}；原因：{DescribeUnavailableHost(host)}。");
        }

        private static string DescribeUnavailableHost(Pawn? host)
        {
            if (host == null)
                return "尚未登记机械意识宿主";
            if (!PerspectiveShiftRuntime.IsPlayerMechanitor(host))
                return "宿主未满足存活、已注册的自由玩家机械师资格";
            if (!host.Spawned || host.Map == null)
                return "宿主尚未实际生成在地图上，可能仍在入场容器内";
            var missing = new List<string>();
            if (host.jobs == null) missing.Add("jobs");
            if (host.pather == null) missing.Add("pather");
            if (host.stances == null) missing.Add("stances");
            if (host.drafter == null) missing.Add("drafter");
            if (host.needs == null) missing.Add("needs");
            if (host.thinker == null) missing.Add("thinker");
            if (host.equipment == null) missing.Add("equipment");
            if (host.story == null) missing.Add("story");
            if (host.skills == null) missing.Add("skills");
            if (host.playerSettings == null) missing.Add("playerSettings");
            if (missing.Count != 0)
                return "缺少控制所需 Tracker：" + string.Join(", ", missing);
            if (host.Downed) return "宿主失能";
            if (host.InMentalState) return "宿主处于精神状态";
            if (host.Deathresting) return "宿主处于死亡休眠";
            if (host.health.capacities?.CanBeAwake != true) return "宿主当前无法保持清醒";
            if (host.needs?.energy?.IsLowEnergySelfShutdown == true) return "宿主处于低能量停机";
            if (!MechTransformationUtility.IsInPawnForm(host)) return "宿主处于形态转换或非 Pawn 形态";
            if (MechanicalFlightEmergencyUtility.IsEmergencySequence(host)) return "宿主处于紧急飞行流程";
            return "宿主尚未通过接管安全检查";
        }
    }
}
