using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace MAP_MechanoidMechanitor.GD5Expansion;

// 独立的 1.7 程序集：现有 MAP 主程序集与 GD5 程序集仅作为运行时输入。
[StaticConstructorOnStartup]
internal static class ExpansionCompatibilityBootstrap
{
    internal const string SourceId = "xyzf.mechanoidmechanitor.gd5expansion";
    internal const string LegacyOwner = "xyzf.map.mechanoidmechanitor";
    internal const string LogPrefix = "[MAP-机械族机械师] 闪拓 1.7 兼容：";
    private static bool initialized;

    static ExpansionCompatibilityBootstrap() => LongEventHandler.ExecuteWhenFinished(Initialize);

    private static void Initialize()
    {
        if (initialized) return;
        initialized = true;
        try
        {
            ModContentPack? map = ReflectionTargets.FindMod("xyzf.mechanoidmechanitor");
            ModContentPack? expansion = ReflectionTargets.FindMod("qiuci.GD5.Expansion");
            if (map == null || expansion == null) return;

            // 只在真实的 MAP Mod 包内解析类型，并让其既有引导逻辑先完成初始化。
            Type modInit = ReflectionTargets.RequireType(map, "MAP_MechanoidMechanitor.ModInit");
            if (modInit.Assembly.GetName().Name != "MAP-MechanoidMechanitor"
                || modInit.GetField("HarmonyId", BindingFlags.Public | BindingFlags.Static
                    | BindingFlags.DeclaredOnly)?.GetRawConstantValue() as string != LegacyOwner)
                throw new InvalidOperationException("MAP 主程序集或既有 Harmony owner 已变化。");
            RuntimeHelpers.RunClassConstructor(modInit.TypeHandle);

            LegacyShieldControlPatch.EnsureControlGate(expansion);

            Type? apiType = ReflectionTargets.FindType(expansion, "MK3expand.ModificationCompatibilityApi");
            ExpansionApi? api = null;
            if (apiType != null)
            {
                try { api = ExpansionApi.Resolve(apiType); }
                catch (Exception ex) { Log.Warning(LogPrefix + "API 协议不匹配，检查旧兼容分支。" + ex); }
            }
            LegacyInstallationPatches oldPatches = LegacyInstallationPatches.Resolve(map, expansion);
            oldPatches.RequireComplete();
            if (api == null)
            {
                Log.Message(LogPrefix + "使用旧版分支；保留既有安装/自身等待补丁。");
                return;
            }
            AuthorizedMechanitor.Configure(map);
            SwitchToApi(api, oldPatches);
        }
        catch (Exception ex)
        {
            Log.Warning(LogPrefix + "未切换兼容分支，保留已存在的旧补丁。" + ex);
        }
    }

    private static void SwitchToApi(ExpansionApi api, LegacyInstallationPatches legacy)
    {
        bool registrationAccepted = false;
        AuthorizedMechanitor.Enabled = false;
        try
        {
            if (api.IsRegistered(SourceId))
                throw new InvalidOperationException("来源 ID 已被注册，不能接管未知 API 注册。");

            // 注册为原子操作；两项旧补丁移除之前，回调始终不生效。
            registrationAccepted = api.TryRegister(1, SourceId, AuthorizedMechanitor.CanInstall, true);
            if (!registrationAccepted || !api.IsRegistered(SourceId))
                throw new InvalidOperationException("闪拓 API 未接受完整安装/自身等待注册。");

            legacy.RemoveBoth();
            if (legacy.AnyPresent())
                throw new InvalidOperationException("旧兼容补丁仍然存在，API 分支保持禁用。");
            AuthorizedMechanitor.Enabled = true;
            Log.Message(LogPrefix + "使用 API v1；已原子注册安装与自身等待资格，并撤下两项旧补丁。");
        }
        catch (Exception ex)
        {
            AuthorizedMechanitor.Enabled = false;
            if (!registrationAccepted)
            {
                Log.Warning(LogPrefix + "API 注册未取得所有权；没有撤销任何注册或旧补丁。" + ex);
                return;
            }
            bool apiRemoved = false;
            if (registrationAccepted)
            {
                try
                {
                    api.Unregister(SourceId);
                    apiRemoved = !api.IsRegistered(SourceId);
                }
                catch (Exception rollback) { Log.Warning(LogPrefix + "撤销 API 注册异常：" + rollback); }
            }
            bool legacyRestored = apiRemoved && legacy.RestoreBoth();
            Log.Warning(LogPrefix + (apiRemoved && legacyRestored
                ? "API 切换失败，已确认退回完整旧兼容分支。"
                : "API 切换失败且回滚不完整；新回调保持禁用，未安装任何替代分支。") + ex);
        }
    }
}

