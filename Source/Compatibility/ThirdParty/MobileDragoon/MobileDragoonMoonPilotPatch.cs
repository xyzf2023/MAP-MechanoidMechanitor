using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.MobileDragoon
{
    internal static class MobileDragoonMoonPilotPatch
    {
        private static Type? coreType;
        private static MethodInfo? coreGetter;

        internal static void Configure(Type resolvedCoreType, MethodInfo resolvedCoreGetter)
        {
            coreType = resolvedCoreType;
            coreGetter = resolvedCoreGetter;
        }

        internal static bool IsMoon(Pawn? pawn)
        {
            // 这是用户指定的单一机体兼容，不以机械族或机械师通用身份扩大授权。
            return pawn?.def?.defName == "MAP_Mech_Moon";
        }

        internal static bool IsDragoonCore(Thing? core)
        {
            return core != null
                && coreType != null
                && coreType.IsInstanceOfType(core)
                && string.Equals(core.def?.modContentPack?.PackageId,
                    MobileDragoonCompatibility.TargetPackageId, StringComparison.OrdinalIgnoreCase)
                && (core.def?.apparel?.tags?.Contains("DMS_DragoonArmour") ?? false);
        }

        private static bool TryGetCore(Thing? bay, out Thing? core)
        {
            core = null;
            if (bay == null || coreGetter?.DeclaringType == null
                || !coreGetter.DeclaringType.IsInstanceOfType(bay))
            {
                return false;
            }

            core = coreGetter.Invoke(bay, null) as Thing;
            return true;
        }

        private static bool IsDragoonContext(Thing? bay, Pawn pawn, bool allowWornCore)
        {
            if (!TryGetCore(bay, out Thing? core))
            {
                return false;
            }

            // 整备台有核心时，必须以待驾驶的核心为准；不能用已穿的龙骑兵放行其他外骨骼。
            if (core != null)
            {
                return IsDragoonCore(core);
            }

            // 上机之后 Core 已从整备台移到 Pawn。仅身份检查/分配候选允许这个下机上下文。
            return allowWornCore && pawn.apparel != null
                && pawn.apparel.WornApparel.Any(apparel => IsDragoonCore(apparel));
        }

        internal static IEnumerable<CodeInstruction> TranspilerAcceptablePawnKind(
            IEnumerable<CodeInstruction> instructions)
        {
            // 原栈保留 RaceProperties，再压入 pawn 和 bay；只替换 Humanlike getter。
            return ReplaceGetter(instructions,
                AccessTools.PropertyGetter(typeof(RaceProperties), nameof(RaceProperties.Humanlike)),
                nameof(HumanlikeOrMoonPilot), 1, true);
        }

        internal static IEnumerable<CodeInstruction> TranspilerReservations(
            IEnumerable<CodeInstruction> instructions)
        {
            // 当前上游恰有两处 BodySize：ExosuitExt.BodySizeCap 与无扩展时的 1.25 上限。
            // 只替换本方法内这两个读数，保留比较、拒绝提示、全部其他驾驶条件以及 Reserve 调用。
            return ReplaceGetter(instructions,
                AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.BodySize)),
                nameof(BodySizeForPilotCheck), 2, false);
        }

        private static IEnumerable<CodeInstruction> ReplaceGetter(
            IEnumerable<CodeInstruction> instructions, MethodInfo getter,
            string helperName, int expectedCount, bool loadPawnArgument)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            MethodInfo helper = AccessTools.Method(typeof(MobileDragoonMoonPilotPatch), helperName);
            if (getter == null || helper == null)
            {
                throw new InvalidOperationException("月亮驾驶龙骑兵：无法解析原版 属性读取方法 或兼容辅助方法。");
            }

            List<int> matches = new List<int>();
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].Calls(getter))
                {
                    if (codes[i].blocks.Count != 0)
                    {
                        throw new InvalidOperationException("月亮驾驶龙骑兵：目标 属性读取方法 带有异常块边界，拒绝改写。");
                    }
                    matches.Add(i);
                }
            }

            if (matches.Count != expectedCount)
            {
                throw new InvalidOperationException(
                    $"月亮驾驶龙骑兵：{getter.Name} 预期 {expectedCount} 处，实际 {matches.Count} 处，目标结构已变化。");
            }

            for (int m = matches.Count - 1; m >= 0; m--)
            {
                int index = matches[m];
                CodeInstruction call = codes[index];
                CodeInstruction firstLoad = new CodeInstruction(
                    loadPawnArgument ? OpCodes.Ldarg_1 : OpCodes.Ldarg_0);
                firstLoad.labels.AddRange(call.labels);
                call.labels.Clear();
                codes.Insert(index, firstLoad);
                if (loadPawnArgument)
                {
                    codes.Insert(index + 1, new CodeInstruction(OpCodes.Ldarg_0));
                }
                call.opcode = OpCodes.Call;
                call.operand = helper;
            }

            return codes;
        }

        private static bool HumanlikeOrMoonPilot(RaceProperties race, Pawn pawn, Thing bay)
        {
            return race.Humanlike || (IsMoon(pawn) && IsDragoonContext(bay, pawn, true));
        }

        private static float BodySizeForPilotCheck(Pawn pawn, JobDriver driver)
        {
            // 仅对此次驾驶门槛提供不超上限的读数，不修改 Pawn 体型或共享 Def/扩展对象。
            return IsMoon(pawn)
                && driver?.job != null
                && IsDragoonContext(driver.job.GetTarget(TargetIndex.A).Thing, pawn, false)
                    ? float.NegativeInfinity
                    : pawn.BodySize;
        }

        internal static void PostfixAssigningCandidates(
            CompAssignableToPawn __instance, ref IEnumerable<Pawn> __result)
        {
            ThingWithComps? bay = __instance?.parent;
            if (bay == null || !bay.Spawned || bay.Map == null
                || !TryGetCore(bay, out Thing? core)
                || (core != null && !IsDragoonCore(core)))
            {
                return;
            }

            List<Pawn> moons = bay.Map.mapPawns.AllPawnsSpawned
                .Where(pawn => IsMoon(pawn) && pawn.Faction == Faction.OfPlayer
                    && !pawn.Dead && !pawn.Destroyed
                    && IsDragoonContext(bay, pawn, true))
                .ToList();
            if (moons.Count == 0)
            {
                return;
            }

            // 保留原候选，避免其他补丁已添加月亮造成重复，并沿用上游 CanAssignTo 排序。
            __result = __result.Concat(moons).Distinct()
                .OrderByDescending(pawn => __instance!.CanAssignTo(pawn).Accepted);
        }
    }
}
