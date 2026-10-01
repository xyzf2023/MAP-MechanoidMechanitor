using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.NewRatkin
{
    internal static class NewRatkinWanderingTraderIncidentPatch
    {
        // 只缓存已校验的 Def/类型，不保存 Game、Map、Pawn 或第三方游戏组件实例。
        private static IncidentDef? targetIncident;
        private static Type? targetWorkerType;

        internal static void Configure(IncidentDef incident, Type workerType)
        {
            targetIncident = incident;
            targetWorkerType = workerType;
        }

        internal static void Reset()
        {
            targetIncident = null;
            targetWorkerType = null;
        }

        internal static int GetColonistPresenceCount(MapPawns mapPawns, IncidentWorker worker)
        {
            int originalCount = mapPawns.FreeColonistsSpawnedCount;
            if (originalCount > 0
                || targetIncident == null
                || targetWorkerType == null
                || worker == null
                || worker.def != targetIncident
                || worker.GetType() != targetWorkerType)
                return originalCount;

            IReadOnlyList<Pawn> mechanitors = GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors;
            for (int i = 0; i < mechanitors.Count; i++)
            {
                Pawn pawn = mechanitors[i];
                if (NewRatkinCompatibilityUtility.IsPlayerMechanitor(pawn)
                    && pawn.Spawned
                    && pawn.Map?.mapPawns == mapPawns)
                {
                    // 这里只用于判断是否有人：倒地/休眠不取消殖民地成员资格，不向共享名单写入。
                    return 1;
                }
            }
            return originalCount;
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo? getter = AccessTools.PropertyGetter(typeof(MapPawns), nameof(MapPawns.FreeColonistsSpawnedCount));
            MethodInfo? helper = AccessTools.DeclaredMethod(typeof(NewRatkinWanderingTraderIncidentPatch),
                nameof(GetColonistPresenceCount), new[] { typeof(MapPawns), typeof(IncidentWorker) });
            FieldInfo? requiresColonists = AccessTools.Field(typeof(IncidentDef), nameof(IncidentDef.requireColonistsPresent));
            if (getter == null || helper == null || requiresColonists == null)
                throw new InvalidOperationException("鼠族游商事件：无法解析殖民者存在门槛。");

            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            int match = -1;
            int requirementCount = 0;
            int requirementIndex = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].LoadsField(requiresColonists))
                {
                    requirementCount++;
                    requirementIndex = i;
                }
                if (!codes[i].Calls(getter))
                    continue;
                if (match >= 0)
                    throw new InvalidOperationException("鼠族游商事件：殖民者数量判断不唯一。");
                match = i;
            }
            if (match < 0 || requirementCount != 1 || requirementIndex >= match
                || codes[match].blocks.Count != 0)
                throw new InvalidOperationException("鼠族游商事件：TryExecute 的殖民者存在门槛结构发生变化。");

            // 数量读取必须紧接零人口分支；上游若改为数量阈值等其他用途则拒绝套用。
            int branchIndex = match + 1;
            while (branchIndex < codes.Count && codes[branchIndex].opcode == OpCodes.Nop)
                branchIndex++;
            if (branchIndex >= codes.Count
                || (codes[branchIndex].opcode != OpCodes.Brtrue && codes[branchIndex].opcode != OpCodes.Brtrue_S))
                throw new InvalidOperationException("鼠族游商事件：殖民者数量读取不再用于零人口判断。");

            CodeInstruction call = codes[match];
            CodeInstruction loadWorker = new CodeInstruction(OpCodes.Ldarg_0);
            // MapPawns 已在栈上，再加载当前 IncidentWorker；跳转必须先经过新增的参数加载。
            loadWorker.labels.AddRange(call.labels);
            call.labels.Clear();
            codes.Insert(match, loadWorker);
            call.opcode = OpCodes.Call;
            call.operand = helper;
            return codes;
        }
    }
}
