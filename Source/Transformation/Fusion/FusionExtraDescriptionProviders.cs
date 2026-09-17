using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class HediffExtraDescriptionProvider_BodySynchronization : HediffExtraDescriptionProvider
    {
        public override IEnumerable<HediffExtraDescriptionEntry> GetEntries(Hediff hediff)
        {
            if (hediff.pawn == null
                || !GameComponent_MechFusionSessionRegistry.TryGetSessionForWearer(
                    hediff.pawn, out MechFusionSession? session)
                || session == null || !session.IsActive)
            {
                yield break;
            }

            yield return StatEntry(StatDefOf.MoveSpeed,
                MechFusionStatUtility.GetForcedMoveSpeed(session), ToStringNumberSense.Absolute, true);
            foreach (var entry in StatEntries(session.StatOffsets, ToStringNumberSense.Offset, true))
            {
                yield return entry;
            }
            foreach (var entry in StatEntries(session.StatFactors, ToStringNumberSense.Factor, false))
            {
                yield return entry;
            }

            if (session.TemporaryFlightAuthorized)
            {
                yield return new HediffExtraDescriptionEntry("MAP_MechanoidMechanitor.HediffExtra.FlightAuthorized");
            }
            if (session.RepairBeaconAuthorized
                && !ImplantEffectUtility.HasHediff(hediff.pawn, MAPMechanitor_HediffDefOf.MAP_FusionStructuralRepair))
            {
                yield return new HediffExtraDescriptionEntry("MAP_MechanoidMechanitor.HediffExtra.StructuralStabilization");
            }
        }

        private static HediffExtraDescriptionEntry StatEntry(
            StatDef stat, float value, ToStringNumberSense sense, bool finalized)
        {
            return new HediffExtraDescriptionEntry("MAP_MechanoidMechanitor.HediffExtra.StatValue",
                stat.LabelCap.ToString(), stat.ValueToString(value, sense, finalized: finalized));
        }

        private static IEnumerable<HediffExtraDescriptionEntry> StatEntries(
            IReadOnlyList<MechFusionStatEntry> entries, ToStringNumberSense sense, bool skipApparelWorkSpeedOffsets)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                MechFusionStatEntry? entry = entries[i];
                StatDef? stat = entry?.stat;
                if (stat == null || stat == StatDefOf.MoveSpeed
                    || (skipApparelWorkSpeedOffsets && MechFusionStatUtility.IsApparelWorkSpeedStat(stat)))
                {
                    continue;
                }

                yield return StatEntry(stat, entry!.value, sense, false);
            }
        }
    }

    public sealed class HediffExtraDescriptionProvider_MechControlSynchronization : HediffExtraDescriptionProvider
    {
        public override IEnumerable<HediffExtraDescriptionEntry> GetEntries(Hediff hediff)
        {
            if (hediff.pawn == null
                || !GameComponent_MechFusionSessionRegistry.TryGetSessionForWearer(
                    hediff.pawn, out MechFusionSession? session))
            {
                yield break;
            }

            MechFusionMechanitorSnapshot? snapshot = session?.MechanitorSnapshot;
            if (snapshot == null)
            {
                yield break;
            }

            if (!snapshot.wearerWasMechanitor)
            {
                yield return new HediffExtraDescriptionEntry("MAP_MechanoidMechanitor.HediffExtra.MechanitorConnected");
            }
            if (snapshot.grantsQuantumCommunicator)
            {
                yield return new HediffExtraDescriptionEntry("MAP_MechanoidMechanitor.HediffExtra.QuantumCommunicator");
            }
            if (snapshot.grantsProxySubchain)
            {
                yield return new HediffExtraDescriptionEntry("MAP_MechanoidMechanitor.HediffExtra.ProxySubchain");
            }
        }
    }
}
