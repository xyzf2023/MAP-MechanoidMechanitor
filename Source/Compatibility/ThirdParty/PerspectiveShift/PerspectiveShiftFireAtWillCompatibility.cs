using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.PerspectiveShift
{
    internal sealed class PerspectiveShiftFireAtWillCompatibility : PerspectiveShiftCompatibilityModule
    {
        public override string ModuleId => "PerspectiveShift.FireAtWillState";
        public override string DisplayName => "Perspective Shift：自由开火状态保护";
        protected override string AppliedDetail => "机械师接管期间仍禁止自动射击，但不改写原有自由开火存档值。";
        protected override void SetEnabled(bool enabled) => PerspectiveShiftFireAtWillPatch.Enabled = enabled;

        protected override void Resolve(ModContentPack mod, List<Binding> bindings)
        {
            MethodInfo target = Method(ResolveType(mod, "Pawn_DraftController_FireAtWill_Patch"), "Prefix", true,
                typeof(bool), typeof(Pawn_DraftController), typeof(bool).MakeByRefType());
            RequirePatch(Method(typeof(Pawn_DraftController), "get_FireAtWill", false, typeof(bool)),
                target, HarmonyPatchType.Prefix);
            FieldInfo savedValue = Field(typeof(Pawn_DraftController), "fireAtWillInt", typeof(bool));
            int writes = 0;
            foreach (CodeInstruction code in PatchProcessor.GetOriginalInstructions(target))
                if (code.opcode == OpCodes.Stfld && Equals(code.operand, savedValue)) writes++;
            if (writes != 1)
                throw new System.InvalidOperationException("Perspective Shift：自由开火字段写入预期唯一，目标已变化。");
            bindings.Add(new Binding(target, typeof(PerspectiveShiftFireAtWillPatch),
                nameof(PerspectiveShiftFireAtWillPatch.Prefix), HarmonyPatchType.Prefix));
        }
    }

    internal static class PerspectiveShiftFireAtWillPatch
    {
        internal static bool Enabled;

        // __1 是上游 Prefix 的 ref bool 参数；__result 是该 Prefix 自身的 bool 返回值。
        public static bool Prefix(Pawn_DraftController __0, ref bool __1, ref bool __result)
        {
            if (!Enabled || !PerspectiveShiftRuntime.CanQuery
                || !PerspectiveShiftRuntime.IsControlled(__0?.pawn)
                || !PerspectiveShiftRuntime.IsPlayerMechanitor(__0!.pawn))
                return true;
            __1 = false;
            __result = false;
            return false;
        }
    }
}
