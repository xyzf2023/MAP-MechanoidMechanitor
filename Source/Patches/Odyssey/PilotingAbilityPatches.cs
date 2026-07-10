using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.IsDisabledFor))]
    public static class PilotingAbility_StatWorker_IsDisabledFor_Patch
    {
        private const int ErrorKeyStatFieldMissing = 879346101;

        private static readonly FieldInfo? StatField =
            AccessTools.Field(typeof(StatWorker), "stat");

        public static void Postfix(StatWorker __instance, Thing thing, ref bool __result)
        {
            if (!__result)
            {
                return;
            }

            if (!ModsConfig.OdysseyActive)
            {
                return;
            }

            if (thing is not Pawn pawn || !pawn.RaceProps.IsMechanoid)
            {
                return;
            }

            if (!MechanoidMechanitorRoleUtility.AllowsShuttlePilot(pawn))
            {
                return;
            }

            if (StatField == null)
            {
                Log.ErrorOnce(
                    "[MAP-机械族机械师] 无法解析 StatWorker.stat 字段，PilotingAbility 禁用绕过未应用。",
                    ErrorKeyStatFieldMissing);
                return;
            }

            if (StatField.GetValue(__instance) is not StatDef stat
                || stat != StatDefOf.PilotingAbility)
            {
                return;
            }

            __result = false;
        }
    }
}
