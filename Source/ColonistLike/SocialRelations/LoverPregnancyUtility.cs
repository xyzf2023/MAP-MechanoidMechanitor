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
        /// 新生儿一旦成功进入实际环境，即视为出生已提交，此后不得再返回 null。
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
            Pawn? geneticParent = pregnancy.GeneticParent;

            Pawn? child = null;
            bool birthCommitted = false;
            bool addedParentBirth = false;
            bool addedGeneticParent = false;
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
                if (child.health?.hediffSet == null)
                {
                    throw new InvalidOperationException("新生儿没有 health tracker。");
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
                    lover,
                    geneticParent,
                    out addedParentBirth,
                    out addedGeneticParent))
                {
                    throw new InvalidOperationException("未能建立必要亲属关系。");
                }

                bool spawned;
                try
                {
                    spawned = PawnUtility.TrySpawnHatchedOrBornPawn(child, lover);
                }
                catch (Exception spawnException)
                {
                    // 放置过程抛异常，但新生儿可能已进入实际环境——此时必须提交。
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
                        $"{LogPrefix}生产失败：无法将 {child} 放置到 {lover} 所在环境；" +
                        "将清理本次亲属关系并丢弃未落地新生儿，孕期保留以便重试。");
                    ClearAttemptedBirthRelations(
                        child,
                        lover,
                        geneticParent,
                        addedParentBirth,
                        addedGeneticParent);
                    DiscardUncommittedChild(child);
                    return null;
                }

                // 从此刻起出生已提交：后续任何非关键异常都不得返回 null。
                birthCommitted = true;

                try
                {
                    TaleRecorder.RecordTale(TaleDefOf.GaveBirth, lover, child);
                }
                catch (Exception taleException)
                {
                    Log.Warning(
                        $"{LogPrefix}记录 GaveBirth 故事失败（出生仍视为成功）：{taleException}");
                }

                try
                {
                    Log.Message(
                        $"{LogPrefix}{lover.LabelShort} 成功产下 {child.LabelShort} " +
                        $"({kind.defName})。");
                }
                catch (Exception logException)
                {
                    Log.Warning(
                        $"{LogPrefix}记录成功生产日志失败（出生仍视为成功）：{logException}");
                }

                return child;
            }
            catch (Exception exception)
            {
                // 已提交或已实际存在于游戏环境：必须返回新生儿，绝不可丢弃或保留孕期重产。
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
                        lover,
                        geneticParent,
                        addedParentBirth,
                        addedGeneticParent);
                    DiscardUncommittedChild(child);
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

        private static bool TryAddBirthRelations(
            Pawn child,
            Pawn lover,
            Pawn? geneticParent,
            out bool addedParentBirth,
            out bool addedGeneticParent)
        {
            addedParentBirth = false;
            addedGeneticParent = false;

            if (child.relations == null)
            {
                Log.Error(
                    $"{LogPrefix}新生儿 {child} 没有 relations tracker，无法建立必要亲属关系。");
                return false;
            }

            bool parentBirthExistedBefore =
                child.relations.DirectRelationExists(PawnRelationDefOf.ParentBirth, lover);
            bool geneticParentRequired = geneticParent != null && geneticParent != lover;
            bool geneticParentExistedBefore = geneticParentRequired
                && child.relations.DirectRelationExists(PawnRelationDefOf.Parent, geneticParent!);
            bool failed = false;

            // 恋人仅作生母（ParentBirth），不作普通 Parent，避免同时显示“母亲”和“生母”。
            if (!parentBirthExistedBefore)
            {
                try
                {
                    child.relations.AddDirectRelation(PawnRelationDefOf.ParentBirth, lover);
                }
                catch (Exception exception)
                {
                    failed = true;
                    Log.Error(
                        $"{LogPrefix}为新生儿 {child} 添加恋人 {lover} 的 ParentBirth " +
                        $"关系时发生异常：{exception}");
                }
            }

            bool parentBirthExistsNow =
                child.relations.DirectRelationExists(PawnRelationDefOf.ParentBirth, lover);
            addedParentBirth = !parentBirthExistedBefore && parentBirthExistsNow;
            if (!parentBirthExistsNow)
            {
                failed = true;
                Log.Error(
                    $"{LogPrefix}未能为新生儿 {child} 添加恋人 {lover} 的 ParentBirth 关系。");
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
                            $"{LogPrefix}为新生儿 {child} 添加遗传配偶 {geneticParent} " +
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
                        $"{LogPrefix}未能为新生儿 {child} 添加遗传配偶 {geneticParent} " +
                        "的 Parent 关系。");
                }
            }

            return !failed;
        }

        /// <summary>
        /// 仅清理本次尝试实际添加的关系，不触碰恋人或遗传配偶原有的其他亲属关系。
        /// 使用原版 TryRemoveDirectRelation，由其维护双向索引。
        /// </summary>
        private static void ClearAttemptedBirthRelations(
            Pawn child,
            Pawn lover,
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
                                $"{LogPrefix}清理本次添加的遗传配偶 Parent 关系失败：" +
                                $"child={child}，geneticParent={geneticParent}。");
                        }
                    }
                }
                catch (Exception exception)
                {
                    Log.Error(
                        $"{LogPrefix}清理本次添加的遗传配偶 Parent 关系时发生异常：" +
                        exception);
                }
            }

            if (addedParentBirth)
            {
                try
                {
                    if (child.relations.DirectRelationExists(
                        PawnRelationDefOf.ParentBirth,
                        lover))
                    {
                        bool removed = child.relations.TryRemoveDirectRelation(
                            PawnRelationDefOf.ParentBirth,
                            lover);
                        if (!removed)
                        {
                            Log.Error(
                                $"{LogPrefix}清理本次添加的恋人 ParentBirth 关系失败：" +
                                $"child={child}，lover={lover}。");
                        }
                    }
                }
                catch (Exception exception)
                {
                    Log.Error(
                        $"{LogPrefix}清理本次添加的恋人 ParentBirth 关系时发生异常：" +
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
