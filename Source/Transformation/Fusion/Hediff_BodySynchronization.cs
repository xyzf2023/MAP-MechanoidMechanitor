using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// “机体同调”的可见说明。实际加成仍由权威合体会话提供，
    /// 本 Hediff 只在玩家查看健康状态时按当前会话生成文字，
    /// 不修改全局 HediffDef，也不复制实例数据。
    /// </summary>
    public sealed class Hediff_BodySynchronization : Hediff
    {
        public override string TipStringExtra
        {
            get
            {
                StringBuilder builder = new StringBuilder();
                string baseTip = base.TipStringExtra;
                if (!baseTip.NullOrEmpty())
                {
                    builder.AppendLine(baseTip);
                }

                if (pawn == null
                    || !GameComponent_MechFusionSessionRegistry
                        .TryGetSessionForWearer(
                            pawn,
                            out MechFusionSession? session)
                    || session == null
                    || !session.IsActive)
                {
                    return builder.ToString().TrimEnd('\r', '\n');
                }

                AppendAbsolute(
                    builder,
                    StatDefOf.MoveSpeed,
                    GetForcedMoveSpeed(session));
                AppendAbsolute(
                    builder,
                    StatDefOf.ArmorRating_Sharp,
                    session.ArmorSharp);
                AppendAbsolute(
                    builder,
                    StatDefOf.ArmorRating_Blunt,
                    session.ArmorBlunt);
                AppendAbsolute(
                    builder,
                    StatDefOf.ArmorRating_Heat,
                    session.ArmorHeat);
                AppendEntries(
                    builder,
                    session.StatOffsets,
                    ToStringNumberSense.Offset);
                AppendEntries(
                    builder,
                    session.StatFactors,
                    ToStringNumberSense.Factor);

                if (session.TemporaryFlightAuthorized)
                {
                    builder.AppendLine(" - 飞行能力：已准许");
                }

                return builder.ToString().TrimEnd('\r', '\n');
            }
        }

        private static float GetForcedMoveSpeed(MechFusionSession session)
        {
            session.TryGetStatOffset(StatDefOf.MoveSpeed, out float offset);
            session.TryGetStatFactor(StatDefOf.MoveSpeed, out float factor);
            return (session.MoveSpeedBase + offset) * factor;
        }

        private static void AppendAbsolute(
            StringBuilder builder,
            StatDef stat,
            float value)
        {
            builder.Append(" - ");
            builder.Append(stat.LabelCap);
            builder.Append(": ");
            builder.AppendLine(
                stat.ValueToString(
                    value,
                    ToStringNumberSense.Absolute,
                    finalized: true));
        }

        private static void AppendEntries(
            StringBuilder builder,
            IReadOnlyList<MechFusionStatEntry> entries,
            ToStringNumberSense numberSense)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                MechFusionStatEntry? entry = entries[i];
                StatDef? stat = entry?.stat;
                if (stat == null || stat == StatDefOf.MoveSpeed)
                {
                    continue;
                }

                builder.Append(" - ");
                builder.Append(stat.LabelCap);
                builder.Append(": ");
                builder.AppendLine(
                    stat.ValueToString(
                        entry!.value,
                        numberSense,
                        finalized: false));
            }
        }
    }
}
