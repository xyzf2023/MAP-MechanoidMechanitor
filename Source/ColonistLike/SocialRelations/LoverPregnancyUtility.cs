using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 恋人专用受孕与出生；不调用原版 Hediff_Pregnant 或人类产程链。
    /// </summary>
    public static class LoverPregnancyUtility
    {
        private const string LogPrefix = "[MAP-机械族机械师] LoverPregnancy：";

        public static void TryConceiveAfterSuccessfulLovin(Pawn? spouse, Pawn? lover)
        {
            if (spouse == null
                || lover == null
                || spouse == lover
                || ExplicitSocialRelationUtility.IsOptedIn(spouse)
                || !ExplicitSocialRelationUtility.IsOptedIn(lover))
            {
                return;
            }

            CompExplicitSocialRelationUser? comp =
                lover.GetComp<CompExplicitSocialRelationUser>();
            if (comp == null
                || !comp.LovinWithSpouseEnabled
                || lover.Dead
                || spouse.Dead
                || lover.relations == null
                || spouse.relations == null
                || !lover.relations.DirectRelationExists(PawnRelationDefOf.Spouse, spouse)
                || !spouse.relations.DirectRelationExists(PawnRelationDefOf.Spouse, lover)
                || lover.health?.hediffSet == null
                || HasPregnancyBlockingHediff(lover))
            {
                return;
            }

            float chance = GetConceptionChance(comp.PregnancyApproach);
            if (chance <= 0f || !Rand.Chance(chance))
            {
                return;
            }

            PawnKindDef? childKind = spouse.kindDef;
            if (childKind?.RaceProps == null)
            {
                Log.Error($"{LogPrefix}受孕失败：配偶 {spouse} 没有有效 PawnKindDef。");
                return;
            }

            bool inheritXenogenes =
                MAPMechanitorMod.Settings?.loverOffspringInheritXenogenes ?? false;
            List<GeneDef> endogenes = SnapshotGenes(spouse.genes?.Endogenes);
            List<GeneDef> xenogenes = inheritXenogenes
                ? SnapshotGenes(spouse.genes?.Xenogenes)
                : new List<GeneDef>();
            Gender? fixedGender = GetFixedOffspringGender(comp.PregnancyApproach);

            Hediff_LoverPregnant pregnancy;
            try
            {
                pregnancy = (Hediff_LoverPregnant)HediffMaker.MakeHediff(
                    MAPMechanitor_HediffDefOf.MAP_LoverPregnant,
                    lover);
                pregnancy.Initialize(
                    spouse,
                    childKind,
                    endogenes,
                    xenogenes,
                    inheritXenogenes,
                    fixedGender);
                lover.health.AddHediff(pregnancy);
            }
            catch (Exception exception)
            {
                Log.Error($"{LogPrefix}为恋人 {lover} 添加自定义怀孕失败：{exception}");
                return;
            }

            Log.Message(
                $"{LogPrefix}{lover.LabelShort} 已受孕；遗传来源={spouse.LabelShort}，" +
                $"kind={childKind.defName}，固定性别={fixedGender?.ToString() ?? "随机"}，" +
                $"继承异种基因={inheritXenogenes}。");
        }

        /// <summary>
        /// 尝试完成自定义生产。成功返回已生成并放置的新生儿；失败返回 null，孕期保留以便重试。
        /// 调用方在成功后负责移除怀孕 Hediff。
        /// </summary>
        public static Pawn? TryCompleteBirth(Hediff_LoverPregnant pregnancy)
        {
            Pawn? lover = pregnancy.pawn;
            PawnKindDef? kind = pregnancy.ChildKindDef;
            if (lover == null || lover.Dead)
            {
                return null;
            }

            if (kind?.RaceProps?.lifeStageAges == null
                || !kind.RaceProps.lifeStageAges.Any(
                    stage => stage.def.developmentalStage.Newborn()))
            {
                Log.Error(
                    $"{LogPrefix}生产失败：受孕时保存的 PawnKindDef " +
                    $"{kind?.defName ?? "<null>"} 不支持 Newborn 阶段。");
                return null;
            }

            List<GeneDef> endogenes = DistinctValid(pregnancy.EndogeneSnapshot);
            List<GeneDef> xenogenes = pregnancy.InheritXenogenes
                ? DistinctValid(pregnancy.XenogeneSnapshot)
                : new List<GeneDef>();
            xenogenes.RemoveAll(endogenes.Contains);
            string? lastName = TryGetLastName(pregnancy.GeneticParent);

            Pawn? child = null;
            try
            {
                PawnGenerationRequest request = new PawnGenerationRequest(
                    kind,
                    lover.Faction,
                    PawnGenerationContext.NonPlayer,
                    forceGenerateNewPawn: false,
                    allowDead: false,
                    allowDowned: true,
                    canGeneratePawnRelations: false,
                    allowPregnant: false,
                    fixedGender: pregnancy.FixedGender,
                    fixedLastName: lastName,
                    forceNoIdeo: true,
                    forcedXenogenes: xenogenes,
                    forcedEndogenes: endogenes,
                    forcedXenotype: XenotypeDefOf.Baseliner,
                    developmentalStages: DevelopmentalStage.Newborn,
                    forceNoGear: true);
                request.DontGivePreArrivalPathway = true;
                child = PawnGenerator.GeneratePawn(request);
                if (child == null || child.kindDef != kind)
                {
                    throw new InvalidOperationException(
                        $"PawnGenerator 返回了错误 kind：{child?.kindDef?.defName ?? "<null>"}");
                }

                EnforceExactGenes(child, endogenes, xenogenes);
                if (child.health == null)
                {
                    throw new InvalidOperationException("新生儿没有 health tracker。");
                }

                // 亲属与超凡子嗣属于出生后附加；即使失败也不回滚已生成的新生儿放置。
                AddBirthRelations(child, lover, pregnancy.GeneticParent);
                try
                {
                    child.health.AddHediff(MAPMechanitor_HediffDefOf.MAP_ExtraordinaryOffspring);
                }
                catch (Exception hediffException)
                {
                    Log.Warning(
                        $"{LogPrefix}为新生儿添加超凡子嗣健康状态失败（出生仍视为成功）：" +
                        hediffException);
                }

                if (!PawnUtility.TrySpawnHatchedOrBornPawn(child, lover))
                {
                    Log.Error(
                        $"{LogPrefix}生产失败：无法将 {child} 放置到 {lover} 所在环境；" +
                        "该 Pawn 将被安全丢弃，孕期保留以便重试。");
                    Find.WorldPawns.PassToWorld(child, PawnDiscardDecideMode.Discard);
                    return null;
                }

                try
                {
                    TaleRecorder.RecordTale(TaleDefOf.GaveBirth, lover, child);
                }
                catch (Exception taleException)
                {
                    Log.Warning(
                        $"{LogPrefix}记录 GaveBirth 故事失败（出生仍视为成功）：{taleException}");
                }

                Log.Message(
                    $"{LogPrefix}{lover.LabelShort} 成功产下 {child.LabelShort} " +
                    $"({kind.defName})。");
                return child;
            }
            catch (Exception exception)
            {
                Log.Error($"{LogPrefix}自定义生产发生异常，孕期保留以便重试：{exception}");
                if (child != null && !child.Spawned && !child.IsCaravanMember())
                {
                    Find.WorldPawns.PassToWorld(child, PawnDiscardDecideMode.Discard);
                }

                return null;
            }
        }

        private static bool HasPregnancyBlockingHediff(Pawn lover)
        {
            List<Hediff> hediffs = lover.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                HediffDef? def = hediffs[i]?.def;
                if (def != null && (def.pregnant || def.preventsPregnancy))
                {
                    return true;
                }
            }

            return false;
        }

        private static float GetConceptionChance(LoverPregnancyApproach approach)
        {
            return approach switch
            {
                LoverPregnancyApproach.Normal => 0.05f,
                LoverPregnancyApproach.AvoidPregnancy => 0f,
                LoverPregnancyApproach.TryForBaby => 1f,
                LoverPregnancyApproach.TryForBabyMale => 1f,
                LoverPregnancyApproach.TryForBabyFemale => 1f,
                _ => 0f,
            };
        }

        private static Gender? GetFixedOffspringGender(LoverPregnancyApproach approach)
        {
            return approach switch
            {
                LoverPregnancyApproach.TryForBabyMale => Gender.Male,
                LoverPregnancyApproach.TryForBabyFemale => Gender.Female,
                _ => null,
            };
        }

        private static List<GeneDef> SnapshotGenes(List<Gene>? genes)
        {
            if (genes == null)
            {
                return new List<GeneDef>();
            }

            return genes
                .Where(gene => gene?.def != null)
                .Select(gene => gene.def)
                .Distinct()
                .ToList();
        }

        private static List<GeneDef> DistinctValid(IEnumerable<GeneDef> genes)
        {
            return genes.Where(gene => gene != null).Distinct().ToList();
        }

        private static void EnforceExactGenes(
            Pawn child,
            List<GeneDef> endogenes,
            List<GeneDef> xenogenes)
        {
            if (child.genes == null)
            {
                if (endogenes.Count > 0 || xenogenes.Count > 0)
                {
                    Log.Warning(
                        $"{LogPrefix}新生儿种族 {child.kindDef?.defName} 没有 genes tracker，" +
                        "无法应用已保存的基因快照。");
                }

                return;
            }

            List<Gene> existing = child.genes.GenesListForReading.ToList();
            for (int i = existing.Count - 1; i >= 0; i--)
            {
                child.genes.RemoveGene(existing[i]);
            }

            for (int i = 0; i < endogenes.Count; i++)
            {
                child.genes.AddGene(endogenes[i], xenogene: false);
            }

            for (int i = 0; i < xenogenes.Count; i++)
            {
                child.genes.AddGene(xenogenes[i], xenogene: true);
            }
        }

        private static void AddBirthRelations(Pawn child, Pawn lover, Pawn? geneticParent)
        {
            if (child.relations == null)
            {
                Log.Warning($"{LogPrefix}新生儿 {child} 没有 relations tracker，无法建立亲属关系。");
                return;
            }

            try
            {
                // 恋人仅作生母（ParentBirth），不作普通 Parent，避免同时显示“母亲”和“生母”。
                child.relations.AddDirectRelation(PawnRelationDefOf.ParentBirth, lover);
                if (geneticParent != null && geneticParent != lover)
                {
                    child.relations.AddDirectRelation(PawnRelationDefOf.Parent, geneticParent);
                }
            }
            catch (Exception exception)
            {
                Log.Error(
                    $"{LogPrefix}为新生儿 {child} 建立亲属关系失败；出生仍继续：{exception}");
            }
        }

        private static string? TryGetLastName(Pawn? pawn)
        {
            if (pawn?.Name is NameTriple name && !name.Last.NullOrEmpty())
            {
                return name.Last.Trim();
            }

            return null;
        }
    }
}
