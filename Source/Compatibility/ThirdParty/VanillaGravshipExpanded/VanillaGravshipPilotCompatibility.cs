using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.VanillaGravshipExpanded
{
    // 驾驶支持与起飞状态保护分开安装、诊断及回滚。
    internal sealed class VanillaGravshipPilotCompatibility : IThirdPartyCompatibilityModule
    {
        public string ModuleId => "VanillaGravshipExpanded.MechPilot";
        public string DisplayName => "Vanilla Gravship Expanded：机械驾驶与船员";
        public string PackageId => VanillaGravshipLaunchStateCompatibility.ModPackageId;

        public ThirdPartyCompatibilityResult Apply(Harmony harmony)
        {
            GravshipLaunchRitualUtility.SetAdditionalLaunchDefs(null, null);
            ModContentPack? mod = ThirdPartyCompatibilityTargetResolver.FindRunningMod(PackageId);
            if (mod == null)
                return ThirdPartyCompatibilityResult.CreateInactive(ModuleId, DisplayName, PackageId);
            MethodInfo? target = null;
            MethodInfo? patch = null;
            bool attempted = false;
            try
            {
                if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(mod,
                        "VanillaGravshipExpanded.VGEDefOf", out Type? defOf, out string failure)
                    || !ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(mod,
                        "VanillaGravshipExpanded.JobDriver_PilotConsole_MakeNewToils_Patch",
                        out Type? jobPatch, out failure))
                    return Changed(failure);

                if (!TryReadLaunchDef(mod, defOf!, "VGE_GravjumperLaunch",
                        out PreceptDef? jumper, out FieldInfo? jumperField, out failure)
                    || !TryReadLaunchDef(mod, defOf!, "VGE_GravhulkLaunch",
                        out PreceptDef? hulk, out FieldInfo? hulkField, out failure)
                    || !TryReadConsoleDef(mod, defOf!, "VGE_PilotCockpit", out FieldInfo? cockpit, out failure)
                    || !TryReadConsoleDef(mod, defOf!, "VGE_PilotBridge", out FieldInfo? bridge, out failure))
                    return Changed(failure);

                MethodInfo? upstream = AccessTools.DeclaredMethod(jobPatch, "Postfix",
                    new[] { typeof(IEnumerable<Verse.AI.Toil>), typeof(JobDriver_PilotConsole) });
                if (upstream == null || !upstream.IsStatic
                    || upstream.ReturnType != typeof(IEnumerable<Verse.AI.Toil>)
                    || upstream.GetMethodBody() == null)
                    return Changed("VGE 驾驶任务 Postfix 的签名已变化。");

                VanillaGravshipPilotInitActionPatch.Configure(jumperField!, hulkField!);
                List<MethodInfo> matches = new List<MethodInfo>();
                foreach (Type nested in EnumerateNestedTypes(jobPatch!))
                {
                    if (!nested.IsDefined(typeof(CompilerGeneratedAttribute), false))
                        continue;
                    foreach (MethodInfo candidate in nested.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                        | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    {
                        if (!candidate.Name.StartsWith("<Postfix>b__", StringComparison.Ordinal)
                            || candidate.ReturnType != typeof(void) || candidate.GetParameters().Length != 0
                            || candidate.ContainsGenericParameters || candidate.GetMethodBody() == null)
                            continue;
                        List<CodeInstruction> codes = new List<CodeInstruction>(
                            PatchProcessor.GetOriginalInstructions(candidate));
                        if (codes.Count(c => c.opcode == System.Reflection.Emit.OpCodes.Ldsfld
                                && Equals(c.operand, cockpit)) != 1
                            || codes.Count(c => c.opcode == System.Reflection.Emit.OpCodes.Ldsfld
                                && Equals(c.operand, bridge)) != 1)
                            continue;
                        if (VanillaGravshipPilotInitActionPatch.TryFindAnchors(codes, out _, out _, out _))
                            matches.Add(candidate);
                    }
                }
                if (matches.Count != 1)
                    return Changed($"VGE 驾驶台 initAction 预期唯一，实际找到 {matches.Count} 个；未扩展仪式资格。");
                target = matches[0];
                patch = AccessTools.DeclaredMethod(typeof(VanillaGravshipPilotInitActionPatch),
                    nameof(VanillaGravshipPilotInitActionPatch.Transpiler));
                if (patch == null)
                    throw new MissingMethodException(nameof(VanillaGravshipPilotInitActionPatch.Transpiler));
                attempted = true;
                harmony.Patch(target, transpiler: new HarmonyMethod(patch));
                // 全部目标与安装均成功后才开放两个 Def，失败时原版支持仍保留。
                GravshipLaunchRitualUtility.SetAdditionalLaunchDefs(jumper, hulk);
                return ThirdPartyCompatibilityResult.CreateApplied(ModuleId, DisplayName, PackageId,
                    "Gravjumper/Gravhulk 复用机械驾驶候选、pilot/copilot、船员及登船保护；空 Ideo 安全解析。");
            }
            catch (Exception ex)
            {
                GravshipLaunchRitualUtility.SetAdditionalLaunchDefs(null, null);
                if (attempted && target != null && patch != null)
                {
                    try { harmony.Unpatch(target, patch); }
                    catch { /* 仅回滚本模块，保留原安装异常。 */ }
                }
                return ThirdPartyCompatibilityResult.CreateFailed(ModuleId, DisplayName, PackageId,
                    "机械驾驶兼容安装失败，已关闭新增资格并尝试回滚本模块补丁。", ex,
                    target == null ? Array.Empty<MethodBase>() : new MethodBase[] { target });
            }
        }

        private static IEnumerable<Type> EnumerateNestedTypes(Type type)
        {
            foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                yield return nested;
                foreach (Type descendant in EnumerateNestedTypes(nested))
                    yield return descendant;
            }
        }

        private static bool TryReadLaunchDef(ModContentPack mod, Type defOf, string name,
            out PreceptDef? def, out FieldInfo? field, out string failure)
        {
            def = null;
            if (!TryReadDefField(mod, defOf, name, typeof(PreceptDef), out field, out failure))
                return false;
            def = field!.GetValue(null) as PreceptDef;
            RitualBehaviorDef? behavior = def?.ritualPatternBase?.ritualBehavior;
            if (def == null || !typeof(Precept_GravshipLaunch).IsAssignableFrom(def.preceptClass)
                || behavior?.workerClass == null
                || !typeof(RitualBehaviorWorker_GravshipLaunch).IsAssignableFrom(behavior.workerClass)
                || behavior.roles == null
                || behavior.roles.Count(r => r is RitualRoleColonist && r.id == "pilot" && r.required) != 1
                || behavior.roles.Count(r => r is RitualRoleColonist && r.id == "copilot") != 1)
            {
                failure = $"VGE 的 {name} 不再具备预期起飞类型与驾驶角色。";
                return false;
            }
            return true;
        }

        private static bool TryReadConsoleDef(ModContentPack mod, Type defOf, string name,
            out FieldInfo? field, out string failure)
        {
            if (!TryReadDefField(mod, defOf, name, typeof(ThingDef), out field, out failure))
                return false;
            ThingDef? def = field!.GetValue(null) as ThingDef;
            if (def?.comps == null || !def.comps.Any(c => c.compClass != null
                && typeof(CompPilotConsole).IsAssignableFrom(c.compClass)))
            {
                failure = $"VGE 的 {name} 不再具备驾驶台组件。";
                return false;
            }
            return true;
        }

        private static bool TryReadDefField(ModContentPack mod, Type type, string name, Type fieldType,
            out FieldInfo? field, out string failure)
        {
            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueField(type, name, fieldType,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly, out field, out failure))
                return false;
            Def? def = field!.GetValue(null) as Def;
            if (def == null || def.defName != name || !ReferenceEquals(def.modContentPack, mod))
            {
                failure = $"VGE DefOf.{name} 未绑定到目标内容包的预期 Def。";
                return false;
            }
            return true;
        }

        private ThirdPartyCompatibilityResult Changed(string reason) =>
            ThirdPartyCompatibilityResult.CreateTargetChanged(ModuleId, DisplayName, PackageId, reason);
    }
}
