using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(ApparelGraphicRecordGetter), nameof(ApparelGraphicRecordGetter.TryGetGraphicApparel))]
    public static class HumanApparelGraphicPatches
    {
        [HarmonyPrefix]
        public static void Prefix(Apparel apparel, ref BodyTypeDef bodyType)
        {
            if (bodyType != null && bodyType.defName == "MAP_LoverFemale")
            {
                bodyType = BodyTypeDefOf.Female;
            }

            if (apparel?.Wearer == null)
            {
                return;
            }

            if (!HumanApparelUtility.TryGetApparelComp(apparel.Wearer, out CompHumanApparelUser? comp)
                || !comp!.EnableHumanApparelRendering
                || comp.ApparelBodyType == null)
            {
                return;
            }

            bodyType = comp.ApparelBodyType;
        }
    }
}
