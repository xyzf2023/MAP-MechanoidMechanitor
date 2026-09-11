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

        // 编译期绑定字段访问器；正常路径不再使用 FieldInfo.GetValue 反射。
        private static readonly AccessTools.FieldRef<StatWorker, StatDef>? StatFieldRef =
            CreateStatFieldRef();

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

            AccessTools.FieldRef<StatWorker, StatDef>? statFieldRef = StatFieldRef;
            if (statFieldRef == null)
            {
                Log.ErrorOnce(
                    "[MAP-机械族机械师] 无法解析 StatWorker.stat 字段，PilotingAbility 禁用绕过未应用。",
                    ErrorKeyStatFieldMissing);
                return;
            }

            // 只处理驾驶能力 Stat，其他被禁用 Stat 一律保持原版结果。
            if (statFieldRef(__instance) != StatDefOf.PilotingAbility)
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

            __result = false;
        }

        private static AccessTools.FieldRef<StatWorker, StatDef>? CreateStatFieldRef()
        {
            if (StatField == null)
            {
                return null;
            }

            try
            {
                return AccessTools.FieldRefAccess<StatWorker, StatDef>("stat");
            }
            catch
            {
                return null;
            }
        }
    }
}
