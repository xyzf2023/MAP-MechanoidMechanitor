using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor.GD5Expansion;

internal sealed class LegacyInstallationPatches
{
    private const string PatchTypeName = "MAP_MechanoidMechanitor.Compatibility.ThirdParty."
        + "GlitterworldDestroyer5Expansion.GlitterworldDestroyer5ExpansionInstallationPatch";
    private readonly PatchBinding[] bindings;
    private readonly Harmony harmony = new(ExpansionCompatibilityBootstrap.LegacyOwner);

    private LegacyInstallationPatches(PatchBinding[] bindings) => this.bindings = bindings;

    internal static LegacyInstallationPatches Resolve(ModContentPack map, ModContentPack expansion)
    {
        Type patchType = ReflectionTargets.RequireType(map, PatchTypeName);
        if (patchType.Assembly.GetName().Name != "MAP-MechanoidMechanitor")
            throw new InvalidOperationException("旧兼容补丁不属于已知 MAP 主程序集。");
        MethodInfo effectPatch = ReflectionTargets.RequireMethod(patchType, "TranspilerDoEffectOn", true,
            typeof(IEnumerable<CodeInstruction>), typeof(IEnumerable<CodeInstruction>));
        MethodInfo waitPatch = ReflectionTargets.RequireMethod(patchType, "TranspilerMakeNewToils", true,
            typeof(IEnumerable<CodeInstruction>), typeof(IEnumerable<CodeInstruction>));
        Type effectType = ReflectionTargets.RequireType(expansion, "MK3expand.CompTargetEffect_GiveHediffToPlayMech");
        Type driverType = ReflectionTargets.RequireType(expansion, "MK3expand.JobDriver_GiveHediffToMech");
        if (effectType.Assembly.GetName().Name != "MK3expand"
            || driverType.Assembly != effectType.Assembly)
            throw new InvalidOperationException("旧兼容目标不属于唯一闪拓主程序集。");
        MethodInfo effect = ReflectionTargets.RequireMethod(effectType, "DoEffectOn", false,
            typeof(void), typeof(Pawn), typeof(Thing));
        MethodInfo makeToils = ReflectionTargets.RequireMethod(driverType, "MakeNewToils", false,
            typeof(IEnumerable<Toil>));
        MethodInfo? moveNext = AccessTools.EnumeratorMoveNext(makeToils);
        if (moveNext == null || moveNext.IsStatic || moveNext.ReturnType != typeof(bool)
            || moveNext.GetParameters().Length != 0 || moveNext.DeclaringType == null
            || moveNext.DeclaringType.Assembly != effectType.Assembly
            || moveNext.DeclaringType.GetFields(BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.DeclaredOnly).Count(field => field.FieldType == driverType) != 1)
            throw new InvalidOperationException("安装等待状态机或捕获字段已变化。");
        MethodInfo colonist = ReflectionTargets.RequireMethod(typeof(Pawn), "get_IsColonistPlayerControlled", false,
            typeof(bool));
        MethodInfo waitWith = ReflectionTargets.RequireMethod(typeof(Toils_General), "WaitWith", true,
            typeof(Toil), typeof(TargetIndex), typeof(int), typeof(bool), typeof(bool), typeof(bool),
            typeof(TargetIndex), typeof(PathEndMode));
        if (PatchProcessor.GetOriginalInstructions(effect).Count(code => code.Calls(colonist)) != 1
            || PatchProcessor.GetOriginalInstructions(moveNext).Count(code => code.Calls(waitWith)) != 1)
            throw new InvalidOperationException("旧兼容必须保留单一殖民者资格与 WaitWith 锚点。");
        return new LegacyInstallationPatches([
            new PatchBinding(effect, effectPatch), new PatchBinding(moveNext, waitPatch)
        ]);
    }

    // 只接管完整且已知的旧补丁对；缺失、残缺或属于其他 owner 的补丁不予处理。
    internal void RequireComplete()
    {
        foreach (PatchBinding binding in bindings)
        {
            Patch[] patches = binding.Find();
            if (patches.Length != 1 || patches[0].owner != ExpansionCompatibilityBootstrap.LegacyOwner)
                throw new InvalidOperationException("未找到成套且 owner 精确匹配的 MAP 旧补丁：" + binding.PatchMethod.Name);
            binding.OriginalPatch = PatchSnapshot.Capture(patches[0]);
        }
    }

    internal void RemoveBoth()
    {
        foreach (PatchBinding binding in bindings)
        {
            // 每次改动前立即复查，避免移除其他 owner 的补丁。
            Patch[] patches = binding.Find();
            if (patches.Length != 1 || !binding.OriginalPatch!.Matches(patches[0], includeIndex: true))
                throw new InvalidOperationException("旧补丁状态在接管过程中发生变化。");
            harmony.Unpatch(binding.Target, binding.PatchMethod);
            if (binding.Find().Length != 0)
                throw new InvalidOperationException("旧补丁移除未生效：" + binding.PatchMethod.Name);
        }
    }

    internal bool AnyPresent() => bindings.Any(binding => binding.Find().Length != 0);

    internal bool RestoreBoth()
    {
        bool restored = true;
        foreach (PatchBinding binding in bindings)
        {
            try
            {
                Patch[] current = binding.Find();
                PatchSnapshot previous = binding.OriginalPatch
                    ?? throw new InvalidOperationException("缺少原补丁快照。");
                if (current.Length == 0)
                {
                    harmony.Patch(binding.Target, transpiler: new HarmonyMethod(binding.PatchMethod)
                    {
                        priority = previous.Priority,
                        before = previous.Before,
                        after = previous.After,
                        debug = previous.Debug
                    });
                    current = binding.Find();
                }
                if (current.Length != 1 || !previous.Matches(current[0], includeIndex: false))
                    throw new InvalidOperationException("无法确认旧补丁已恢复。");
            }
            catch (Exception ex)
            {
                restored = false;
                ExpansionCompatibilityLog.Warning("安装与自身等待旧补丁回滚失败。", "旧补丁回滚异常。", ex);
            }
        }
        return restored;
    }

    // Harmony 可能在每次 GetPatchInfo 调用时反序列化出新的 Patch 实例。
    private sealed record PatchSnapshot(MethodInfo Method, string Owner, int Index, int Priority,
        string[] Before, string[] After, bool Debug)
    {
        internal static PatchSnapshot Capture(Patch patch) => new(patch.PatchMethod, patch.owner,
            patch.index, patch.priority, (patch.before ?? []).ToArray(), (patch.after ?? []).ToArray(), patch.debug);

        internal bool Matches(Patch patch, bool includeIndex) => Method == patch.PatchMethod
            && Owner == patch.owner && (!includeIndex || Index == patch.index)
            && Priority == patch.priority && Debug == patch.debug
            && Before.SequenceEqual(patch.before ?? []) && After.SequenceEqual(patch.after ?? []);
    }

    private sealed class PatchBinding(MethodInfo target, MethodInfo patchMethod)
    {
        internal readonly MethodInfo Target = target;
        internal readonly MethodInfo PatchMethod = patchMethod;
        internal PatchSnapshot? OriginalPatch;
        internal Patch[] Find() => Harmony.GetPatchInfo(Target)?.Transpilers
            .Where(patch => patch.PatchMethod == PatchMethod).ToArray() ?? [];
    }
}
