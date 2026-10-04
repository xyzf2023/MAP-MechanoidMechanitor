using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.VanillaGravshipExpanded
{
    // 手动安装到 VGE 自己的 Postfix，不替换原版起飞方法或 VGE 的目的地数据。
    internal static class VanillaGravshipLaunchStatePatch
    {
        private static FieldInfo? dictionaryField;
        private static FieldInfo? destinationField;
        private static readonly MethodInfo FirstOrDefault = typeof(Enumerable).GetMethods()
            .Single(m => m.Name == nameof(Enumerable.FirstOrDefault)
                && m.IsGenericMethodDefinition && m.GetParameters().Length == 2
                && m.GetParameters()[1].ParameterType.IsGenericType
                && m.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(Func<,>))
            .MakeGenericMethod(typeof(LordJob_Ritual));
        private static readonly MethodInfo DictionarySetter = AccessTools.PropertySetter(
            typeof(Dictionary<LordJob_Ritual, PlanetTile>), "Item");

        public static void Configure(FieldInfo dictionary, FieldInfo destination)
        {
            dictionaryField = dictionary;
            destinationField = destination;
        }

        public static bool TryFindAnchors(List<CodeInstruction> codes,
            out int lookup, out int setter, out string failure)
        {
            lookup = -1;
            setter = -1;
            failure = string.Empty;
            int lookupCount = 0, setterCount = 0, dictionaryCount = 0, destinationCount = 0;
            foreach (CodeInstruction code in codes)
            {
                // 插入额外参数要求调用点没有跳转入栈中间或异常处理边界。
                if (code.blocks.Count != 0)
                {
                    failure = "VGE 起飞 Postfix 出现异常处理块，跳过兼容。";
                    return false;
                }
            }
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].Calls(FirstOrDefault)) { lookup = i; lookupCount++; }
                if (codes[i].Calls(DictionarySetter)) { setter = i; setterCount++; }
                if (codes[i].opcode == OpCodes.Ldsfld && Equals(codes[i].operand, dictionaryField))
                    dictionaryCount++;
                if (codes[i].opcode == OpCodes.Ldsfld && Equals(codes[i].operand, destinationField))
                    destinationCount++;
            }
            if (lookupCount != 1 || setterCount != 1 || dictionaryCount != 1 || destinationCount != 1
                || lookup >= setter
                || setter < 1 || codes[setter - 1].opcode != OpCodes.Ldsfld
                || !Equals(codes[setter - 1].operand, destinationField))
            {
                failure = "VGE 起飞 Postfix 的仪式查找/目的地字典写入锚点不符合预期。";
                return false;
            }
            return true;
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            if (!TryFindAnchors(codes, out int lookup, out int setter, out string failure))
                throw new InvalidOperationException(failure);

            // 原字典和目的地值仍由 VGE 读取；只给查找结果与空键写入增加约束。
            codes[setter].opcode = OpCodes.Call;
            codes[setter].operand = AccessTools.DeclaredMethod(typeof(VanillaGravshipLaunchStatePatch),
                nameof(StoreDestinationIfStarted));
            codes[lookup].opcode = OpCodes.Call;
            codes[lookup].operand = AccessTools.DeclaredMethod(typeof(VanillaGravshipLaunchStatePatch),
                nameof(FindCurrentLaunch));
            CodeInstruction loadTarget = new CodeInstruction(OpCodes.Ldarg_0);
            // 编译器缓存委托的分支通常以 FirstOrDefault 调用为汇合点。
            loadTarget.labels.AddRange(codes[lookup].labels);
            codes[lookup].labels.Clear();
            codes.InsertRange(lookup, new[]
            {
                loadTarget,
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Ldarg_S, (byte)4)
            });
            return codes;
        }

        public static LordJob_Ritual? FindCurrentLaunch(IEnumerable<LordJob_Ritual> jobs,
            Func<LordJob_Ritual, bool> upstreamPredicate, TargetInfo target,
            Precept_Ritual ritual, RitualRoleAssignments assignments)
        {
            // 包括角色分配对象，避免启动复查失败后误写已有的同类仪式。
            return jobs.FirstOrDefault(job => ReferenceEquals(job.Ritual, ritual)
                && ReferenceEquals(job.assignments, assignments)
                && job.selectedTarget == target && upstreamPredicate(job));
        }

        public static void StoreDestinationIfStarted(Dictionary<LordJob_Ritual, PlanetTile> destinations,
            LordJob_Ritual? job, PlanetTile destination)
        {
            if (job != null)
                destinations[job] = destination;
        }
    }
}
