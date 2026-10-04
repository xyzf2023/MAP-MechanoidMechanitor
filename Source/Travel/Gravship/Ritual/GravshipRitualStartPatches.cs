using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// CanStartRitualNow 调用作用域：仅管理 ThreadStatic 上下文，不跳过原版。
    /// </summary>
    [HarmonyPatch(typeof(RitualBehaviorWorker), nameof(RitualBehaviorWorker.CanStartRitualNow))]
    public static class GravshipRitualStart_CanStartRitualNow_Scope_Patch
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] GravshipRitualStartPatches：";

        private const int ErrorKeyPushFailed = 879348001;

        [ThreadStatic]
        private static Stack<GravshipCanStartScopeFrame>? scopeStack;

        public sealed class GravshipCanStartScopeFrame
        {
            public RitualBehaviorWorker? Worker;
            public Precept_Ritual? Ritual;
            public TargetInfo Target;
            public bool IsActiveGravshipLaunch;
        }

        [HarmonyPrefix]
        public static void Prefix(
            RitualBehaviorWorker __instance,
            TargetInfo target,
            Precept_Ritual ritual,
            out bool __state)
        {
            __state = false;
            try
            {
                GravshipCanStartScopeFrame frame = new GravshipCanStartScopeFrame
                {
                    Worker = __instance,
                    Ritual = ritual,
                    Target = target,
                    IsActiveGravshipLaunch = IsActiveGravshipLaunchContext(
                        __instance,
                        ritual,
                        target)
                };

                GetScopeStack().Push(frame);
                __state = true;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}压入 CanStartRitualNow 作用域失败：{ex}",
                    ErrorKeyPushFailed);
                __state = false;
            }
        }

        [HarmonyFinalizer]
        public static Exception? Finalizer(Exception? __exception, bool __state)
        {
            if (__state)
            {
                Stack<GravshipCanStartScopeFrame> stack = GetScopeStack();
                if (stack.Count > 0)
                {
                    stack.Pop();
                }
            }

            return __exception;
        }

        public static bool IsActiveForSelectedTarget(TargetInfo selectedTarget)
        {
            Stack<GravshipCanStartScopeFrame>? stack = scopeStack;
            if (stack == null || stack.Count == 0)
            {
                return false;
            }

            GravshipCanStartScopeFrame frame = stack.Peek();
            if (!frame.IsActiveGravshipLaunch)
            {
                return false;
            }

            Thing? selectedThing = selectedTarget.Thing;
            Thing? scopedThing = frame.Target.Thing;
            if (selectedThing == null
                || scopedThing == null
                || !ReferenceEquals(selectedThing, scopedThing))
            {
                return false;
            }

            Map? selectedMap = selectedTarget.Map;
            Map? scopedMap = frame.Target.Map;
            if (selectedMap != null
                && scopedMap != null
                && !ReferenceEquals(selectedMap, scopedMap))
            {
                return false;
            }

            return true;
        }

        public static bool TryGetActiveGravshipTarget(out TargetInfo target)
        {
            target = default;
            Stack<GravshipCanStartScopeFrame>? stack = scopeStack;
            if (stack == null || stack.Count == 0)
            {
                return false;
            }

            GravshipCanStartScopeFrame frame = stack.Peek();
            if (!frame.IsActiveGravshipLaunch)
            {
                return false;
            }

            target = frame.Target;
            return true;
        }

        private static bool IsActiveGravshipLaunchContext(
            RitualBehaviorWorker instance,
            Precept_Ritual ritual,
            TargetInfo target)
        {
            if (!ModsConfig.OdysseyActive)
            {
                return false;
            }

            if (instance is not RitualBehaviorWorker_GravshipLaunch)
            {
                return false;
            }

            if (!GravshipLaunchRitualUtility.IsSupportedLaunch(ritual))
            {
                return false;
            }

            if (!target.IsValid || target.Map == null || target.Thing == null)
            {
                return false;
            }

            return target.Thing.TryGetComp<CompPilotConsole>() != null;
        }

        private static Stack<GravshipCanStartScopeFrame> GetScopeStack()
        {
            return scopeStack ??= new Stack<GravshipCanStartScopeFrame>();
        }
    }

    /// <summary>
    /// 在原版候选列表 AddRange(动物) 之后追加机械驾驶员，不跳过/复制 CanStartRitualNow。
    /// </summary>
    [HarmonyPatch(typeof(RitualBehaviorWorker), nameof(RitualBehaviorWorker.CanStartRitualNow))]
    public static class GravshipRitualStart_CanStartRitualNow_CandidateList_Patch
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] GravshipRitualStartPatches：";

        private const int ErrorKeyResolveMembers = 879348101;
        private const int ErrorKeyPatternMismatch = 879348102;

        public static void AddCurrentGravshipPilotCandidates(List<Pawn>? candidates)
        {
            if (candidates == null)
            {
                return;
            }

            if (!GravshipRitualStart_CanStartRitualNow_Scope_Patch.TryGetActiveGravshipTarget(
                    out TargetInfo target))
            {
                return;
            }

            Map? map = target.Map;
            if (map?.mapPawns == null)
            {
                return;
            }

            IReadOnlyList<Pawn> spawned = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn pawn = spawned[i];
                if (!GravshipRitualPilotCandidateUtility.IsEligiblePilotCandidate(pawn))
                {
                    continue;
                }

                candidates.AddUnique(pawn);
            }
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            MethodInfo? freeColonistsGetter = AccessTools.PropertyGetter(
                typeof(MapPawns),
                nameof(MapPawns.FreeColonistsAndPrisonersSpawned));
            MethodInfo? animalsGetter = AccessTools.PropertyGetter(
                typeof(MapPawns),
                nameof(MapPawns.SpawnedColonyAnimals));
            MethodInfo? toListOpen = AccessTools.Method(
                typeof(Enumerable),
                nameof(Enumerable.ToList));
            MethodInfo? addRange = AccessTools.Method(
                typeof(List<Pawn>),
                nameof(List<Pawn>.AddRange),
                new[] { typeof(IEnumerable<Pawn>) });
            MethodInfo? addCandidates = AccessTools.Method(
                typeof(GravshipRitualStart_CanStartRitualNow_CandidateList_Patch),
                nameof(AddCurrentGravshipPilotCandidates));

            if (freeColonistsGetter == null
                || animalsGetter == null
                || toListOpen == null
                || addRange == null
                || addCandidates == null)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}无法解析 CanStartRitualNow 候选列表 Transpiler 成员，补丁未应用。",
                    ErrorKeyResolveMembers);
                return codes;
            }

            List<(int AddRangeIndex, CodeInstruction LoadList)> matches =
                new List<(int, CodeInstruction)>();

            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(freeColonistsGetter))
                {
                    continue;
                }

                int toListIndex = FindToListPawnCall(codes, i + 1, Math.Min(i + 8, codes.Count), toListOpen);
                if (toListIndex < 0 || toListIndex + 1 >= codes.Count)
                {
                    continue;
                }

                CodeInstruction storeList = codes[toListIndex + 1];
                if (!TryGetLocalIndex(storeList, isStore: true, out int listLocal))
                {
                    continue;
                }

                int searchEnd = Math.Min(toListIndex + 24, codes.Count);
                for (int j = toListIndex + 2; j < searchEnd; j++)
                {
                    if (!codes[j].Calls(addRange))
                    {
                        continue;
                    }

                    if (!TryFindMatchingLdlocBefore(
                            codes,
                            toListIndex + 2,
                            j,
                            listLocal,
                            out CodeInstruction loadListTemplate))
                    {
                        continue;
                    }

                    if (!ContainsCallBetween(codes, toListIndex + 2, j, animalsGetter))
                    {
                        continue;
                    }

                    matches.Add((j, loadListTemplate));
                    break;
                }
            }

            if (matches.Count != 1)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}CanStartRitualNow 候选列表初始化锚点预期仅 1 组，实际 {matches.Count} 组，补丁未应用。",
                    ErrorKeyPatternMismatch);
                return codes;
            }

            int insertAt = matches[0].AddRangeIndex + 1;
            CodeInstruction cleanLoad = new CodeInstruction(
                matches[0].LoadList.opcode,
                matches[0].LoadList.operand);
            codes.Insert(insertAt, cleanLoad);
            codes.Insert(insertAt + 1, new CodeInstruction(OpCodes.Call, addCandidates));
            return codes;
        }

        private static int FindToListPawnCall(
            List<CodeInstruction> codes,
            int startInclusive,
            int endExclusive,
            MethodInfo toListOpen)
        {
            for (int i = startInclusive; i < endExclusive; i++)
            {
                if (codes[i].operand is not MethodInfo method)
                {
                    continue;
                }

                if (!method.IsGenericMethod)
                {
                    continue;
                }

                MethodInfo definition;
                try
                {
                    definition = method.GetGenericMethodDefinition();
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                if (definition != toListOpen)
                {
                    continue;
                }

                Type[] args = method.GetGenericArguments();
                if (args.Length == 1 && args[0] == typeof(Pawn))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool TryFindMatchingLdlocBefore(
            List<CodeInstruction> codes,
            int startInclusive,
            int addRangeIndex,
            int listLocal,
            out CodeInstruction loadListTemplate)
        {
            loadListTemplate = null!;
            for (int i = addRangeIndex - 1; i >= startInclusive; i--)
            {
                if (!TryGetLocalIndex(codes[i], isStore: false, out int local)
                    || local != listLocal)
                {
                    continue;
                }

                loadListTemplate = codes[i];
                return true;
            }

            return false;
        }

        private static bool ContainsCallBetween(
            List<CodeInstruction> codes,
            int startInclusive,
            int endExclusive,
            MethodInfo target)
        {
            for (int i = startInclusive; i < endExclusive; i++)
            {
                if (codes[i].Calls(target))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetLocalIndex(
            CodeInstruction instruction,
            bool isStore,
            out int localIndex)
        {
            localIndex = -1;
            if (instruction == null)
            {
                return false;
            }

            if (isStore)
            {
                if (!instruction.IsStloc())
                {
                    return false;
                }
            }
            else if (!instruction.IsLdloc())
            {
                return false;
            }

            try
            {
                localIndex = instruction.LocalIndex();
                return localIndex >= 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
