using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 授权恋人床上 Lovin 时复用原版 Humanlike 床上绘制分支；不修改 RaceProps.Humanlike。
    /// </summary>
    public static class ExplicitSocialLovinRenderPatches
    {
        private const string LogPrefix = "[MAP-机械族机械师] ExplicitSocialLovinRenderPatches：";
        private const int ErrorKeyGetBodyPosNotFound = 879346711;

        [HarmonyPatch]
        public static class Patch_PawnRenderer_GetBodyPos
        {
            private static MethodInfo? cachedTargetMethod;

            private static bool Prepare()
            {
                if (TargetMethod() != null)
                {
                    return true;
                }

                Log.ErrorOnce(
                    $"{LogPrefix}未找到 PawnRenderer.GetBodyPos(Vector3, PawnPosture, out bool)，补丁未应用。",
                    ErrorKeyGetBodyPosNotFound);
                return false;
            }

            private static MethodBase? TargetMethod()
            {
                if (cachedTargetMethod != null)
                {
                    return cachedTargetMethod;
                }

                cachedTargetMethod = AccessTools.Method(
                    typeof(PawnRenderer),
                    "GetBodyPos",
                    new[] { typeof(Vector3), typeof(PawnPosture), typeof(bool).MakeByRefType() });
                return cachedTargetMethod;
            }

            [HarmonyPrefix]
            public static bool Prefix(
                PawnRenderer __instance,
                Pawn ___pawn,
                Vector3 drawLoc,
                PawnPosture posture,
                ref bool showBody,
                ref Vector3 __result)
            {
                if (!ExplicitSocialLovinUtility.ShouldUseHumanlikeBedLovinRender(___pawn))
                {
                    return true;
                }

                Building_Bed? bed = ___pawn.CurrentBed();
                if (bed?.def?.building == null
                    || ___pawn.story?.bodyType == null)
                {
                    // 缺少人类床上偏移所需数据时回退原版。
                    return true;
                }

                try
                {
                    showBody = bed.def.building.bed_showSleeperBody;
                    AltitudeLayer altLayer = (AltitudeLayer)Mathf.Max((int)bed.def.altitudeLayer, 20);
                    Vector3 vector = ___pawn.Position.ToVector3ShiftedWithAltitude(altLayer);
                    Rot4 rotation = bed.Rotation;
                    rotation.AsInt += 2;
                    float num = __instance.BaseHeadOffsetAt(Rot4.South).z
                        + ___pawn.story.bodyType.bedOffset
                        + bed.def.building.bed_pawnDrawOffset;
                    Vector3 vector2 = rotation.FacingCell.ToVector3();
                    __result = vector - vector2 * num;
                    showBody = ___pawn.mindState?.duty?.def?.drawBodyOverride ?? showBody;
                    return false;
                }
                catch (System.Exception)
                {
                    return true;
                }
            }
        }

        [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.BodyAngle))]
        public static class Patch_PawnRenderer_BodyAngle
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn ___pawn, PawnRenderFlags flags, ref float __result)
            {
                if (!ExplicitSocialLovinUtility.ShouldUseHumanlikeBedLovinRender(___pawn, flags))
                {
                    return true;
                }

                Building_Bed? bed = ___pawn.CurrentBed();
                if (bed == null)
                {
                    return true;
                }

                Rot4 rotation = bed.Rotation;
                rotation.AsInt += 2;
                __result = rotation.AsAngle;
                return false;
            }
        }

        [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.LayingFacing))]
        public static class Patch_PawnRenderer_LayingFacing
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn ___pawn, ref Rot4 __result)
            {
                if (!ExplicitSocialLovinUtility.ShouldUseHumanlikeBedLovinRender(___pawn))
                {
                    return true;
                }

                // 复刻原版 ForcedLayingRotation / FaceUp 优先，以及 Humanlike 成年躺卧朝向分布。
                if (___pawn.jobs?.curDriver != null
                    && ___pawn.jobs.curDriver.ForcedLayingRotation.IsValid)
                {
                    __result = ___pawn.jobs.curDriver.ForcedLayingRotation;
                    return false;
                }

                PawnPosture posture = ___pawn.GetPosture();
                if (posture == PawnPosture.LayingOnGroundFaceUp || ___pawn.Deathresting)
                {
                    __result = Rot4.South;
                    return false;
                }

                if (posture.FaceUp() && ___pawn.CurrentBed() != null)
                {
                    __result = Rot4.South;
                    return false;
                }

                switch (___pawn.thingIDNumber % 4)
                {
                    case 0:
                    case 1:
                        __result = Rot4.South;
                        break;
                    case 2:
                        __result = Rot4.East;
                        break;
                    default:
                        __result = Rot4.West;
                        break;
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(PawnRenderNodeWorker_Body), nameof(PawnRenderNodeWorker_Body.CanDrawNow))]
        public static class Patch_PawnRenderNodeWorker_Body_CanDrawNow
        {
            [HarmonyPostfix]
            public static void Postfix(PawnRenderNode node, PawnDrawParms parms, ref bool __result)
            {
                if (!__result)
                {
                    return;
                }

                if (parms.Portrait
                    || parms.flags.FlagSet(PawnRenderFlags.NoBody)
                    || parms.posture == PawnPosture.Standing)
                {
                    return;
                }

                if (!ExplicitSocialLovinUtility.ShouldUseHumanlikeBedLovinRender(
                        parms.pawn,
                        parms.flags))
                {
                    return;
                }

                Pawn_MindState? mindState = parms.pawn.mindState;
                if (mindState != null && mindState.duty?.def?.drawBodyOverride.HasValue == true)
                {
                    // 保留原版 duty.drawBodyOverride 结果。
                    return;
                }

                if (parms.bed?.def?.building == null)
                {
                    return;
                }

                __result = parms.bed.def.building.bed_showSleeperBody;
            }
        }
    }
}
