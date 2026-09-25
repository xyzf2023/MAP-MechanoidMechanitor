using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 原版 Joyous 未检查被评价对象的人格数据；社交面板可能向无人格的机械体查询此思想。
    /// 缺少特性数据时不具备 Joyous 特性，不为此创建人格，也不屏蔽其他社交思想。
    /// </summary>
    [HarmonyPatch(typeof(ThoughtWorker_Joyous), "CurrentSocialStateInternal")]
    internal static class SyntheticCompanionJoyousThoughtPatch
    {
        private static bool Prefix(Pawn other, ref ThoughtState __result)
        {
            if (other.story?.traits != null) return true;
            __result = ThoughtState.Inactive;
            return false;
        }
    }
}
