using System.Collections.Generic;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public abstract class HediffComp_DistributedImplantEffect : HediffComp
    {
        private const int SyncIntervalTicks = 60;

        private List<Pawn>? affectedPawns = new List<Pawn>();
        private int ticksUntilSync;

        protected abstract HediffDef DistributedHediffDef { get; }

        protected virtual bool IncludeMechanoidMechanitorSelf => true;

        protected virtual bool CanApplyTo(Pawn recipient, bool isSelf) => true;

        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            base.CompPostPostAdd(dinfo);
            ticksUntilSync = SyncIntervalTicks;
            SyncEffects();
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            ticksUntilSync--;
            if (ticksUntilSync <= 0)
            {
                ticksUntilSync = SyncIntervalTicks;
                SyncEffects();
            }
        }

        public override void CompPostPostRemoved()
        {
            RemoveAllEffects();
            base.CompPostPostRemoved();
        }

        public override void Notify_PawnDied(DamageInfo? dinfo, Hediff? culprit = null)
        {
            RemoveAllEffects();
            base.Notify_PawnDied(dinfo, culprit);
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Collections.Look(ref affectedPawns, "affectedPawns", LookMode.Reference);
            Scribe_Values.Look(ref ticksUntilSync, "ticksUntilSync", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                affectedPawns ??= new List<Pawn>();
                affectedPawns.RemoveAll(pawn => pawn == null);
            }
        }

        private void SyncEffects()
        {
            Pawn provider = Pawn;
            HashSet<Pawn> desired = ImplantEffectUtility.CollectControlledMechsAndSelf(
                provider,
                IncludeMechanoidMechanitorSelf);
            desired.RemoveWhere(recipient => !CanApplyTo(recipient, recipient == provider));

            affectedPawns ??= new List<Pawn>();
            for (int i = affectedPawns.Count - 1; i >= 0; i--)
            {
                Pawn previous = affectedPawns[i];
                if (previous == null || !desired.Contains(previous))
                {
                    RemoveEffect(previous);
                    affectedPawns.RemoveAt(i);
                }
            }

            foreach (Pawn recipient in desired)
            {
                if (!ImplantEffectUtility.HasHediff(recipient, DistributedHediffDef))
                {
                    recipient.health.AddHediff(DistributedHediffDef);
                }

                if (!affectedPawns.Contains(recipient))
                {
                    affectedPawns.Add(recipient);
                }
            }
        }

        private void RemoveAllEffects()
        {
            if (affectedPawns == null)
            {
                return;
            }

            for (int i = affectedPawns.Count - 1; i >= 0; i--)
            {
                RemoveEffect(affectedPawns[i]);
            }

            affectedPawns.Clear();
        }

        private void RemoveEffect(Pawn? recipient)
        {
            if (recipient?.health?.hediffSet == null)
            {
                return;
            }

            Hediff? effect = recipient.health.hediffSet.GetFirstHediffOfDef(DistributedHediffDef);
            if (effect != null)
            {
                recipient.health.RemoveHediff(effect);
            }
        }
    }
}
