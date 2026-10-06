using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.PerspectiveShift
{
    internal sealed class PerspectiveShiftControlCompatibility : PerspectiveShiftCompatibilityModule
    {
        public override string ModuleId => "PerspectiveShift.MechanitorControl";
        public override string DisplayName => "Perspective Shift：机械师接管与角色名单";
        protected override string AppliedDetail => "已扩展正式玩家机械师接管与角色名单，保留模式限制及实际工作优先级。";
        protected override void SetEnabled(bool enabled) => PerspectiveShiftControlPatches.Enabled = enabled;

        protected override void Resolve(ModContentPack mod, List<Binding> bindings)
        {
            Type gizmoPatchType = ResolveType(mod, "Pawn_GetGizmos_Patch");
            MethodInfo menu = Method(gizmoPatchType, "Postfix", true, typeof(IEnumerable<Gizmo>),
                typeof(IEnumerable<Gizmo>), typeof(Pawn));
            RequirePatch(Method(typeof(Pawn), nameof(Pawn.GetGizmos), false, typeof(IEnumerable<Gizmo>)),
                menu, HarmonyPatchType.Postfix);
            MethodInfo? moveNext = AccessTools.EnumeratorMoveNext(menu);
            Type? iterator = moveNext?.DeclaringType;
            if (moveNext == null || moveNext.IsStatic || moveNext.ReturnType != typeof(bool)
                || moveNext.GetParameters().Length != 0 || iterator?.DeclaringType != gizmoPatchType
                || iterator.Assembly != gizmoPatchType.Assembly
                || !typeof(IEnumerator<Gizmo>).IsAssignableFrom(iterator))
                throw new InvalidOperationException("Perspective Shift：机械师菜单迭代器结构已变化。");

            Type characterPage = ResolveType(mod, "Page_ChooseStartingCharacter");
            if (!typeof(Page).IsAssignableFrom(characterPage))
                throw new InvalidOperationException("Perspective Shift：角色选择页不再继承 Page。");
            MethodInfo pawns = Method(characterPage, "get_StartingPawns", false, typeof(List<Pawn>));
            MethodInfo setAvatar = Method(PerspectiveShiftRuntime.StateType, "SetAvatar", true,
                typeof(void), typeof(Pawn), typeof(bool));
            MethodInfo workPriority = Method(ResolveType(mod, "Pawn_WorkSettings_GetPriority_Patch"),
                "Postfix", true, typeof(void), typeof(Pawn_WorkSettings), typeof(WorkTypeDef), typeof(int).MakeByRefType());
            RequirePatch(Method(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.GetPriority), false,
                typeof(int), typeof(WorkTypeDef)), workPriority, HarmonyPatchType.Postfix);

            bindings.Add(new Binding(moveNext, typeof(PerspectiveShiftControlPatches),
                nameof(PerspectiveShiftControlPatches.MenuTranspiler), HarmonyPatchType.Transpiler));
            bindings.Add(new Binding(pawns, typeof(PerspectiveShiftControlPatches),
                nameof(PerspectiveShiftControlPatches.StartingPawnsPostfix), HarmonyPatchType.Postfix));
            bindings.Add(new Binding(setAvatar, typeof(PerspectiveShiftControlPatches),
                nameof(PerspectiveShiftControlPatches.SetAvatarPrefix), HarmonyPatchType.Prefix));
            bindings.Add(new Binding(workPriority, typeof(PerspectiveShiftControlPatches),
                nameof(PerspectiveShiftControlPatches.WorkPriorityPrefix), HarmonyPatchType.Prefix));
        }
    }

    internal static class PerspectiveShiftControlPatches
    {
        internal static bool Enabled;

        private static bool IsColonistOrMechanitor(Pawn pawn) => pawn.IsColonist
            || (Enabled && PerspectiveShiftRuntime.CanQuery && PerspectiveShiftRuntime.IsPlayerMechanitor(pawn)
                && PerspectiveShiftRuntime.HasControlInfrastructure(pawn));

        public static IEnumerable<CodeInstruction> MenuTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo? getter = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.IsColonist));
            MethodInfo? helper = AccessTools.DeclaredMethod(typeof(PerspectiveShiftControlPatches),
                nameof(IsColonistOrMechanitor), new[] { typeof(Pawn) });
            if (getter == null || helper == null)
                throw new InvalidOperationException("Perspective Shift：菜单资格方法无法解析。");
            var codes = new List<CodeInstruction>(instructions);
            int[] matches = Enumerable.Range(0, codes.Count).Where(i => codes[i].Calls(getter)).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException("Perspective Shift：菜单 IsColonist 判断预期唯一，目标已变化。");
            codes[matches[0]].opcode = OpCodes.Call;
            codes[matches[0]].operand = helper;
            return codes;
        }

        public static void StartingPawnsPostfix(ref List<Pawn> __result)
        {
            if (!Enabled || !PerspectiveShiftRuntime.CanQuery || Current.ProgramState != ProgramState.Playing
                || __result == null)
                return;
            var result = new List<Pawn>(__result);
            var seen = new HashSet<Pawn>(result);
            foreach (Pawn pawn in GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors)
            {
                if (pawn.Spawned && pawn.Map != null && Find.Maps.Contains(pawn.Map)
                    && PerspectiveShiftRuntime.CanOperate(pawn) && seen.Add(pawn))
                    result.Add(pawn);
            }
            // 不写入上游页面缓存或 MapPawns 的共享列表。
            __result = result;
        }

        public static bool SetAvatarPrefix(Pawn __0)
        {
            if (!Enabled || !PerspectiveShiftRuntime.IsMechanitor(__0) || PerspectiveShiftRuntime.CanOperate(__0))
                return true;
            Messages.Message("MAP_PerspectiveShift_ControlUnavailable".Translate(), MessageTypeDefOf.RejectInput, false);
            return false;
        }

        public static bool WorkPriorityPrefix(Pawn_WorkSettings __0)
        {
            if (!Enabled || !PerspectiveShiftRuntime.CanQuery || __0 == null)
                return true;
            Pawn? pawn = PerspectiveShiftRuntime.CurrentPawn;
            // 原版 settings.pawn 是私有字段；这里只需核对当前主角公开的 tracker 引用。
            return pawn == null || !ReferenceEquals(pawn.workSettings, __0)
                || !PerspectiveShiftRuntime.IsControlled(pawn) || !PerspectiveShiftRuntime.IsPlayerMechanitor(pawn);
        }
    }
}
