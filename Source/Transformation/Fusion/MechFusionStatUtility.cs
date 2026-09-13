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
                // MoveSpeed 必须在 StatWorker.FinalizeValue 完成后强制覆盖，
                // 否则会再次受到人类 Moving 容量与 0.15 最小值钳制。
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

        internal static bool TryGetForcedMoveSpeed(
            Pawn pawn,
            out float value)
        {
            value = 0f;
            if (pawn == null
                || !GameComponent_MechFusionSessionRegistry.TryGetSessionForWearer(
                    pawn,
                    out MechFusionSession? session)
                || session == null
                || !session.IsActive)
            {
                return false;
            }

            session.TryGetStatOffset(StatDefOf.MoveSpeed, out float offset);
            session.TryGetStatFactor(StatDefOf.MoveSpeed, out float factor);
            value = Math.Max(
                0f,
                (session.MoveSpeedBase + offset) * factor);
            return true;
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
