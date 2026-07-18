using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 授权机械体床上 Lovin 时复用原版 Humanlike 床上绘制分支；不修改 RaceProps.Humanlike。
    /// </summary>
    public static class SyntheticLovinRenderPatches
    {
        private const string LogPrefix = "[MAP-机械族机械师] SyntheticLovinRenderPatches：";
        private const int ErrorKeyGetBodyPosNotFound = 879346711;
        private const int ErrorKeyGetBodyPosException = 879346712;

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
                if (!SyntheticLovinUtility.TryGetSyntheticLovinBed(
                        ___pawn,
                        isPortrait: false,
                        out Building_Bed? bed)
                    || bed?.def?.building == null
                    || ___pawn.story?.bodyType == null)
                {
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
                catch (System.Exception ex)
                {
                    Log.ErrorOnce(
                        $"{LogPrefix}GetBodyPos 特殊渲染异常，已回退原版。"
                        + $" pawn={___pawn?.LabelShort}/{___pawn?.ThingID}"
                        + $", CurJobDef={___pawn?.CurJobDef?.defName ?? "null"}"
                        + $"\n{ex}",
                        ErrorKeyGetBodyPosException);
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
                if (!SyntheticLovinUtility.TryGetSyntheticLovinBed(
                        ___pawn,
                        flags.FlagSet(PawnRenderFlags.Portrait),
                        out Building_Bed? bed)
                    || bed == null)
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
                if (!SyntheticLovinUtility.TryGetSyntheticLovinBed(
                        ___pawn,
                        isPortrait: false,
                        out Building_Bed? bed)
                    || bed == null)
                {
                    return true;
                }

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

                if (posture.FaceUp())
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
                    || parms.flags.FlagSet(PawnRenderFlags.Portrait)
                    || parms.flags.FlagSet(PawnRenderFlags.NoBody)
                    || parms.posture == PawnPosture.Standing)
                {
                    return;
                }

                Pawn? pawn = parms.pawn;
                Building_Bed? bed = parms.bed;
                // 与 TryGetSyntheticLovinBed 同序廉价淘汰；直接复用 parms.bed，不重复 CurrentBed。
                if (pawn == null
                    || pawn.CurJobDef != JobDefOf.Lovin
                    || !parms.posture.InBed()
                    || bed?.def?.building == null
                    || !SyntheticCompanionStateUtility.IsSyntheticCompanion(pawn))
                {
                    return;
                }

                Pawn_MindState? mindState = pawn.mindState;
                if (mindState != null && mindState.duty?.def?.drawBodyOverride.HasValue == true)
                {
                    return;
                }

                __result = bed.def.building.bed_showSleeperBody;
            }
        }
    }
}
