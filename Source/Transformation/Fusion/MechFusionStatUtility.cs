using System;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 窄范围的实例 Stat 提供入口。只有固定合体服装与存在有效活动合体记录
    /// 的人类才会读取快照；其他对象立即走原版路径。
    /// </summary>
    internal static class MechFusionStatUtility
    {
        private const float Epsilon = 0.0001f;

        internal static bool IsArmorStat(StatDef? stat)
        {
            return stat == StatDefOf.ArmorRating_Sharp
                || stat == StatDefOf.ArmorRating_Blunt
                || stat == StatDefOf.ArmorRating_Heat;
        }

        internal static void ApplyToPawn(
            Pawn pawn,
            StatDef stat,
            ref float value)
        {
            if (pawn == null
                || stat == null
                || IsArmorStat(stat)
                || !GameComponent_MechFusionSessionRegistry.TryGetSessionForWearer(
                    pawn,
                    out MechFusionSession? session)
                || session == null
                || !session.IsActive)
            {
                return;
            }

            if (stat == StatDefOf.MoveSpeed)
            {
                session.TryGetStatOffset(stat, out float speedOffset);
                session.TryGetStatFactor(stat, out float speedFactor);
                value = (session.MoveSpeedBase + speedOffset) * speedFactor;
                return;
            }

            if (session.TryGetStatOffset(stat, out float offset)
                && Math.Abs(offset) > Epsilon)
            {
                value += offset;
            }

            if (session.TryGetStatFactor(stat, out float factor)
                && Math.Abs(factor - 1f) > Epsilon)
            {
                value *= factor;
            }
        }

        internal static void ApplyToApparel(
            Apparel apparel,
            StatDef stat,
            ref float value)
        {
            if (apparel == null || stat == null || !IsArmorStat(stat))
            {
                return;
            }

            CompMechFusionShell? shellComp =
                apparel.TryGetComp<CompMechFusionShell>();
            if (shellComp == null
                || !GameComponent_MechFusionSessionRegistry.TryGetSessionById(
                    shellComp.SessionId,
                    out MechFusionSession? session)
                || session == null
                || !session.IsActive)
            {
                return;
            }

            if (stat == StatDefOf.ArmorRating_Sharp)
            {
                value = session.ArmorSharp;
            }
            else if (stat == StatDefOf.ArmorRating_Blunt)
            {
                value = session.ArmorBlunt;
            }
            else
            {
                value = session.ArmorHeat;
            }
        }
    }
}
