using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// “机控同调”的可见载体。动态 StatStage 只读取合体会话中的不可变快照；
    /// 控制权和控制组迁移由 MechFusionMechanitorSynchronizationService 负责。
    /// </summary>
    public sealed class Hediff_MechControlSynchronization : Hediff
    {
        private HediffStage? cachedStage;
        private int cachedBandwidth = int.MinValue;
        private int cachedControlGroups = int.MinValue;

        public override HediffStage CurStage
        {
            get
            {
                ResolveBonuses(out int bandwidth, out int controlGroups);
                if (cachedStage == null
                    || cachedBandwidth != bandwidth
                    || cachedControlGroups != controlGroups)
                {
                    cachedBandwidth = bandwidth;
                    cachedControlGroups = controlGroups;
                    cachedStage = new HediffStage
                    {
                        statOffsets = new List<StatModifier>
                        {
                            new StatModifier
                            {
                                stat = StatDefOf.MechBandwidth,
                                value = bandwidth
                            },
                            new StatModifier
                            {
                                stat = StatDefOf.MechControlGroups,
                                value = controlGroups
                            }
                        }
                    };
                }

                return cachedStage;
            }
        }

        public override string TipStringExtra
        {
            get
            {
                StringBuilder builder = new StringBuilder();
                string baseTip = base.TipStringExtra.TrimEnd('\r', '\n');
                if (!baseTip.NullOrEmpty())
                {
                    builder.Append(baseTip);
                }

                MechFusionMechanitorSnapshot? snapshot = ResolveSnapshot();
                AppendEffectLine(
                    builder,
                    snapshot != null && !snapshot.wearerWasMechanitor,
                    " - 机控系统已接入");
                AppendEffectLine(
                    builder,
                    snapshot?.grantsQuantumCommunicator == true,
                    " - 量子通讯器已启用");
                AppendEffectLine(
                    builder,
                    snapshot?.grantsProxySubchain == true,
                    " - 代理子链已启用");
                return builder.ToString().TrimEnd('\r', '\n');
            }
        }

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            if (pawn != null && ModsConfig.BiotechActive)
            {
                PawnComponentsUtility.AddAndRemoveDynamicComponents(pawn);
            }
        }

        public override void PostRemoved()
        {
            Pawn? localPawn = pawn;
            base.PostRemoved();
            if (localPawn != null && ModsConfig.BiotechActive)
            {
                PawnComponentsUtility.AddAndRemoveDynamicComponents(localPawn);
            }
        }

        private void ResolveBonuses(out int bandwidth, out int controlGroups)
        {
            bandwidth = 0;
            controlGroups = 0;
            MechFusionMechanitorSnapshot? snapshot = ResolveSnapshot();
            if (snapshot == null)
            {
                return;
            }

            bandwidth = snapshot.bandwidthBonus;
            controlGroups = snapshot.controlGroupBonus;
        }

        private MechFusionMechanitorSnapshot? ResolveSnapshot()
        {
            if (pawn == null
                || !GameComponent_MechFusionSessionRegistry.TryGetSessionForWearer(
                    pawn,
                    out MechFusionSession? session))
            {
                return null;
            }

            return session?.MechanitorSnapshot;
        }

        private static void AppendEffectLine(
            StringBuilder builder,
            bool shouldAppend,
            string text)
        {
            if (!shouldAppend)
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(text);
        }
    }
}
