using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 仿生孕期。所有胎儿来源在受孕时快照，不依赖生产时的当前配偶。
    /// 达到 100% 后不再自动生产，改由玩家通过「开始分娩」指令主动触发产程。
    /// </summary>
    public sealed class Hediff_SyntheticPregnant : HediffWithComps
    {
        private const float TicksPerDay = 60000f;
        private const string LogPrefix = "[MAP-机械族机械师] SyntheticPregnancy：";

        private static Texture2D? cachedBirthIcon;

        private Pawn? geneticParent;
        private PawnKindDef? childKindDef;
        private List<GeneDef>? endogeneSnapshot;
        private List<GeneDef>? xenogeneSnapshot;
        private bool inheritXenogenes;
        private int fixedGender = -1;
        private bool readyForBirthLetterSent;

        public Pawn? GeneticParent => geneticParent;
        public PawnKindDef? ChildKindDef => childKindDef;
        public IReadOnlyList<GeneDef> EndogeneSnapshot =>
            endogeneSnapshot ?? (IReadOnlyList<GeneDef>)Array.Empty<GeneDef>();
        public IReadOnlyList<GeneDef> XenogeneSnapshot =>
            xenogeneSnapshot ?? (IReadOnlyList<GeneDef>)Array.Empty<GeneDef>();
        public bool InheritXenogenes => inheritXenogenes;
        public Gender? FixedGender => fixedGender >= 0 ? (Gender?)fixedGender : null;

        public bool ReadyForBirth => Severity >= 1f;

        private static Texture2D BirthIcon =>
            cachedBirthIcon ??= ContentFinder<Texture2D>.Get("UI/Icons/Rituals/GiveBirth");

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
            readyForBirthLetterSent = false;
            Severity = 0.001f;
        }

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (pawn == null || pawn.Dead)
            {
                return;
            }

            if (Severity < 1f)
            {
                float gestationDays =
                    def.GetModExtension<HediffDefExtension_SyntheticPregnancy>()
                        ?.ResolveGestationDays()
                    ?? HediffDefExtension_SyntheticPregnancy.DefaultGestationDays;
                Severity += delta / (gestationDays * TicksPerDay);
                if (Severity > 1f)
                {
                    Severity = 1f;
                }
            }
            else if (Severity > 1f)
            {
                Severity = 1f;
            }

            TrySendReadyForBirthLetter();
        }

        private void TrySendReadyForBirthLetter()
        {
            if (readyForBirthLetterSent || Severity < 1f || pawn == null)
            {
                return;
            }

            readyForBirthLetterSent = true;
            try
            {
                string name = pawn.LabelShortCap;
                Find.LetterStack.ReceiveLetter(
                    "MAP_MechanoidMechanitor.SyntheticPregnancy.ReadyForBirthLetterLabel"
                        .Translate(name),
                    "MAP_MechanoidMechanitor.SyntheticPregnancy.ReadyForBirthLetterText"
                        .Translate(name),
                    LetterDefOf.PositiveEvent,
                    pawn);
            }
            catch (Exception exception)
            {
                Log.Warning(
                    $"{LogPrefix}发送足月分娩信件失败（孕期与冻结状态不受影响）：{exception}");
            }
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            if (!ReadyForBirth
                || pawn == null
                || pawn.Faction != Faction.OfPlayer)
            {
                yield break;
            }

            Command_Action command = new Command_Action
            {
                defaultLabel = "开始分娩",
                defaultDesc =
                    "MAP_MechanoidMechanitor.SyntheticPregnancy.StartBirthGizmoDesc"
                        .Translate(pawn.LabelShortCap),
                icon = BirthIcon,
                action = StartBirthJob,
            };

            string? disabledReason = GetBirthDisabledReason();
            if (disabledReason != null)
            {
                command.Disable(disabledReason);
            }

            yield return command;
        }

        private string? GetBirthDisabledReason()
        {
            if (pawn == null)
            {
                return "授权机械体无效。";
            }

            if (!pawn.Spawned)
            {
                return "授权机械体当前不在地图上。";
            }

            if (pawn.Dead)
            {
                return "授权机械体已死亡。";
            }

            if (pawn.Downed)
            {
                return "授权机械体已倒地。";
            }

            if (pawn.Drafted)
            {
                return "授权机械体已被征召。";
            }

            if (pawn.InMentalState)
            {
                return "授权机械体正处于精神状态。";
            }

            if (pawn.IsBurning())
            {
                return "授权机械体正在燃烧。";
            }

            if (pawn.jobs == null)
            {
                return "授权机械体无法执行工作。";
            }

            if (pawn.CurJobDef == MAPMechanitor_JobDefOf.MAP_SyntheticGiveBirth)
            {
                return "授权机械体正在分娩。";
            }

            return null;
        }

        private void StartBirthJob()
        {
            if (pawn?.jobs == null || GetBirthDisabledReason() != null)
            {
                return;
            }

            Job job = JobMaker.MakeJob(MAPMechanitor_JobDefOf.MAP_SyntheticGiveBirth);
            job.count = Rand.Range(1250, 2501);
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
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
            Scribe_Values.Look(ref readyForBirthLetterSent, "readyForBirthLetterSent", false);

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
