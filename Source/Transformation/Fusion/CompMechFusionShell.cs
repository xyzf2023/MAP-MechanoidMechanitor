using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_MechFusionShell : CompProperties
    {
        public CompProperties_MechFusionShell()
        {
            compClass = typeof(CompMechFusionShell);
        }
    }

    /// <summary>
    /// 合体服装连接组件。这里只保存与权威合体记录连接所需的稳定 ID，
    /// 不保存第二份能源、结构稳定值或属性数据；脱下与销毁通知在第二轮接入
    /// 统一解除合体服务。
    /// </summary>
    public sealed class CompMechFusionShell : ThingComp
    {
        private string? sessionId;

        public string? SessionId => sessionId;

        internal static bool IsWornBy(Pawn? pawn)
        {
            if (pawn?.apparel == null)
            {
                return false;
            }

            foreach (Apparel apparel in pawn.apparel.WornApparel)
            {
                MechFusionSession? session = apparel
                    .TryGetComp<CompMechFusionShell>()?.GetBoundSession();
                if (session != null && ReferenceEquals(session.WearerPawn, pawn))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 仅当会话ID有效、会话处于 Active 且本服装确实是该会话的载体时返回会话。
        /// 活动耐久豁免与实例属性读取都必须经过这里，损坏链接不得被保护。
        /// </summary>
        internal MechFusionSession? GetActiveSession()
        {
            MechFusionSession? session = GetBoundSession();
            return session != null && session.IsActive ? session : null;
        }

        private MechFusionSession? GetBoundSession()
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return null;
            }

            if (!GameComponent_MechFusionSessionRegistry.TryGetSessionById(
                    sessionId,
                    out MechFusionSession? session)
                || session == null
                || !ReferenceEquals(session.FusionApparel, parent))
            {
                return null;
            }

            return session;
        }

        /// <summary>
        /// 由当前服装实例提供专属工作速度。数值仍只保存在权威会话中，
        /// 组件只负责把已绑定会话的快照接入原版装备属性入口。
        /// </summary>
        internal bool TryGetWorkSpeedOffset(StatDef? stat, out float value)
        {
            value = 0f;
            if (!MechFusionStatUtility.IsApparelWorkSpeedStat(stat))
            {
                return false;
            }

            MechFusionSession? session = GetActiveSession();
            return session != null
                && session.TryGetStatOffset(stat, out value)
                && value != 0f;
        }

        public override IEnumerable<StatDrawEntry> SpecialDisplayStats()
        {
            MechFusionSession? session = GetActiveSession();
            if (session == null)
            {
                yield break;
            }

            IReadOnlyList<MechFusionStatEntry> offsets = session.StatOffsets;
            for (int i = 0; i < offsets.Count; i++)
            {
                MechFusionStatEntry? entry = offsets[i];
                StatDef? stat = entry?.stat;
                if (!MechFusionStatUtility.IsApparelWorkSpeedStat(stat)
                    || entry!.value == 0f)
                {
                    continue;
                }

                yield return new StatDrawEntry(
                    StatCategoryDefOf.EquippedStatOffsets,
                    stat!,
                    entry.value,
                    StatRequest.ForEmpty(),
                    ToStringNumberSense.Offset,
                    forceUnfinalizedMode: true);
            }
        }

        internal void AssignSession(string value)
        {
            sessionId = value;
        }

        internal void ClearSession()
        {
            sessionId = null;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref sessionId, "sessionId");
        }

        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            if (ReferenceEquals(GetBoundSession()?.WearerPawn, pawn))
            {
                pawn.GetAttachment(ThingDefOf.Fire)?.Destroy();
            }
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            MechFusionTeardownService.NotifyShellUnequipped(this, pawn);
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            MechFusionTeardownService.NotifyShellDestroyed(this, previousMap);
        }
    }
}
