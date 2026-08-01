using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class HumanApparelGearTabPatches
    {
        private const string LogPrefix = "[MAP-机械族机械师] HumanApparelGearTabPatches：";

        private const int ErrorKeyResolveFailed = 879345401;
        private const int ErrorKeyMatchCount = 879345402;

        public static bool GearTabCountsAsColonistPlayerControlled(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            if (pawn.IsColonistPlayerControlled)
            {
                return true;
            }

            if (pawn.Faction == Faction.OfPlayer
                && MechanoidMechanitorRoleUtility.AllowsHumanWeapons(pawn))
            {
                return true;
            }

            return HumanApparelUtility.TryGetApparelComp(pawn, out CompHumanApparelUser? comp)
                && comp!.AllowRemoveApparel;
        }

        [HarmonyPatch(typeof(ITab_Pawn_Gear), "CanControlColonist", MethodType.Getter)]
        public static class Patch_ITab_Pawn_Gear_CanControlColonist
        {
            [HarmonyTranspiler]
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

                MethodInfo? isColonistPlayerControlledGetter = AccessTools.PropertyGetter(
                    typeof(Pawn),
                    nameof(Pawn.IsColonistPlayerControlled));
                MethodInfo? helperMethod = AccessTools.Method(
                    typeof(HumanApparelGearTabPatches),
                    nameof(GearTabCountsAsColonistPlayerControlled));

                if (isColonistPlayerControlledGetter == null || helperMethod == null)
                {
                    Log.ErrorOnce(
                        $"{LogPrefix}无法解析 CanControlColonist 相关方法，补丁未应用。",
                        ErrorKeyResolveFailed);
                    return codes;
                }

                int matchCount = 0;
                int matchIndex = -1;

                for (int i = 0; i < codes.Count; i++)
                {
                    if (!codes[i].Calls(isColonistPlayerControlledGetter))
                    {
                        continue;
                    }

                    matchCount++;
                    matchIndex = i;
                }

                if (matchCount != 1)
                {
                    Log.ErrorOnce(
                        $"{LogPrefix}CanControlColonist 中 IsColonistPlayerControlled 调用预期仅 1 处，实际找到 {matchCount} 处，补丁未应用。",
                        ErrorKeyMatchCount);
                    return codes;
                }

                CodeInstruction instruction = codes[matchIndex];
                instruction.opcode = OpCodes.Call;
                instruction.operand = helperMethod;

                return codes;
            }
        }
    }
}
