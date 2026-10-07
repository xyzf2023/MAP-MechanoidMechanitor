using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.RimHUD
{
    internal static class RimHUDPawnDisplayPatches
    {
        internal static bool CompInfoEnabled;
        internal static bool InspirationEnabled;
        internal static bool LayoutEnabled;
        internal static bool EnergyEnabled;
        internal static Func<Pawn?>? SelectedPawn;
        internal static Func<string?>? FormatInspiration;
        internal static MethodInfo? IsTargetMethod;
        internal static MethodInfo? RowVisibleMethod;
        internal static MethodInfo? PanelVisibleMethod;
        internal static MethodInfo? BaseVisibleMethod;
        internal static Func<object, bool>? OriginalIsTarget;
        internal static Func<object, bool>? OriginalRowVisible;
        internal static Func<object, bool>? OriginalPanelVisible;
        internal static Func<object, string>? LayerId;
        internal static Func<object, int>? LayerTargets;
        internal static Type? WidgetType;
        internal static ChildrenAccessor[] Children = Array.Empty<ChildrenAccessor>();

        internal sealed class ChildrenAccessor
        {
            internal readonly Type Type;
            internal readonly Func<object, Array> Get;
            internal ChildrenAccessor(Type type, Func<object, Array> get) { Type = type; Get = get; }
        }

        private static readonly HashSet<string> SkillWidgets = new HashSet<string>(StringComparer.Ordinal)
        {
            "Skill", "SkillShooting", "SkillMelee", "SkillConstruction", "SkillMining", "SkillCooking",
            "SkillPlants", "SkillAnimals", "SkillCrafting", "SkillArtistic", "SkillMedicine", "SkillSocial",
            "SkillIntellectual"
        };

        internal static IEnumerable<CodeInstruction> CompInfoTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = instructions.ToList();
            MethodInfo shutdown = AccessTools.PropertyGetter(typeof(Need_MechEnergy), nameof(Need_MechEnergy.IsLowEnergySelfShutdown));
            MethodInfo hasValue = AccessTools.PropertyGetter(typeof(bool?), nameof(Nullable<bool>.HasValue));
            int shutdownIndex = codes.FindIndex(c => c.Calls(shutdown));
            int hasValueIndex = codes.FindIndex(c => c.Calls(hasValue));
            ConstructorInfo? nullableConstructor = typeof(bool?).GetConstructor(new[] { typeof(bool) });
            if (codes.Count(c => c.Calls(shutdown)) != 1 || codes.Count(c => c.Calls(hasValue)) != 1
                || shutdownIndex < 0 || hasValueIndex <= shutdownIndex || nullableConstructor == null
                || !codes.Skip(shutdownIndex + 1).Take(hasValueIndex - shutdownIndex - 1)
                    .Any(c => c.opcode == OpCodes.Newobj && Equals(c.operand, nullableConstructor)))
                throw new InvalidOperationException("RimHUD CompInfo 低电关机的可空布尔判断已变化，跳过修正。");

            // 原指令接收 Nullable<bool> 的地址；只替换谓词，保留标签、异常块与其他信息逻辑。
            codes[hasValueIndex].opcode = OpCodes.Call;
            codes[hasValueIndex].operand = AccessTools.DeclaredMethod(typeof(RimHUDPawnDisplayPatches),
                nameof(IsLowEnergyShutdown));
            return codes;
        }

        private static bool IsLowEnergyShutdown(ref bool? shutdown) =>
            CompInfoEnabled ? shutdown == true : shutdown.HasValue;

        internal static void InspirationPostfix(ref string? __result, bool __runOriginal)
        {
            if (!InspirationEnabled || !__runOriginal || __result != null || FormatInspiration == null) return;
            try
            {
                Pawn? pawn = SelectedPawn?.Invoke();
                if (!IsPlayerMech(pawn) || pawn!.needs?.mood != null
                    || !MechanoidMechanitorCapabilityUtility.HasCapability(pawn, MechanoidMechanitorCapability.Inspiration)
                    || !pawn.Inspired || pawn.Inspiration == null || pawn.InspirationDef == null) return;
                // 上游非空结果仍优先，包括眩晕和精神状态；无心情时补上原有颜色、名称与时长格式。
                __result = FormatInspiration();
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[MAP-机械族机械师] RimHUD 灵感显示兼容异常：" + ex, 879347231);
            }
        }

        internal static void EnergyPostfix(ref string? __result, bool __runOriginal)
        {
            if (!EnergyEnabled || !__runOriginal) return;
            try
            {
                Pawn? pawn = SelectedPawn?.Invoke();
                if (pawn == null || pawn.Destroyed || pawn.Discarded || pawn.Dead
                    || Scribe.mode != LoadSaveMode.Inactive
                    || MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferPositiveRestore
                    || !MechServiceEnergyContext.TryGetServiceEnergyRate(pawn, out string rate)) return;
                string line = "MechEnergy".Translate() + ": " + rate;
                // 保留上游消耗说明，再补充整备台当前速率；不在显示路径写入能量。
                if (string.IsNullOrEmpty(__result)) __result = line;
                else if (!__result!.Split('\n').Contains(line)) __result += "\n" + line;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[MAP-机械族机械师] RimHUD 整备台能量提示兼容异常：" + ex, 879347232);
            }
        }

        internal static IEnumerable<CodeInstruction> LayoutTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = instructions.ToList();
            if (IsTargetMethod == null || codes.Count(c => c.Calls(IsTargetMethod)) != 1)
                throw new InvalidOperationException("RimHUD 布局的 IsTarget 调用点已变化，跳过技能与作息放行。");
            CodeInstruction call = codes.Single(c => c.Calls(IsTargetMethod));
            call.opcode = OpCodes.Call;
            call.operand = AccessTools.DeclaredMethod(typeof(RimHUDPawnDisplayPatches), nameof(IsTarget));
            return codes;
        }

        internal static IEnumerable<CodeInstruction> RowVisibilityTranspiler(IEnumerable<CodeInstruction> instructions) =>
            RewriteVisibility(instructions, RowVisibleMethod, nameof(IsRowVisible));

        internal static IEnumerable<CodeInstruction> PanelVisibilityTranspiler(IEnumerable<CodeInstruction> instructions) =>
            RewriteVisibility(instructions, PanelVisibleMethod, nameof(IsPanelVisible));

        private static IEnumerable<CodeInstruction> RewriteVisibility(IEnumerable<CodeInstruction> instructions,
            MethodInfo? visible, string helper)
        {
            List<CodeInstruction> codes = instructions.ToList();
            if (visible == null || BaseVisibleMethod == null)
                throw new InvalidOperationException("RimHUD 容器可见性方法未就绪。");
            // 编译器可能把虚调用编码为基类槽位，也可能引用闭合泛型上的覆盖方法。
            List<CodeInstruction> calls = codes.Where(c => c.Calls(visible) || c.Calls(BaseVisibleMethod)).ToList();
            if (calls.Count != 1)
                throw new InvalidOperationException("RimHUD 行或面板的可见性调用点已变化。");
            calls[0].opcode = OpCodes.Call;
            calls[0].operand = AccessTools.DeclaredMethod(typeof(RimHUDPawnDisplayPatches), helper);
            return codes;
        }

        private static bool IsRowVisible(object layer) => IsContainerVisible(layer, OriginalRowVisible!);
        private static bool IsPanelVisible(object layer) => IsContainerVisible(layer, OriginalPanelVisible!);

        private static bool IsContainerVisible(object layer, Func<object, bool> upstream)
        {
            bool original = upstream(layer);
            if (original || !LayoutEnabled) return original;
            // 原方法的空容器限制仍然保留；只扩展技能或作息所在容器的目标资格。
            try
            {
                foreach (ChildrenAccessor accessor in Children)
                    if (accessor.Type.IsInstanceOfType(layer))
                        return accessor.Get(layer).Length > 0 && IsTarget(layer);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[MAP-机械族机械师] RimHUD 容器可见性兼容异常：" + ex, 879347234);
            }
            return original;
        }

        private static bool IsTarget(object layer)
        {
            // 即使安装失败留下补丁，禁用时仍调用未修改的上游判断。
            bool original = OriginalIsTarget!(layer);
            if (original || !LayoutEnabled) return original;
            try
            {
                Pawn? pawn = SelectedPawn?.Invoke();
                if (!IsPlayerMech(pawn)) return original;
                bool skills = pawn!.skills != null && MechanoidMechanitorCapabilityUtility.HasCapability(
                    pawn, MechanoidMechanitorCapability.IndividualSkills);
                bool timetable = pawn.timetable != null && MechanoidMechanitorCapabilityUtility.HasCapability(
                    pawn, MechanoidMechanitorCapability.ColonistLikeTimetable);
                // 组件来源的 UI 显示开关与后台作息能力分离；正式机械师不受该组件开关限制。
                if (timetable && pawn.GetComp<CompColonistLikeTimetableUser>()?.ShowInSchedule == false
                    && !GameComponent_MechanoidMechanitorRegistry.TryGetMechanitorRecord(pawn, out _))
                    timetable = false;
                return (skills || timetable) && ContainsEligibleWidget(layer, skills, timetable, 0);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[MAP-机械族机械师] RimHUD 技能与作息布局兼容异常：" + ex, 879347233);
                return original;
            }
        }

        private static bool ContainsEligibleWidget(object layer, bool skills, bool timetable, int depth)
        {
            // 尊重显式排除玩家的配置；仅识别技能和作息，不扩大其他模型的目标条件。
            if (depth > 64 || (LayerTargets!(layer) & 3) == 0) return false;
            if (WidgetType!.IsInstanceOfType(layer))
            {
                string id = LayerId!(layer);
                return (skills && SkillWidgets.Contains(id)) || (timetable && id == "Timetable");
            }
            foreach (ChildrenAccessor accessor in Children)
            {
                if (!accessor.Type.IsInstanceOfType(layer)) continue;
                foreach (object? child in accessor.Get(layer))
                    if (child != null && ContainsEligibleWidget(child, skills, timetable, depth + 1)) return true;
                break;
            }
            return false;
        }

        private static bool IsPlayerMech(Pawn? pawn) =>
            Scribe.mode == LoadSaveMode.Inactive && !MechanoidMechanitorPostLoadSafetyCoordinator.ShouldDeferPositiveRestore
            && pawn != null && !pawn.Destroyed && !pawn.Discarded && !pawn.Dead
            && pawn.Faction != null && pawn.Faction.IsPlayerSafe() && pawn.RaceProps?.IsMechanoid == true;
    }
}
