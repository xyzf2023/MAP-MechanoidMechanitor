using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 恋人专用孕期。所有胎儿来源在受孕时快照，不依赖生产时的当前配偶。
    /// 达到 100% 后不再自动生产，改由玩家通过「开始分娩」指令主动触发产程。
    /// </summary>
    public sealed class Hediff_LoverPregnant : HediffWithComps
    {
        private const float TicksPerDay = 60000f;

        private static Texture2D? cachedBirthIcon;

        private Pawn? geneticParent;
        private PawnKindDef? childKindDef;
        private List<GeneDef>? endogeneSnapshot;
        private List<GeneDef>? xenogeneSnapshot;
        private bool inheritXenogenes;
        private int fixedGender = -1;

        public Pawn? GeneticParent => geneticParent;
        public PawnKindDef? ChildKindDef => childKindDef;
        public IReadOnlyList<GeneDef> EndogeneSnapshot =>
            endogeneSnapshot ?? (IReadOnlyList<GeneDef>)System.Array.Empty<GeneDef>();
        public IReadOnlyList<GeneDef> XenogeneSnapshot =>
            xenogeneSnapshot ?? (IReadOnlyList<GeneDef>)System.Array.Empty<GeneDef>();
        public bool InheritXenogenes => inheritXenogenes;
        public Gender? FixedGender => fixedGender >= 0 ? (Gender?)fixedGender : null;

        /// <summary>
        /// 孕期是否已足月（100%）。足月后不再自动生产，仅等待玩家下达分娩指令。
        /// </summary>
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
            Severity = 0.001f;
        }

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (pawn == null || pawn.Dead)
            {
                return;
            }

            // 已足月：封顶在 100% 并保持，不自动生产、不自动消失。
            if (Severity >= 1f)
            {
                if (Severity > 1f)
                {
                    Severity = 1f;
                }

                return;
            }

            float gestationDays =
                def.GetModExtension<HediffDefExtension_LoverPregnancy>()
                    ?.ResolveGestationDays()
                ?? HediffDefExtension_LoverPregnancy.DefaultGestationDays;
            Severity += delta / (gestationDays * TicksPerDay);
            if (Severity > 1f)
            {
                Severity = 1f;
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
                    "命令恋人执行分娩流程。请在确保恋人处于安全环境时下达此命令。",
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
                return "恋人无效。";
            }

            if (!pawn.Spawned)
            {
                return "恋人当前不在地图上。";
            }

            if (pawn.Dead)
            {
                return "恋人已死亡。";
            }

            if (pawn.Downed)
            {
                return "恋人已倒地。";
            }

            if (pawn.Drafted)
            {
                return "恋人已被征召。";
            }

            if (pawn.InMentalState)
            {
                return "恋人正处于精神状态。";
            }

            if (pawn.IsBurning())
            {
                return "恋人正在燃烧。";
            }

            if (pawn.jobs == null)
            {
                return "恋人无法执行工作。";
            }

            if (pawn.CurJobDef == MAPMechanitor_JobDefOf.MAP_LoverGiveBirth)
            {
                return "恋人正在分娩。";
            }

            return null;
        }

        private void StartBirthJob()
        {
            if (pawn?.jobs == null || GetBirthDisabledReason() != null)
            {
                return;
            }

            Job job = JobMaker.MakeJob(MAPMechanitor_JobDefOf.MAP_LoverGiveBirth);
            // 产程时长在下达指令时随机决定一次，之后随 Job 存档。
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
