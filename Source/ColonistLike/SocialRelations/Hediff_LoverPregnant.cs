using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 恋人专用孕期。所有胎儿来源在受孕时快照，不依赖生产时的当前配偶。
    /// </summary>
    public sealed class Hediff_LoverPregnant : HediffWithComps
    {
        private const float TicksPerDay = 60000f;

        private Pawn? geneticParent;
        private PawnKindDef? childKindDef;
        private List<GeneDef>? endogeneSnapshot;
        private List<GeneDef>? xenogeneSnapshot;
        private bool inheritXenogenes;
        private int fixedGender = -1;
        private bool birthAttempted;

        public Pawn? GeneticParent => geneticParent;
        public PawnKindDef? ChildKindDef => childKindDef;
        public IReadOnlyList<GeneDef> EndogeneSnapshot =>
            endogeneSnapshot ?? (IReadOnlyList<GeneDef>)System.Array.Empty<GeneDef>();
        public IReadOnlyList<GeneDef> XenogeneSnapshot =>
            xenogeneSnapshot ?? (IReadOnlyList<GeneDef>)System.Array.Empty<GeneDef>();
        public bool InheritXenogenes => inheritXenogenes;
        public Gender? FixedGender => fixedGender >= 0 ? (Gender?)fixedGender : null;

        public void Initialize(
            Pawn geneticParentPawn,
            PawnKindDef offspringKind,
            List<GeneDef> endogenes,
            List<GeneDef> xenogenes,
            bool inheritXenogeneSnapshot,
            Gender? offspringGender)
        {
            geneticParent = geneticParentPawn;
            childKindDef = offspringKind;
            endogeneSnapshot = endogenes;
            xenogeneSnapshot = xenogenes;
            inheritXenogenes = inheritXenogeneSnapshot;
            fixedGender = offspringGender.HasValue ? (int)offspringGender.Value : -1;
            birthAttempted = false;
            Severity = 0.001f;
        }

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (birthAttempted || pawn == null || pawn.Dead)
            {
                return;
            }

            float gestationDays =
                def.GetModExtension<HediffDefExtension_LoverPregnancy>()
                    ?.ResolveGestationDays()
                ?? HediffDefExtension_LoverPregnancy.DefaultGestationDays;
            Severity += delta / (gestationDays * TicksPerDay);
            if (Severity < 1f)
            {
                return;
            }

            birthAttempted = true;
            try
            {
                LoverPregnancyUtility.TryCompleteBirth(this);
            }
            finally
            {
                // 无论出生成功、失败还是异常，都必须移除，避免 birthAttempted 卡死孕期。
                pawn.health?.RemoveHediff(this);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref geneticParent, "geneticParent");
            Scribe_Defs.Look(ref childKindDef, "childKindDef");
            Scribe_Collections.Look(ref endogeneSnapshot, "endogeneSnapshot", LookMode.Def);
            Scribe_Collections.Look(ref xenogeneSnapshot, "xenogeneSnapshot", LookMode.Def);
            Scribe_Values.Look(ref inheritXenogenes, "inheritXenogenes", false);
            Scribe_Values.Look(ref fixedGender, "fixedGender", -1);
            Scribe_Values.Look(ref birthAttempted, "birthAttempted", false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                endogeneSnapshot ??= new List<GeneDef>();
                xenogeneSnapshot ??= new List<GeneDef>();
                endogeneSnapshot.RemoveAll(gene => gene == null);
                xenogeneSnapshot.RemoveAll(gene => gene == null);
            }
        }
    }
}