internal static class AuthorizedMechanitor
{
    internal static bool Enabled;
    private static Func<Pawn, bool>? isMechanitor;
    private static Func<Pawn, bool>? canOperate;

    internal static void Configure(ModContentPack map)
    {
        Type role = ReflectionTargets.RequireType(map, "MAP_MechanoidMechanitor.MechanoidMechanitorRoleUtility");
        Type menu = ReflectionTargets.RequireType(map, "MAP_MechanoidMechanitor.CompColonistLikeFloatMenuUser");
        isMechanitor = ReflectionTargets.RequireMethod(role, "IsMechanoidMechanitor", true,
            typeof(bool), typeof(Pawn)).CreateDelegate<Func<Pawn, bool>>();
        canOperate = ReflectionTargets.RequireMethod(menu, "PawnCanUseColonistLikeFloatMenu", true,
            typeof(bool), typeof(Pawn)).CreateDelegate<Func<Pawn, bool>>();
    }

    // 复用 MAP 的权威查询；不在此初始化 pawn 或创建/写入角色数据。
    internal static bool CanInstall(Pawn pawn) => Enabled
        && pawn != null
        && isMechanitor != null && isMechanitor(pawn)
        && canOperate != null && canOperate(pawn);
}

internal sealed class ExpansionApi
{
    internal required Func<int, string, Func<Pawn, bool>, bool, bool> TryRegister { get; init; }
    internal required Func<string, bool> IsRegistered { get; init; }
    internal required Func<string, bool> Unregister { get; init; }

    internal static ExpansionApi Resolve(Type type)
    {
        if (type.Assembly.GetName().Name != "MK3expand" || !type.IsPublic || !type.IsAbstract || !type.IsSealed)
            throw new InvalidOperationException("闪拓 API 类型的程序集或 public static 声明不符。");
        MethodInfo version = ReflectionTargets.RequireMethod(type, "get_ProtocolVersion", true, typeof(int));
        MethodInfo register = ReflectionTargets.RequireMethod(type, "TryRegister", true, typeof(bool),
            typeof(int), typeof(string), typeof(Func<Pawn, bool>), typeof(bool));
        MethodInfo query = ReflectionTargets.RequireMethod(type, "IsRegistered", true, typeof(bool), typeof(string));
        MethodInfo unregister = ReflectionTargets.RequireMethod(type, "Unregister", true, typeof(bool), typeof(string));
        if (!version.IsPublic || !register.IsPublic || !query.IsPublic || !unregister.IsPublic
            || version.CreateDelegate<Func<int>>()() != 1)
            throw new InvalidOperationException("闪拓 API 必须完整公开并提供协议版本 1。");
        return new ExpansionApi
        {
            TryRegister = register.CreateDelegate<Func<int, string, Func<Pawn, bool>, bool, bool>>(),
            IsRegistered = query.CreateDelegate<Func<string, bool>>(),
            Unregister = unregister.CreateDelegate<Func<string, bool>>()
        };
    }
}

internal static class ReflectionTargets
{
    internal static ModContentPack? FindMod(string packageId)
    {
        ModContentPack[] matches = LoadedModManager.RunningModsListForReading
            .Where(mod => string.Equals(mod.PackageId, packageId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length > 1) throw new InvalidOperationException("重复加载 packageId：" + packageId);
        return matches.SingleOrDefault();
    }

    internal static Type? FindType(ModContentPack mod, string name)
    {
        Type[] matches = mod.assemblies.loadedAssemblies.Select(assembly => assembly.GetType(name, false, false))
            .OfType<Type>().Distinct().ToArray();
        if (matches.Length > 1) throw new InvalidOperationException("目标类型不唯一：" + name);
        return matches.SingleOrDefault();
    }

    internal static Type RequireType(ModContentPack mod, string name) => FindType(mod, name)
        ?? throw new MissingMemberException("目标类型缺失：" + name);

    internal static MethodInfo RequireMethod(Type type, string name, bool isStatic, Type result,
        params Type[] parameters)
    {
        MethodInfo[] matches = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == name && method.IsStatic == isStatic
                && !method.ContainsGenericParameters && method.ReturnType == result
                && method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameters))
            .ToArray();
        if (matches.Length != 1) throw new MissingMethodException(type.FullName,
            name + "（完整签名必须唯一）");
        return matches[0];
    }
}