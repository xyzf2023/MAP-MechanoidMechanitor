using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 仿生受孕与出生；不调用原版 Hediff_Pregnant 或人类产程链。
    /// </summary>
    public static class SyntheticPregnancyUtility
    {
        private const string LogPrefix = "[MAP-机械族机械师] SyntheticPregnancy：";

        public static void TryConceiveAfterSuccessfulLovin(Pawn? spouse, Pawn? pregnantCompanion)
        {
            if (spouse == null
                || pregnantCompanion == null
                || spouse == pregnantCompanion
                || !SyntheticCompanionStateUtility.IsSyntheticCompanion(pregnantCompanion)
                || SyntheticCompanionStateUtility.IsSyntheticCompanion(spouse))
            {
                return;
            }

            if (!SyntheticCompanionStateUtility.TryGetState(pregnantCompanion, out ISyntheticCompanionState? state)
                || state == null
                || !state.LovinWithSpouseEnabled
                || pregnantCompanion.Dead
                || spouse.Dead
                || pregnantCompanion.relations == null
                || spouse.relations == null
                || SyntheticCompanionRelationshipUtility.IntimacyReason(spouse, pregnantCompanion) != null
                || pregnantCompanion.health?.hediffSet == null
                || HasPregnancyBlockingHediff(pregnantCompanion))
            {
                return;
            }

            float chance = GetConceptionChance(state.PregnancyApproach);
            if (chance <= 0f || !Rand.Chance(chance))
            {
                return;
            }

            PawnKindDef? childKind = spouse.kindDef;
            if (childKind?.RaceProps == null)
            {
                Log.Error($"{LogPrefix}受孕失败：伴侣 {spouse} 没有有效 PawnKindDef。");
                return;
            }

            bool inheritXenogenes =
                MAPMechanitorMod.Settings?.syntheticOffspringInheritXenogenes ?? false;
            List<GeneDef> endogenes = SnapshotGenes(spouse.genes?.Endogenes);
            List<GeneDef> xenogenes = inheritXenogenes
                ? SnapshotGenes(spouse.genes?.Xenogenes)
                : new List<GeneDef>();
            Gender? fixedGender = GetFixedOffspringGender(state.PregnancyApproach);

            Hediff_SyntheticPregnant pregnancy;
            try
            {
                pregnancy = (Hediff_SyntheticPregnant)HediffMaker.MakeHediff(
                    MAPMechanitor_HediffDefOf.MAP_SyntheticPregnant,
                    pregnantCompanion);
                pregnancy.Initialize(
                    spouse,
                    childKind,
                    endogenes,
                    xenogenes,
                    inheritXenogenes,
                    fixedGender);
                pregnantCompanion.health.AddHediff(pregnancy);
            }
            catch (Exception exception)
            {
                Log.Error($"{LogPrefix}为仿生伴侣 {pregnantCompanion} 添加自定义怀孕失败：{exception}");
                return;
            }

            Log.Message(
                $"{LogPrefix}{pregnantCompanion.LabelShort} 已受孕；遗传来源={spouse.LabelShort}，" +
                $"kind={childKind.defName}，固定性别={fixedGender?.ToString() ?? "随机"}，" +
                $"继承异种基因={inheritXenogenes}。");

            try
            {
                string name = pregnantCompanion.LabelShortCap;
                Find.LetterStack.ReceiveLetter(
                    "MAP_MechanoidMechanitor.Lover.Pregnancy.Conceived.Letter.Label"
                        .Translate(name),
                    "MAP_MechanoidMechanitor.Lover.Pregnancy.Conceived.Letter.Text"
                        .Translate(name),
                    LetterDefOf.PositiveEvent,
                    pregnantCompanion);
            }
            catch (Exception letterException)
            {
                Log.Warning(
                    $"{LogPrefix}发送受孕信件失败（怀孕仍有效）：{letterException}");
            }
        }

        public static Pawn? TryCompleteBirth(Hediff_SyntheticPregnant pregnancy)
        {
            Pawn? pregnantCompanion = pregnancy.pawn;
            PawnKindDef? kind = pregnancy.ChildKindDef;
            if (pregnantCompanion == null || pregnantCompanion.Dead)
            {
                return null;
            }

            if (kind?.RaceProps == null)
            {
                Log.Error(
                    $"{LogPrefix}生产失败：受孕时保存的 PawnKindDef " +
                    $"{kind?.defName ?? "<null>"} 无效。");
                return null;
            }

            List<GeneDef> endogenes = DistinctValid(pregnancy.EndogeneSnapshot);
            List<GeneDef> xenogenes = pregnancy.InheritXenogenes
                ? DistinctValid(pregnancy.XenogeneSnapshot)
                : new List<GeneDef>();
            xenogenes.RemoveAll(endogenes.Contains);
            string? lastName = TryGetLastName(pregnancy.GeneticParent);
            Pawn? geneticParent = pregnancy.GeneticParent;

            Pawn? child = null;
            bool birthCommitted = false;
            bool addedParentBirth = false;
            bool addedGeneticParent = false;
            try
            {
                PawnGenerationRequest request = new PawnGenerationRequest(
                    kind,
                    pregnantCompanion.Faction,
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
                if (child.health?.hediffSet == null)
                {
                    throw new InvalidOperationException("新生儿没有 健康追踪器。");
                }

                child.health.AddHediff(MAPMechanitor_HediffDefOf.MAP_ExtraordinaryOffspring);
                if (!child.health.hediffSet.HasHediff(
                    MAPMechanitor_HediffDefOf.MAP_ExtraordinaryOffspring))
                {
                    throw new InvalidOperationException(
                        "未能为新生儿添加或确认「超凡子嗣」健康状态。");
                }

                if (!TryAddBirthRelations(
                    child,
                    pregnantCompanion,
                    geneticParent,
                    out addedParentBirth,
                    out addedGeneticParent))
                {
                    throw new InvalidOperationException("未能建立必要亲属关系。");
                }

                bool spawned;
                try
                {
                    spawned = PawnUtility.TrySpawnHatchedOrBornPawn(child, pregnantCompanion);
                }
                catch (Exception spawnException)
                {
                    if (IsChildInGameWorld(child))
                    {
                        birthCommitted = true;
                        Log.Warning(
                            $"{LogPrefix}TrySpawnHatchedOrBornPawn 抛出异常，但新生儿已进入" +
                            $"实际环境，仍视为生产成功：{spawnException}");
                        spawned = true;
                    }
                    else
                    {
                        throw;
                    }
                }

                if (!spawned && IsChildInGameWorld(child))
                {
                    Log.Warning(
                        $"{LogPrefix}TrySpawnHatchedOrBornPawn 返回 false，但新生儿已进入" +
                        "实际环境，仍视为生产成功。");
                    spawned = true;
                }

                if (!spawned)
                {
                    Log.Error(
                        $"{LogPrefix}生产失败：无法将 {child} 放置到 {pregnantCompanion} 所在环境；" +
                        "将清理本次亲属关系并丢弃未落地新生儿，孕期保留以便重试。");
                    ClearAttemptedBirthRelations(
                        child,
                        pregnantCompanion,
                        geneticParent,
                        addedParentBirth,
                        addedGeneticParent);
                    DiscardUncommittedChild(child);
                    return null;
                }

                birthCommitted = true;

                try
                {
                    TaleRecorder.RecordTale(TaleDefOf.GaveBirth, pregnantCompanion, child);
                }
                catch (Exception taleException)
                {
                    Log.Warning(
                        $"{LogPrefix}记录 GaveBirth 故事失败（出生仍视为成功）：{taleException}");
                }

                Log.Message(
                    $"{LogPrefix}{pregnantCompanion.LabelShort} 成功产下 {child.LabelShort} " +
                    $"({kind.defName})。");

                return child;
            }
            catch (Exception exception)
            {
                if (child != null && (birthCommitted || IsChildInGameWorld(child)))
                {
                    Log.Error(
                        $"{LogPrefix}生产已提交后发生异常，仍返回新生儿且不会重试：" +
                        exception);
                    return child;
                }

                Log.Error($"{LogPrefix}自定义生产发生异常，孕期保留以便重试：{exception}");
                if (child != null)
                {
                    ClearAttemptedBirthRelations(
                        child,
                        pregnantCompanion,
                        geneticParent,
                        addedParentBirth,
                        addedGeneticParent);
                    DiscardUncommittedChild(child);
                }

                return null;
            }
        }

        private static bool HasPregnancyBlockingHediff(Pawn pregnantCompanion)
        {
            List<Hediff> hediffs = pregnantCompanion.health.hediffSet.hediffs;
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

        private static float GetConceptionChance(SyntheticPregnancyApproach approach)
        {
            return approach switch
            {
                SyntheticPregnancyApproach.AvoidPregnancy => 0f,
                SyntheticPregnancyApproach.TryForBaby => 1f,
                SyntheticPregnancyApproach.TryForBabyMale => 1f,
                SyntheticPregnancyApproach.TryForBabyFemale => 1f,
                _ => 0f,
            };
        }

        private static Gender? GetFixedOffspringGender(SyntheticPregnancyApproach approach)
        {
            return approach switch
            {
                SyntheticPregnancyApproach.TryForBabyMale => Gender.Male,
                SyntheticPregnancyApproach.TryForBabyFemale => Gender.Female,
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
                        $"{LogPrefix}新生儿种族 {child.kindDef?.defName} 没有 基因追踪器，" +
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

        private static bool TryAddBirthRelations(
            Pawn child,
            Pawn pregnantCompanion,
            Pawn? geneticParent,
            out bool addedParentBirth,
            out bool addedGeneticParent)
        {
            addedParentBirth = false;
            addedGeneticParent = false;

            if (child.relations == null)
            {
                Log.Error(
                    $"{LogPrefix}新生儿 {child} 没有 关系追踪器，无法建立必要亲属关系。");
                return false;
            }

            bool parentBirthExistedBefore =
                child.relations.DirectRelationExists(PawnRelationDefOf.ParentBirth, pregnantCompanion);
            bool geneticParentRequired = geneticParent != null && geneticParent != pregnantCompanion;
            bool geneticParentExistedBefore = geneticParentRequired
                && child.relations.DirectRelationExists(PawnRelationDefOf.Parent, geneticParent!);
            bool failed = false;

            if (!parentBirthExistedBefore)
            {
                try
                {
                    child.relations.AddDirectRelation(PawnRelationDefOf.ParentBirth, pregnantCompanion);
                }
                catch (Exception exception)
                {
                    failed = true;
                    Log.Error(
                        $"{LogPrefix}为新生儿 {child} 添加仿生伴侣 {pregnantCompanion} 的 ParentBirth " +
                        $"关系时发生异常：{exception}");
                }
            }

            bool parentBirthExistsNow =
                child.relations.DirectRelationExists(PawnRelationDefOf.ParentBirth, pregnantCompanion);
            addedParentBirth = !parentBirthExistedBefore && parentBirthExistsNow;
            if (!parentBirthExistsNow)
            {
                failed = true;
                Log.Error(
                    $"{LogPrefix}未能为新生儿 {child} 添加仿生伴侣 {pregnantCompanion} 的 ParentBirth 关系。");
            }

            if (geneticParentRequired)
            {
                if (!geneticParentExistedBefore)
                {
                    try
                    {
                        child.relations.AddDirectRelation(PawnRelationDefOf.Parent, geneticParent!);
                    }
                    catch (Exception exception)
                    {
                        failed = true;
                        Log.Error(
                            $"{LogPrefix}为新生儿 {child} 添加遗传伴侣 {geneticParent} " +
                            $"的 Parent 关系时发生异常：{exception}");
                    }
                }

                bool geneticParentExistsNow =
                    child.relations.DirectRelationExists(PawnRelationDefOf.Parent, geneticParent!);
                addedGeneticParent = !geneticParentExistedBefore && geneticParentExistsNow;
                if (!geneticParentExistsNow)
                {
                    failed = true;
                    Log.Error(
                        $"{LogPrefix}未能为新生儿 {child} 添加遗传伴侣 {geneticParent} " +
                        "的 Parent 关系。");
                }
            }

            return !failed;
        }

        private static void ClearAttemptedBirthRelations(
            Pawn child,
            Pawn pregnantCompanion,
            Pawn? geneticParent,
            bool addedParentBirth,
            bool addedGeneticParent)
        {
            if (child.relations == null)
            {
                return;
            }

            if (addedGeneticParent && geneticParent != null)
            {
                try
                {
                    if (child.relations.DirectRelationExists(
                        PawnRelationDefOf.Parent,
                        geneticParent))
                    {
                        bool removed = child.relations.TryRemoveDirectRelation(
                            PawnRelationDefOf.Parent,
                            geneticParent);
                        if (!removed)
                        {
                            Log.Error(
                                $"{LogPrefix}清理本次添加的遗传伴侣 Parent 关系失败：" +
                                $"child={child}，geneticParent={geneticParent}。");
                        }
                    }
                }
                catch (Exception exception)
                {
                    Log.Error(
                        $"{LogPrefix}清理本次添加的遗传伴侣 Parent 关系时发生异常：" +
                        exception);
                }
            }

            if (addedParentBirth)
            {
                try
                {
                    if (child.relations.DirectRelationExists(
                        PawnRelationDefOf.ParentBirth,
                        pregnantCompanion))
                    {
                        bool removed = child.relations.TryRemoveDirectRelation(
                            PawnRelationDefOf.ParentBirth,
                            pregnantCompanion);
                        if (!removed)
                        {
                            Log.Error(
                                $"{LogPrefix}清理本次添加的仿生伴侣 ParentBirth 关系失败：" +
                                $"child={child}，pregnantCompanion={pregnantCompanion}。");
                        }
                    }
                }
                catch (Exception exception)
                {
                    Log.Error(
                        $"{LogPrefix}清理本次添加的仿生伴侣 ParentBirth 关系时发生异常：" +
                        exception);
                }
            }
        }

        private static bool IsChildInGameWorld(Pawn child)
        {
            return child.Spawned
                || child.SpawnedOrAnyParentSpawned
                || child.IsCaravanMember()
                || child.IsWorldPawn();
        }

        private static void DiscardUncommittedChild(Pawn child)
        {
            if (IsChildInGameWorld(child))
            {
                Log.Warning(
                    $"{LogPrefix}拒绝丢弃已进入实际环境的新生儿 {child}。");
                return;
            }

            try
            {
                Find.WorldPawns.PassToWorld(child, PawnDiscardDecideMode.Discard);
            }
            catch (Exception exception)
            {
                Log.Error($"{LogPrefix}丢弃未落地新生儿失败：{exception}");
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
