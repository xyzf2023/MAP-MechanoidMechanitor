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
        private const string PilotApparelTag = "DMS_Pilot";
        private static Type? coreType;
        private static MethodInfo? coreGetter;
        private static FieldInfo? requiredApparelTag;

        internal static bool Enabled;

        internal static void Configure(Type resolvedCoreType, MethodInfo resolvedCoreGetter,
            FieldInfo resolvedRequiredApparelTag)
        {
            Enabled = false;
            coreType = resolvedCoreType;
            coreGetter = resolvedCoreGetter;
            requiredApparelTag = resolvedRequiredApparelTag;
        }

        internal static bool ValidateTargets(MethodInfo acceptablePawnKind,
            MethodInfo reservations, out string failure)
        {
            try
            {
                ValidateConfiguration();
                // 预检查与实际安装共用转译路径，避免二者对上游结构的要求发生偏差。
                TranspilerAcceptablePawnKind(
                    PatchProcessor.GetOriginalInstructions(acceptablePawnKind)).ToList();
                TranspilerReservations(
                    PatchProcessor.GetOriginalInstructions(reservations)).ToList();
                failure = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                failure = "月亮驾驶龙骑兵：关键 IL 预检查失败："
                    + ex.GetType().Name + "：" + ex.Message;
                return false;
            }
        }

        private static void ValidateConfiguration()
        {
            if (coreType == null || !typeof(Apparel).IsAssignableFrom(coreType)
                || coreGetter == null || coreGetter.IsStatic
                || coreGetter.ReturnType != coreType || coreGetter.GetParameters().Length != 0
                || coreGetter.DeclaringType == null
                || !typeof(Thing).IsAssignableFrom(coreGetter.DeclaringType)
                || requiredApparelTag == null || requiredApparelTag.IsStatic
                || requiredApparelTag.FieldType != typeof(string)
                || requiredApparelTag.Name != "RequiredApparelTag"
                || requiredApparelTag.DeclaringType?.FullName != "Exosuit.ExosuitExt")
            {
                throw new InvalidOperationException("核心、整备台 Core 读取或驾驶服标签字段的配置不符合预期。");
            }
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

        internal static bool TryGetCore(Thing? bay, out Thing? core)
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

        internal static bool IsDragoonContext(Thing? bay, Pawn? pawn, bool allowWornCore)
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
            return allowWornCore && pawn?.apparel != null
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
            ValidateConfiguration();
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            FindRequiredApparelTagNullCheck(codes);

            // 当前上游恰有两处 BodySize：ExosuitExt.BodySizeCap 与无扩展时的 1.25 上限。
            // 只替换本方法内这两个读数，保留比较、拒绝提示、全部其他驾驶条件以及 Reserve 调用。
            codes = ReplaceGetter(codes,
                AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.BodySize)),
                nameof(BodySizeForPilotCheck), 2, false).ToList();

            int tagIndex = FindRequiredApparelTagNullCheck(codes);
            MethodInfo? helper = AccessTools.Method(typeof(MobileDragoonMoonPilotPatch),
                nameof(RequiredApparelTagForPilotCheck));
            if (helper == null)
            {
                throw new InvalidOperationException("月亮驾驶龙骑兵：无法解析驾驶服标签兼容辅助方法。");
            }

            // 保留真实字段读取，只在此次空值判断前转换局部结果，不修改共享扩展或服装标签。
            codes.Insert(tagIndex + 1, new CodeInstruction(OpCodes.Ldarg_0));
            codes.Insert(tagIndex + 2, new CodeInstruction(OpCodes.Call, helper));
            return codes;
        }

        private static int FindRequiredApparelTagNullCheck(List<CodeInstruction> codes)
        {
            int fieldIndex = -1;
            int count = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldfld
                    && Equals(codes[i].operand, requiredApparelTag))
                {
                    fieldIndex = i;
                    count++;
                }
            }

            if (count != 1)
            {
                throw new InvalidOperationException(
                    $"月亮驾驶龙骑兵：驾驶服标签空值检查预期 1 处字段读取，实际 {count} 处，目标结构已变化。");
            }

            if (codes[fieldIndex].blocks.Count != 0)
            {
                throw new InvalidOperationException("月亮驾驶龙骑兵：驾驶服标签读取带有异常块边界，拒绝改写。");
            }

            int branchIndex = fieldIndex + 1;
            while (branchIndex < codes.Count && codes[branchIndex].opcode == OpCodes.Nop)
            {
                if (codes[branchIndex].labels.Count != 0 || codes[branchIndex].blocks.Count != 0)
                {
                    throw new InvalidOperationException("月亮驾驶龙骑兵：驾驶服标签空值检查间存在跳转或异常块边界，拒绝改写。");
                }
                branchIndex++;
            }

            if (branchIndex >= codes.Count
                || (codes[branchIndex].opcode != OpCodes.Brfalse
                    && codes[branchIndex].opcode != OpCodes.Brfalse_S)
                || codes[branchIndex].labels.Count != 0 || codes[branchIndex].blocks.Count != 0
                || !(codes[branchIndex].operand is Label target))
            {
                throw new InvalidOperationException("月亮驾驶龙骑兵：驾驶服标签不再直接用于预期的空值分支，目标结构已变化。");
            }

            // 空标签必须向前跳过驾驶服拒绝段，不能误改其他用途的字段读取。
            int targetIndex = codes.FindIndex(code => code.labels.Contains(target));
            int rejectIndex = -1;
            for (int i = branchIndex + 1; i < targetIndex; i++)
            {
                if (codes[i].opcode == OpCodes.Ldstr
                    && Equals(codes[i].operand, "WG_RequirePilotSuit"))
                {
                    if (rejectIndex >= 0)
                    {
                        throw new InvalidOperationException("月亮驾驶龙骑兵：驾驶服空值分支内出现重复拒绝提示，目标结构已变化。");
                    }
                    rejectIndex = i;
                }
            }
            if (targetIndex <= branchIndex || rejectIndex < 0)
            {
                throw new InvalidOperationException("月亮驾驶龙骑兵：驾驶服空值分支未跳过预期的驾驶服拒绝段，目标结构已变化。");
            }
            return fieldIndex;
        }

        private static IEnumerable<CodeInstruction> ReplaceGetter(
            IEnumerable<CodeInstruction> instructions, MethodInfo? getter,
            string helperName, int expectedCount, bool loadPawnArgument)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            MethodInfo? helper = AccessTools.Method(typeof(MobileDragoonMoonPilotPatch), helperName);
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
            return race.Humanlike || (Enabled
                && IsMoon(pawn)
                && IsDragoonContext(bay, pawn, true));
        }

        private static float BodySizeForPilotCheck(Pawn pawn, JobDriver driver)
        {
            // 仅对此次驾驶门槛提供不超上限的读数，不修改 Pawn 体型或共享 Def/扩展对象。
            return Enabled && IsMoon(pawn)
                && driver?.job != null
                && IsDragoonContext(driver.job.GetTarget(TargetIndex.A).Thing, pawn, false)
                    ? float.NegativeInfinity
                    : pawn.BodySize;
        }

        private static string? RequiredApparelTagForPilotCheck(string? tag, JobDriver driver)
        {
            return Enabled && string.Equals(tag, PilotApparelTag, StringComparison.Ordinal)
                && IsMoon(driver?.pawn) && driver?.job != null
                && IsDragoonContext(driver.job.GetTarget(TargetIndex.A).Thing, driver.pawn, false)
                    ? null
                    : tag;
        }

        internal static void PostfixAssigningCandidates(
            CompAssignableToPawn __instance, ref IEnumerable<Pawn> __result)
        {
            ThingWithComps? bay = __instance?.parent;
            if (!Enabled || bay == null || !bay.Spawned || bay.Map == null
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
            __result = (__result ?? Enumerable.Empty<Pawn>()).Concat(moons).Distinct()
                .OrderByDescending(pawn => __instance!.CanAssignTo(pawn).Accepted);
        }
    }
}
