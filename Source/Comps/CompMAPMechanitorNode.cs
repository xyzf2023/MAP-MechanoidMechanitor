using System;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompProperties_MAPMechanitorNode : CompProperties
    {
        public MAPMechanitorControlBackend controlBackend = MAPMechanitorControlBackend.None;
        // Whether this node itself needs an external overseer; does not affect its ability to control other mechs.
        public bool requiresExternalOverseer = false;
        // Whether this externally overseen node ignores its overseer's command radius while it is actually controlled.
        public bool ignoreExternalOverseerCommandRange = false;
        public int extraMechBandwidth = 0;
        public int extraMechControlGroups = 0;
        public bool allowBossChipBandwidthUpgrade = false;
        public int maxIntrinsicBandwidth = 0;

        public CompProperties_MAPMechanitorNode()
        {
            compClass = typeof(CompMAPMechanitorNode);
        }
    }

    public class CompMAPMechanitorNode : ThingComp
    {
        private int chipBandwidthBonus;
        private bool bandwidthRefreshQueued;

        public CompProperties_MAPMechanitorNode? NodeProps => props as CompProperties_MAPMechanitorNode;

        public int ChipBandwidthBonus => GetAuthoritativeChipBandwidthBonus();

        public int BaseExtraMechBandwidth => NodeProps?.extraMechBandwidth ?? 0;

        public int CurrentIntrinsicBandwidth => BaseExtraMechBandwidth + ChipBandwidthBonus;

        public int MaxIntrinsicBandwidth => NodeProps?.maxIntrinsicBandwidth ?? 0;

        public bool AllowsBossChipBandwidthUpgrade =>
            NodeProps?.allowBossChipBandwidthUpgrade ?? false;

        public int RemainingIntrinsicBandwidth
        {
            get
            {
                if (!AllowsBossChipBandwidthUpgrade
                    || MaxIntrinsicBandwidth <= 0
                    || NodeProps == null
                    || BaseExtraMechBandwidth >= MaxIntrinsicBandwidth)
                {
                    return 0;
                }

                return Math.Max(0, MaxIntrinsicBandwidth - CurrentIntrinsicBandwidth);
            }
        }

        public static bool PawnHasNode(Pawn? pawn)
        {
            return TryGetNodeComp(pawn, out _);
        }

        public static bool TryGetNodeComp(Pawn? pawn, out CompMAPMechanitorNode? comp)
        {
            comp = null;
            if (pawn == null)
            {
                return false;
            }

            comp = pawn.GetComp<CompMAPMechanitorNode>();
            return comp != null;
        }

        internal int GetLegacyChipBandwidthBonusForMigration() => chipBandwidthBonus;

        public int AddChipBandwidth(int requestedAmount)
        {
            return MechanoidMechanitorRoleUtility.AddChipBandwidth(
                parent as Pawn,
                requestedAmount);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            int loadedValue = chipBandwidthBonus;
            Scribe_Values.Look(ref chipBandwidthBonus, "chipBandwidthBonus", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                int maxBonus = Math.Max(0, MaxIntrinsicBandwidth - BaseExtraMechBandwidth);
                int clamped = Mathf.Clamp(chipBandwidthBonus, 0, maxBonus);
                if (clamped != loadedValue)
                {
                    chipBandwidthBonus = clamped;
                    QueueBandwidthRefreshAfterLoad();
                }
                else
                {
                    chipBandwidthBonus = clamped;
                }
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            if (parent is not Pawn pawn)
            {
                return;
            }

            CompProperties_MAPMechanitorNode? nodeProps = NodeProps;
            if (nodeProps == null)
            {
                return;
            }

            if (nodeProps.controlBackend == MAPMechanitorControlBackend.Vanilla)
            {
                MAPMechanitorNodeLifecycleUtility.EnsureBasicTrackers(pawn);

                if (!nodeProps.requiresExternalOverseer)
                {
                    MAPOverseerlessNodeUtility.ClearExternalOverseerIfNode(pawn);
                }

                if (Prefs.DevMode)
                {
                    Log.Message(
                        $"[MAP-MechanoidMechanitor] MAP mechanitor node ({nodeProps.controlBackend}): {pawn.LabelShort}, " +
                        $"requiresExternalOverseer={nodeProps.requiresExternalOverseer}, " +
                        $"mechanitor={(pawn.mechanitor != null)}, relations={(pawn.relations != null)}, " +
                        $"noOverseer={(pawn.GetOverseer() == null)}, " +
                        $"totalBandwidth={pawn.mechanitor?.TotalBandwidth}, " +
                        $"controlGroups={pawn.mechanitor?.controlGroups?.Count}");
                }
            }

            if (pawn.mechanitor != null)
            {
                NotifyBandwidthChanged();
            }
        }

        private int GetAuthoritativeChipBandwidthBonus()
        {
            if (parent is Pawn pawn
                && GameComponent_MechanoidMechanitorRegistry.TryGetNativeMechanitorRecord(
                    pawn,
                    out MechanoidMechanitorRecord? record)
                && record != null
                && !record.PendingLegacyNativeStateImport)
            {
                return record.ChipBandwidthBonus;
            }

            return chipBandwidthBonus;
        }

        private void QueueBandwidthRefreshAfterLoad()
        {
            if (bandwidthRefreshQueued)
            {
                return;
            }

            bandwidthRefreshQueued = true;
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                bandwidthRefreshQueued = false;

                if (parent is not Pawn pawn
                    || pawn.Destroyed
                    || pawn.mechanitor == null)
                {
                    return;
                }

                NotifyBandwidthChanged();
            });
        }

        private void NotifyBandwidthChanged()
        {
            if (parent is Pawn pawn && pawn.mechanitor != null)
            {
                pawn.mechanitor.Notify_BandwidthChanged();
            }
        }
    }
}
