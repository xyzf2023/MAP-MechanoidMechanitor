using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using GD3;
using HarmonyLib;
using MAP_MechanoidMechanitor.Compatibility.ThirdParty.GlitterworldDestroyer5;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.GD5
{
    /// <summary>GD5 本体玩法兼容。各功能独立校验、安装和回滚，不依赖黑衣剧情初始化成功。</summary>
    [StaticConstructorOnStartup]
    internal static class GD5GameplayCompatibilityBootstrap
    {
        private const string PackageId = "fxz.glitterworlddestroyer.mk5";

        static GD5GameplayCompatibilityBootstrap()
        {
            Install("modify-saving-mech", "动手脚资格", InstallModifySavingMech);
            Install("drysea", "枯海白花资格", InstallDrysea);
            Install("cataphract-weapon", "禁卫机械蜈蚣武器切换", InstallWeapon);
            Install("cerebrex-research", "主脑接管集群科技", InstallTakeoverResearch);
            Install("purge-research", "肃清评级科技支持", InstallPurgeResearch);
            Install("cluster-receiver", "巨型集群接收器", InstallClusterReceiver);
        }

        private static void Install(string id, string label, Action<Harmony, ModContentPack> install)
        {
            string harmonyId = "xyzf.mechanoidmechanitor.gd5.gameplay." + id;
            var harmony = new Harmony(harmonyId);
            try
            {
                ModContentPack? mod = LoadedModManager.RunningModsListForReading.Find(
                    m => string.Equals(m.PackageId, PackageId, StringComparison.OrdinalIgnoreCase));
                if (mod == null) return;
                install(harmony, mod);
            }
            catch (Exception exception)
            {
                // 每个功能使用独立 ID，不撤销其他玩法模块或剧情/通讯模块的补丁。
                try
                {
                    harmony.UnpatchAll(harmonyId);
                }
                catch (Exception rollbackException)
                {
                    Log.Error("[MAP-GD5] " + label + "补丁回滚失败。\n" + rollbackException);
                }
                Log.Error("[MAP-GD5] " + label + "兼容安装失败。\n" + exception);
                return;
            }
            if (MAPMechanitorMod.Settings?.enableStartupDetailedLogging == true)
            {
                Log.Message("[MAP-GD5] " + label + "兼容已加载。");
            }
        }

        // 不内联：第三方类型/成员解析异常留在各模块的安装异常边界内。
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void InstallModifySavingMech(Harmony harmony, ModContentPack mod)
        {
            RequireOwnedType(mod, typeof(FloatMenuOptionProvider_ModifySavingMech));
            MethodInfo target = RequireMethod(typeof(FloatMenuOptionProvider_ModifySavingMech),
                "CanTakeOrder", typeof(bool), new[] { typeof(Pawn) });
            harmony.Patch(target, postfix: Patch(typeof(ModifySavingMechCompatibilityPatch),
                nameof(ModifySavingMechCompatibilityPatch.Postfix)));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void InstallDrysea(Harmony harmony, ModContentPack mod)
        {
            RequireOwnedType(mod, typeof(DryseaDummy));
            MethodInfo target = RequireMethod(typeof(DryseaDummy),
                "get_" + nameof(DryseaDummy.IfPawnStanding), typeof(bool), Type.EmptyTypes);
            harmony.Patch(target, postfix: Patch(typeof(DryseaStandingCompatibilityPatch),
                nameof(DryseaStandingCompatibilityPatch.Postfix)));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void InstallWeapon(Harmony harmony, ModContentPack mod)
        {
            RequireOwnedType(mod, typeof(CompChangeWeaponB));
            MethodInfo target = RequireMethod(typeof(CompChangeWeaponB),
                nameof(CompChangeWeaponB.CompGetGizmosExtra), typeof(IEnumerable<Gizmo>), Type.EmptyTypes);
            ThingDef sr = OwnedDef<ThingDef>(mod, "CataphractCentipede_SR");
            ThingDef fy = OwnedDef<ThingDef>(mod, "CataphractCentipede_FY");
            SoundDef sound = DefDatabase<SoundDef>.GetNamed("Interact_ChargeRifle");
            harmony.Patch(target, postfix: Patch(typeof(CataphractCentipedeWeaponCompatibilityPatch),
                nameof(CataphractCentipedeWeaponCompatibilityPatch.Postfix)));
            CataphractCentipedeWeaponCompatibilityPatch.Configure(sr, fy, sound);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void InstallTakeoverResearch(Harmony harmony, ModContentPack mod)
        {
            ResearchProjectDef research = OwnedDef<ResearchProjectDef>(mod, "GD3_GiantCluster_Ultra");
            MethodInfo target = RequireMethod(typeof(GameComponent_CerebrexTakeoverState),
                nameof(GameComponent_CerebrexTakeoverState.CompleteTakeover), typeof(bool),
                new[] { typeof(CompCerebrexCore), typeof(Pawn) });
            harmony.Patch(target,
                prefix: Patch(typeof(CerebrexTakeoverResearchCompatibilityPatch),
                    nameof(CerebrexTakeoverResearchCompatibilityPatch.Prefix)),
                postfix: Patch(typeof(CerebrexTakeoverResearchCompatibilityPatch),
                    nameof(CerebrexTakeoverResearchCompatibilityPatch.Postfix)));
            CerebrexTakeoverResearchCompatibilityPatch.Configure(research);
            // 保留主 DLL 的旧存档组件，只注册无游戏实例捕获的静态恢复入口。
            GameComponent_GD5ResearchSupport.RegisterTakeoverRecovery(
                CerebrexTakeoverResearchCompatibilityPatch.EnsureTakeoverResearchCompleted);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void InstallPurgeResearch(Harmony harmony, ModContentPack mod)
        {
            ResearchProjectDef medium = OwnedDef<ResearchProjectDef>(mod, "GD3_GiantCluster_Medium");
            ResearchProjectDef large = OwnedDef<ResearchProjectDef>(mod, "GD3_GiantCluster_Large");
            ResearchProjectDef ultra = OwnedDef<ResearchProjectDef>(mod, "GD3_GiantCluster_Ultra");
            MethodInfo target = RequireMethod(typeof(PurgeDirectiveRatingUtility),
                "NotifyRatingChanged", typeof(void), new[] { typeof(int), typeof(int) }, isStatic: true);
            harmony.Patch(target, postfix: Patch(typeof(PurgeDirectiveResearchCompatibilityPatch),
                nameof(PurgeDirectiveResearchCompatibilityPatch.Postfix)));
            PurgeDirectiveResearchCompatibilityPatch.Configure(medium, large, ultra);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void InstallClusterReceiver(Harmony harmony, ModContentPack mod)
        {
            RequireOwnedType(mod, typeof(CompUseEffect_Detection));
            RequireOwnedType(mod, typeof(CompReceiverSelect));
            RequireOwnedType(mod, typeof(GDSettings));
            ResearchProjectDef medium = OwnedDef<ResearchProjectDef>(mod, "GD3_GiantCluster_Medium");
            ResearchProjectDef large = OwnedDef<ResearchProjectDef>(mod, "GD3_GiantCluster_Large");
            ResearchProjectDef ultra = OwnedDef<ResearchProjectDef>(mod, "GD3_GiantCluster_Ultra");
            MethodInfo canUse = RequireMethod(typeof(CompUseEffect_Detection),
                nameof(CompUseEffect_Detection.CanBeUsedBy), typeof(AcceptanceReport), new[] { typeof(Pawn) });
            MethodInfo effect = RequireMethod(typeof(CompUseEffect_Detection),
                nameof(CompUseEffect_Detection.DoEffect), typeof(void), new[] { typeof(Pawn) });
            RequireMethod(typeof(CompUseEffect_Detection), "get_" + nameof(CompUseEffect_Detection.CompSelect),
                typeof(CompReceiverSelect), Type.EmptyTypes);
            RequireMethod(typeof(CompReceiverSelect), "get_" + nameof(CompReceiverSelect.Mark),
                typeof(int), Type.EmptyTypes);
            FieldInfo delay = RequireField(typeof(CompUseEffect_Detection), "delayTicks",
                BindingFlags.Instance | BindingFlags.NonPublic);
            RequireField(typeof(GDSettings), nameof(GDSettings.DetectCooldown),
                BindingFlags.Static | BindingFlags.Public);
            HarmonyMethod canUsePrefix = Patch(typeof(ClusterReceiverCompatibilityPatch),
                nameof(ClusterReceiverCompatibilityPatch.CanBeUsedByPrefix));
            HarmonyMethod effectPrefix = Patch(typeof(ClusterReceiverCompatibilityPatch),
                nameof(ClusterReceiverCompatibilityPatch.DoEffectPrefix));
            harmony.Patch(canUse, prefix: canUsePrefix);
            harmony.Patch(effect, prefix: effectPrefix);
            ClusterReceiverCompatibilityPatch.Configure(delay, medium, large, ultra);
        }

        private static MethodInfo RequireMethod(Type type, string name, Type returnType,
            Type[] parameters, bool isStatic = false)
        {
            MethodInfo? method = AccessTools.DeclaredMethod(type, name, parameters);
            if (method == null || method.ReturnType != returnType || method.IsStatic != isStatic)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static FieldInfo RequireField(Type type, string name, BindingFlags flags)
        {
            FieldInfo? field = type.GetField(name, flags | BindingFlags.DeclaredOnly);
            if (field == null || field.FieldType != typeof(int))
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static HarmonyMethod Patch(Type type, string name)
        {
            MethodInfo? method = AccessTools.DeclaredMethod(type, name);
            if (method == null || !method.IsStatic) throw new MissingMethodException(type.FullName, name);
            return new HarmonyMethod(method);
        }

        private static void RequireOwnedType(ModContentPack mod, Type type)
        {
            if (!mod.assemblies.loadedAssemblies.Contains(type.Assembly))
                throw new InvalidOperationException("目标类型不属于 GD5 本体：" + type.FullName);
        }

        private static T OwnedDef<T>(ModContentPack mod, string name) where T : Def
        {
            T? def = DefDatabase<T>.GetNamedSilentFail(name);
            if (def == null || !ReferenceEquals(def.modContentPack, mod))
                throw new InvalidOperationException("GD5 Def 缺失或来源不符：" + name);
            return def;
        }
    }
}
