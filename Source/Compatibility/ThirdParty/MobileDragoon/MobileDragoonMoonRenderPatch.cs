using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.MobileDragoon
{
    /// <summary>
    /// 驾驶资格与渲染资格分离：只为实际穿着龙骑兵核心的非 Humanlike 月亮补全服装渲染。
    /// 复用原版服装生成器及 Exosuit 的节点/偏移逻辑，不改 RaceProps 或共享节点属性。
    /// </summary>
    internal static class MobileDragoonMoonRenderPatch
    {
        internal sealed class PatchTarget
        {
            internal MethodInfo Original = null!;
            internal MethodInfo Patch = null!;
            internal bool IsPrefix;
        }

        // 仅包围同步枚举服装节点的调用；finally 恢复，并隔离不同渲染线程。
        [ThreadStatic] private static PawnRenderTree? apparelSetupTree;
        private static MethodInfo addChild = null!;
        private static PawnRenderSubWorker headOffset = null!;
        private static PawnRenderSubWorker rootOffset = null!;

        internal static bool TryResolveTargets(ModContentPack framework,
            out List<PatchTarget> targets, out string failure)
        {
            targets = new List<PatchTarget>();
            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(framework,
                    "Exosuit.PawnRenderSubWorker_Offset", out Type? headType, out failure)
                || !ThirdPartyCompatibilityTargetResolver.TryResolveUniqueType(framework,
                    "Exosuit.PawnRenderSubWorker_OffsetRoot", out Type? rootType, out failure))
            {
                return false;
            }

            if (!typeof(PawnRenderSubWorker).IsAssignableFrom(headType!)
                || !typeof(PawnRenderSubWorker).IsAssignableFrom(rootType!))
            {
                failure = "Exosuit 渲染偏移类型不再继承 PawnRenderSubWorker。";
                return false;
            }

            if (!ThirdPartyCompatibilityTargetResolver.TryResolveUniqueConstructor(
                    headType!, Type.EmptyTypes, out ConstructorInfo? headCtor, out failure)
                || !ThirdPartyCompatibilityTargetResolver.TryResolveUniqueConstructor(
                    rootType!, Type.EmptyTypes, out ConstructorInfo? rootCtor, out failure))
            {
                return false;
            }

            try
            {
                addChild = RequireMethod(typeof(PawnRenderTree), "AddChild", typeof(void),
                    typeof(PawnRenderNode), typeof(PawnRenderNode));
                AddTarget(targets, typeof(PawnRenderTree), "SetupDynamicNodes", typeof(void),
                    nameof(PrefixSetupDynamicNodes), true);
                AddTarget(targets, typeof(PawnRenderTree), "ShouldAddNodeToTree", typeof(bool),
                    nameof(PostfixShouldAddNodeToTree), false, typeof(PawnRenderNodeProperties));
                AddTarget(targets, typeof(HumanApparelRenderNodeFactory), "CreateNodes", typeof(List<PawnRenderNode>),
                    nameof(PrefixCreateNodes), true, typeof(Pawn), typeof(PawnRenderTree));
                AddTarget(targets, typeof(PawnRenderTree), "AdjustParms", typeof(void),
                    nameof(PostfixAdjustParms), false, typeof(PawnDrawParms).MakeByRefType());
                AddTarget(targets, typeof(PawnRenderNodeWorker), "CanDrawNow", typeof(bool),
                    nameof(PostfixCanDrawNow), false, typeof(PawnRenderNode), typeof(PawnDrawParms));
                AddTarget(targets, typeof(PawnRenderNode), "GetTransform", typeof(void),
                    nameof(PostfixGetTransform), false, typeof(PawnDrawParms),
                    typeof(Vector3).MakeByRefType(), typeof(Vector3).MakeByRefType(),
                    typeof(Quaternion).MakeByRefType(), typeof(Vector3).MakeByRefType());
                AddTarget(targets, typeof(PawnRenderNodeWorker), "LayerFor", typeof(float),
                    nameof(PostfixLayerFor), false, typeof(PawnRenderNode), typeof(PawnDrawParms));

                headOffset = (PawnRenderSubWorker)headCtor!.Invoke(Array.Empty<object>());
                rootOffset = (PawnRenderSubWorker)rootCtor!.Invoke(Array.Empty<object>());
            }
            catch (Exception ex)
            {
                failure = "月亮龙骑兵渲染目标解析失败：" + ex.Message;
                return false;
            }

            return true;
        }

        private static MethodInfo RequireMethod(Type type, string name, Type result, params Type[] args)
        {
            MethodInfo? method = type.GetMethod(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly, null, args, null);
            if (method == null || method.ReturnType != result)
            {
                throw new MissingMethodException(type.FullName, name);
            }
            return method;
        }

        private static void AddTarget(List<PatchTarget> targets, Type type, string name,
            Type result, string patch, bool prefix, params Type[] args)
        {
            targets.Add(new PatchTarget
            {
                Original = RequireMethod(type, name, result, args),
                Patch = AccessTools.DeclaredMethod(typeof(MobileDragoonMoonRenderPatch), patch)
                    ?? throw new MissingMethodException(typeof(MobileDragoonMoonRenderPatch).FullName, patch),
                IsPrefix = prefix
            });
        }

        private static bool IsActive(Pawn? pawn)
        {
            if (!MobileDragoonMoonPilotPatch.IsMoon(pawn) || pawn!.RaceProps.Humanlike
                || pawn.apparel == null || pawn.story == null
                || !HumanApparelUtility.CanRenderHumanApparel(pawn))
            {
                return false;
            }

            foreach (Apparel apparel in pawn.apparel.WornApparel)
            {
                if (MobileDragoonMoonPilotPatch.IsDragoonCore(apparel))
                {
                    return true;
                }
            }
            return false;
        }

        private static void PrefixSetupDynamicNodes(PawnRenderTree __instance)
        {
            if (!IsActive(__instance.pawn)) return;

            PawnRenderTree? previous = apparelSetupTree;
            apparelSetupTree = __instance;
            try
            {
                // 直接调用生成器，绕开外层 HumanlikeOnly 筛选。仍由原版创建自定义节点和
                // 贴图节点，由原版 AddChild（包含 Exosuit 补丁）处理延迟父标签挂载。
                // 同时处理驾驶服等普通服装；下方禁止本项目再次补绘，以免生成两份。
                var setup = new DynamicPawnRenderNodeSetup_Apparel();
                foreach (var pair in setup.GetDynamicNodes(__instance.pawn, __instance))
                {
                    addChild.Invoke(__instance, new object[] { pair.Item1, pair.Item2 });
                }
            }
            finally
            {
                apparelSetupTree = previous;
            }
        }

        private static void PostfixShouldAddNodeToTree(PawnRenderTree __instance,
            PawnRenderNodeProperties props, ref bool __result)
        {
            if (ReferenceEquals(apparelSetupTree, __instance)
                && props?.pawnType == PawnRenderNodeProperties.RenderNodePawnType.HumanlikeOnly)
            {
                __result = true;
            }
        }

        private static bool PrefixCreateNodes(Pawn pawn, ref List<PawnRenderNode>? __result)
        {
            if (!IsActive(pawn)) return true;
            __result = null;
            return false;
        }

        private static void PostfixAdjustParms(ref PawnDrawParms parms)
        {
            if (!IsActive(parms.pawn) || !PawnRenderNodeWorker_Apparel_Head.HeadgearVisible(parms)) return;
            foreach (Apparel apparel in parms.pawn.apparel.WornApparel)
            {
                // 原版对非 Humanlike 提前返回；只补入龙骑兵模块显式要求的遮挡标记。
                if (!string.Equals(apparel.def.modContentPack?.PackageId,
                        MobileDragoonCompatibility.TargetPackageId, StringComparison.OrdinalIgnoreCase)
                    || apparel.def.apparel.renderSkipFlags == null) continue;
                foreach (RenderSkipFlagDef flag in apparel.def.apparel.renderSkipFlags)
                {
                    if (flag != RenderSkipFlagDefOf.None) parms.skipFlags |= flag;
                }
            }
        }

        private static bool HasSubWorker(PawnRenderNode node, PawnRenderSubWorker worker)
        {
            foreach (PawnRenderSubWorker existing in node.Props.SubWorkers)
            {
                if (existing.GetType() == worker.GetType()) return true;
            }
            return false;
        }

        private static void PostfixCanDrawNow(PawnRenderNode node, PawnDrawParms parms, ref bool __result)
        {
            if (!__result || !IsActive(parms.pawn)) return;
            // 月亮的 AnimalPart 身体没有 skipFlag=Body；只在实际驾驶时补足遮挡。
            if (node.Props.tagDef == PawnRenderNodeTagDefOf.Body)
            {
                // Body 没有原版 RenderSkipFlagDefOf 字段，按 XML 的 defName 在运行期解析。
                // 不在静态初始化时缓存，避免 Def 尚未加载；缺失时保留原显示结果。
                RenderSkipFlagDef? bodyFlag = DefDatabase<RenderSkipFlagDef>.GetNamedSilentFail("Body");
                if (bodyFlag != null && parms.skipFlags.HasFlag(bodyFlag))
                {
                    __result = false;
                }
            }
            else if (node.Props.tagDef == PawnRenderNodeTagDefOf.Head && !HasSubWorker(node, headOffset))
            {
                __result = headOffset.CanDrawNowSub(node, parms);
            }
        }

        private static void PostfixGetTransform(PawnRenderNode __instance, PawnDrawParms parms,
            ref Vector3 offset, ref Vector3 pivot)
        {
            if (!IsActive(parms.pawn)) return;
            PawnRenderSubWorker? worker = ReferenceEquals(__instance, __instance.tree.rootNode)
                ? rootOffset : __instance.Props.tagDef == PawnRenderNodeTagDefOf.Head ? headOffset : null;
            if (worker != null && !HasSubWorker(__instance, worker))
            {
                // 框架 XML 只为 Humanlike 树加入这些子工作器；复用其实现补足 MAP_Moon。
                worker.TransformOffset(__instance, parms, ref offset, ref pivot);
            }
        }

        private static void PostfixLayerFor(PawnRenderNode node, PawnDrawParms parms, ref float __result)
        {
            if (ReferenceEquals(node, node.tree.rootNode) && IsActive(parms.pawn)
                && !HasSubWorker(node, rootOffset))
            {
                rootOffset.TransformLayer(node, parms, ref __result);
            }
        }
    }
}
