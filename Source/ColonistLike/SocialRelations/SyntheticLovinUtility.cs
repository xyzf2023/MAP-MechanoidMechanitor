using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 远程仿生伴侣配偶 Lovin 候选查询。不扫描地图，只遍历发起者 DirectRelations。
    /// </summary>
    public static class SyntheticLovinUtility
    {
        /// <summary>
        /// 授权机械体正在床上执行 JobDefOf.Lovin 时，使用与 Humanlike 相同的床上绘制分支。
        /// </summary>
        public static bool ShouldUseHumanlikeBedLovinRender(Pawn? pawn, bool isPortrait = false)
        {
            return TryGetSyntheticLovinBed(pawn, isPortrait, out _);
        }

        public static bool ShouldUseHumanlikeBedLovinRender(Pawn? pawn, PawnRenderFlags flags)
        {
            return TryGetSyntheticLovinBed(
                pawn,
                flags.FlagSet(PawnRenderFlags.Portrait),
                out _);
        }

        /// <summary>
        /// 廉价淘汰优先：确认床上 Lovin 渲染条件，并一次性返回有效床位。不跨帧缓存。
        /// </summary>
        public static bool TryGetSyntheticLovinBed(
            Pawn? pawn,
            bool isPortrait,
            out Building_Bed? bed)
        {
            bed = null;
            if (pawn == null || isPortrait)
            {
                return false;
            }

            if (pawn.CurJobDef != JobDefOf.Lovin)
            {
                return false;
            }

            if (!pawn.GetPosture().InBed())
            {
                return false;
            }

            bed = pawn.CurrentBed();
            if (bed == null)
            {
                return false;
            }

            if (!SyntheticCompanionStateUtility.IsSyntheticCompanion(pawn))
            {
                bed = null;
                return false;
            }

            return true;
        }

        public const float LovinEffectiveAgeYearsForSyntheticCompanion = 18f;

        public static float EvaluateLovinAgeFlatHill(float ageYears)
        {
            return GenMath.FlatHill(0f, 14f, 16f, 25f, 80f, 0.2f, ageYears);
        }

        public static bool TryComputeSyntheticCompanionLovinMtbSinglePawnFactor(
            Pawn? pawn,
            out float result)
        {
            result = 0f;
            if (pawn == null || !SyntheticCompanionStateUtility.IsSyntheticCompanion(pawn))
            {
                return false;
            }

            if (pawn.health?.hediffSet == null || pawn.health.capacities == null)
            {
                return false;
            }

            float effectiveAgeFactor =
                EvaluateLovinAgeFlatHill(LovinEffectiveAgeYearsForSyntheticCompanion);
            if (effectiveAgeFactor <= 0f
                || float.IsNaN(effectiveAgeFactor)
                || float.IsInfinity(effectiveAgeFactor))
            {
                return false;
            }

            float num = 1f;
            num /= 1f - pawn.health.hediffSet.PainTotal;
            float consciousness =
                pawn.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness);
            if (consciousness < 0.5f)
            {
                num /= consciousness * 2f;
            }

            result = num / effectiveAgeFactor;
            return true;
        }

        public static bool IsRemoteHumanSpouseLovinJob(Pawn? actor, Pawn? partner, Building_Bed? bed)
        {
            if (actor == null || partner == null || bed == null || bed.Destroyed)
            {
                return false;
            }

            if (SyntheticCompanionStateUtility.IsSyntheticCompanion(actor)
                || !SyntheticCompanionStateUtility.IsSyntheticCompanion(partner))
            {
                return false;
            }

            if (actor.RaceProps == null || !actor.RaceProps.Humanlike)
            {
                return false;
            }

            return true;
        }

        public static bool IsRemoteSyntheticCompanionLovinJob(Pawn? actor, Pawn? partner, Building_Bed? bed)
        {
            if (actor == null || partner == null || bed == null || bed.Destroyed)
            {
                return false;
            }

            if (!SyntheticCompanionStateUtility.IsSyntheticCompanion(actor)
                || SyntheticCompanionStateUtility.IsSyntheticCompanion(partner))
            {
                return false;
            }

            if (partner.RaceProps == null || !partner.RaceProps.Humanlike)
            {
                return false;
            }

            return true;
        }

        public static bool IsFrequencyBoostRemoteEnabledSyntheticCompanion(
            Pawn? initiator,
            Pawn? partner)
        {
            if (initiator == null || partner == null || initiator == partner)
            {
                return false;
            }

            if (SyntheticCompanionStateUtility.IsSyntheticCompanion(initiator)
                || !SyntheticCompanionStateUtility.IsSyntheticCompanion(partner))
            {
                return false;
            }

            if (initiator.RaceProps == null || !initiator.RaceProps.Humanlike)
            {
                return false;
            }

            if (!SyntheticCompanionStateUtility.IsLovinWithSpouseEnabled(partner))
            {
                return false;
            }

            if (initiator.relations == null || partner.relations == null)
            {
                return false;
            }

            if (!initiator.relations.DirectRelationExists(PawnRelationDefOf.Spouse, partner)
                || !partner.relations.DirectRelationExists(PawnRelationDefOf.Spouse, initiator))
            {
                return false;
            }

            return true;
        }

        public static bool IsBoundHumanSyntheticCompanionSpouseLovinDriver(JobDriver? driver)
        {
            if (driver?.pawn == null || driver.job == null || driver.job.def != JobDefOf.Lovin)
            {
                return false;
            }

            if (!TryGetLovinPartnerAndBed(driver.job, out Pawn? partner, out Building_Bed? bed)
                || partner == null
                || bed == null)
            {
                return false;
            }

            Pawn actor = driver.pawn;
            Pawn? human;
            Pawn? syntheticCompanion;
            if (IsRemoteSyntheticCompanionLovinJob(actor, partner, bed))
            {
                syntheticCompanion = actor;
                human = partner;
            }
            else if (IsRemoteHumanSpouseLovinJob(actor, partner, bed))
            {
                human = actor;
                syntheticCompanion = partner;
            }
            else
            {
                return false;
            }

            if (human.relations == null || syntheticCompanion.relations == null)
            {
                return false;
            }

            return human.relations.DirectRelationExists(PawnRelationDefOf.Spouse, syntheticCompanion)
                && syntheticCompanion.relations.DirectRelationExists(PawnRelationDefOf.Spouse, human);
        }

        public static bool TryGetLovinPartnerAndBed(
            Job? job,
            out Pawn? partner,
            out Building_Bed? bed)
        {
            partner = null;
            bed = null;
            if (job == null || job.def != JobDefOf.Lovin)
            {
                return false;
            }

            partner = job.GetTarget(TargetIndex.A).Thing as Pawn;
            bed = job.GetTarget(TargetIndex.B).Thing as Building_Bed;
            return partner != null && bed != null;
        }

        public static bool IsRemoteSyntheticCompanionStillBoundForHumanWait(
            Pawn humanSpouse,
            Pawn? syntheticCompanion,
            Building_Bed bed)
        {
            if (!IsLivingSpawnedPawn(syntheticCompanion) || syntheticCompanion!.Map != humanSpouse.Map)
            {
                return false;
            }

            if (bed.Destroyed || !bed.Spawned)
            {
                return false;
            }

            if (syntheticCompanion.CurJobDef != JobDefOf.Lovin || syntheticCompanion.CurJob == null)
            {
                return false;
            }

            Job companionJob = syntheticCompanion.CurJob;
            if (companionJob.GetTarget(TargetIndex.A).Thing != humanSpouse)
            {
                return false;
            }

            return companionJob.GetTarget(TargetIndex.B).Thing == bed;
        }

        public static bool IsRemoteSyntheticCompanionPhysicallyReadyInBed(
            Pawn humanSpouse,
            Pawn? syntheticCompanion,
            Building_Bed bed)
        {
            if (!IsRemoteSyntheticCompanionStillBoundForHumanWait(humanSpouse, syntheticCompanion, bed))
            {
                return false;
            }

            if (!syntheticCompanion!.GetPosture().InBed())
            {
                return false;
            }

            return syntheticCompanion.CurrentBed() == bed;
        }

        public static bool ShouldFailRemoteSyntheticCompanionLovin(
            Pawn syntheticCompanion,
            Pawn? humanSpouse,
            Building_Bed? bed)
        {
            return !IsRemoteHumanStillBoundForSyntheticCompanion(syntheticCompanion, humanSpouse, bed);
        }

        public static bool IsRemoteHumanStillBoundForSyntheticCompanion(
            Pawn syntheticCompanion,
            Pawn? humanSpouse,
            Building_Bed? bed)
        {
            if (!IsLivingSpawnedPawn(humanSpouse) || humanSpouse!.Map != syntheticCompanion.Map)
            {
                return false;
            }

            if (bed == null || bed.Destroyed || !bed.Spawned)
            {
                return false;
            }

            if (humanSpouse.CurJobDef != JobDefOf.Lovin || humanSpouse.CurJob == null)
            {
                return false;
            }

            Job humanJob = humanSpouse.CurJob;
            if (humanJob.GetTarget(TargetIndex.A).Thing != syntheticCompanion)
            {
                return false;
            }

            return humanJob.GetTarget(TargetIndex.B).Thing == bed;
        }

        private static bool IsLivingSpawnedPawn(Pawn? pawn)
        {
            return pawn != null && !pawn.Destroyed && !pawn.Dead && pawn.Spawned;
        }

        public static Pawn? TryFindEnabledSyntheticCompanionForRemoteLovin(Pawn? humanSpouse)
        {
            if (humanSpouse == null || !IsValidHumanSpouseInitiator(humanSpouse))
            {
                return null;
            }

            Building_Bed? bed = humanSpouse.CurrentBed();
            if (bed == null || !IsBedStructurallyEligibleForRemoteLovin(bed))
            {
                return null;
            }

            Pawn_RelationsTracker? relationsTracker = humanSpouse.relations;
            if (relationsTracker == null)
            {
                return null;
            }

            Pawn? best = null;
            List<DirectPawnRelation> relations = relationsTracker.DirectRelations;
            for (int i = 0; i < relations.Count; i++)
            {
                DirectPawnRelation relation = relations[i];
                if (relation.def != PawnRelationDefOf.Spouse)
                {
                    continue;
                }

                Pawn? syntheticCompanion = relation.otherPawn;
                if (!IsValidEnabledSyntheticCompanionForRemoteLovin(humanSpouse, syntheticCompanion, bed))
                {
                    continue;
                }

                if (best == null || syntheticCompanion!.thingIDNumber < best.thingIDNumber)
                {
                    best = syntheticCompanion;
                }
            }

            return best;
        }

        public static Pawn? TryFindVanillaLovePartnerOccupyingBed(Pawn pawn)
        {
            Building_Bed? bed = pawn.CurrentBed();
            if (bed == null || bed.SleepingSlotsCount <= 1)
            {
                return null;
            }

            if (!LovePartnerRelationUtility.HasAnyLovePartner(pawn))
            {
                return null;
            }

            foreach (Pawn curOccupant in bed.CurOccupants)
            {
                if (curOccupant != pawn
                    && LovePartnerRelationUtility.LovePartnerRelationExists(pawn, curOccupant))
                {
                    return curOccupant;
                }
            }

            return null;
        }

        public static void CollectDirectSpousePawns(Pawn pawn, List<Pawn> into)
        {
            into.Clear();
            if (pawn.relations == null)
            {
                return;
            }

            List<DirectPawnRelation> relations = pawn.relations.DirectRelations;
            for (int i = 0; i < relations.Count; i++)
            {
                DirectPawnRelation relation = relations[i];
                if (relation.def != PawnRelationDefOf.Spouse || relation.otherPawn == null)
                {
                    continue;
                }

                into.Add(relation.otherPawn);
            }
        }

        public static Pawn? SelectPreferredPawnByThingId(List<Pawn> candidates)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return null;
            }

            Pawn? best = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (best == null || candidates[i].thingIDNumber < best.thingIDNumber)
                {
                    best = candidates[i];
                }
            }

            return best;
        }

        public static bool IsValidHumanSpouseInitiator(Pawn? pawn)
        {
            return EvaluateHumanSpouseInitiator(pawn, null) == null;
        }

        public static string? EvaluateHumanSpouseInitiator(Pawn? pawn, List<string>? failures)
        {
            bool collecting = failures != null;
            string? first = null;

            void Fail(string reason)
            {
                first ??= reason;
                failures?.Add(reason);
            }

            if (pawn == null)
            {
                Fail("发起者不存在。");
                return first;
            }

            if (SyntheticCompanionStateUtility.IsSyntheticCompanion(pawn))
            {
                Fail("发起者错误地是授权机械体本人（必须由人类配偶发起）。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (pawn.Destroyed)
            {
                Fail("发起者已销毁。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (pawn.Dead)
            {
                Fail("发起者已死亡。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (!pawn.Spawned)
            {
                Fail("发起者未在地图上生成。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (pawn.RaceProps == null || !pawn.RaceProps.Humanlike)
            {
                Fail("发起者不是 Humanlike。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (pawn.Map == null)
            {
                Fail("发起者不在有效地图上。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (pawn.relations == null)
            {
                Fail("发起者缺少 relations Tracker。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (pawn.health == null)
            {
                Fail("发起者缺少 health Tracker。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (pawn.jobs == null)
            {
                Fail("发起者缺少 jobs Tracker。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (pawn.mindState == null)
            {
                Fail("发起者缺少 mindState Tracker。");
                if (!collecting)
                {
                    return first;
                }
            }

            return collecting ? (failures!.Count > 0 ? first : null) : first;
        }

        private static bool IsBedStructurallyEligibleForRemoteLovin(Building_Bed? bed)
        {
            return EvaluateBedStructurallyEligible(bed, null) == null;
        }

        public static string? EvaluateBedStructurallyEligible(
            Building_Bed? bed,
            List<string>? failures)
        {
            bool collecting = failures != null;
            string? first = null;

            void Fail(string reason)
            {
                first ??= reason;
                failures?.Add(reason);
            }

            if (bed == null)
            {
                Fail("无法取得发起者当前床铺（CurrentBed 为空）。");
                return first;
            }

            if (!bed.Spawned)
            {
                Fail("床铺未生成。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (bed.Destroyed)
            {
                Fail("床铺已销毁。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (bed.Medical)
            {
                Fail("床铺是医疗床。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (bed.SleepingSlotsCount <= 1)
            {
                Fail("床铺睡眠位数不足（需要双人及以上床）。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (!bed.AnyUnoccupiedSleepingSlot)
            {
                Fail("床铺没有空闲睡眠位。");
            }

            return collecting ? (failures!.Count > 0 ? first : null) : first;
        }

        private static bool IsValidEnabledSyntheticCompanionForRemoteLovin(
            Pawn humanSpouse,
            Pawn? syntheticCompanion,
            Building_Bed bed)
        {
            return EvaluateEnabledSyntheticCompanionForRemoteLovin(humanSpouse, syntheticCompanion, bed, null) == null;
        }

        public static string? EvaluateEnabledSyntheticCompanionForRemoteLovin(
            Pawn humanSpouse,
            Pawn? syntheticCompanion,
            Building_Bed? bed,
            List<string>? failures)
        {
            bool collecting = failures != null;
            string? first = null;

            void Fail(string reason)
            {
                first ??= reason;
                failures?.Add(reason);
            }

            if (syntheticCompanion == null)
            {
                Fail("仿生伴侣引用为空。");
                return first;
            }

            if (syntheticCompanion == humanSpouse)
            {
                Fail("仿生伴侣与发起者是同一 Pawn。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (!SyntheticCompanionStateUtility.IsSyntheticCompanion(syntheticCompanion))
            {
                Fail("授权机械体不具备 SyntheticSpouseInteraction 能力。");
                if (!collecting)
                {
                    return first;
                }
            }
            else if (!SyntheticCompanionStateUtility.IsLovinWithSpouseEnabled(syntheticCompanion))
            {
                Fail("「与配偶爱爱」开关未开启。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (syntheticCompanion.Destroyed)
            {
                Fail("授权机械体已销毁。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (syntheticCompanion.Dead)
            {
                Fail("授权机械体已死亡。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (!syntheticCompanion.Spawned)
            {
                Fail("授权机械体未在地图上生成。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (syntheticCompanion.Map != humanSpouse.Map)
            {
                Fail("授权机械体与发起者不在同一地图。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (syntheticCompanion.Faction != Faction.OfPlayer)
            {
                Fail("授权机械体不属于玩家阵营。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (syntheticCompanion.relations == null)
            {
                Fail("授权机械体缺少 relations Tracker。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (syntheticCompanion.health == null)
            {
                Fail("授权机械体缺少 health Tracker。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (syntheticCompanion.jobs == null)
            {
                Fail("授权机械体缺少 jobs Tracker。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (syntheticCompanion.mindState == null)
            {
                Fail("授权机械体缺少 mindState Tracker。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (syntheticCompanion.relations == null
                || !syntheticCompanion.relations.DirectRelationExists(PawnRelationDefOf.Spouse, humanSpouse))
            {
                Fail("授权机械体对发起者不存在直接 Spouse 关系。");
                if (!collecting)
                {
                    return first;
                }
            }
            else if (collecting
                && (humanSpouse.relations == null
                    || !humanSpouse.relations.DirectRelationExists(
                        PawnRelationDefOf.Spouse,
                        syntheticCompanion)))
            {
                Fail("发起者对授权机械体不存在直接 Spouse 关系（双方关系不一致）。");
            }

            if (syntheticCompanion.health != null && !syntheticCompanion.health.capacities.CanBeAwake)
            {
                Fail("授权机械体不具备 CanBeAwake。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (syntheticCompanion.Downed)
            {
                Fail("授权机械体处于倒地状态。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (syntheticCompanion.Drafted)
            {
                Fail("授权机械体已被征召。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (syntheticCompanion.InMentalState)
            {
                Fail("授权机械体处于精神状态。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (syntheticCompanion.IsBurning())
            {
                Fail("授权机械体正在着火。");
                if (!collecting)
                {
                    return first;
                }
            }

            string? interruptFail = EvaluateCurrentJobInterruptible(syntheticCompanion, failures);
            if (interruptFail != null && !collecting)
            {
                return first ?? interruptFail;
            }

            if (first != null && !collecting)
            {
                return first;
            }

            if (bed == null)
            {
                Fail("缺少可用于预检的床铺。");
                if (!collecting)
                {
                    return first;
                }
            }
            else
            {
                if (!syntheticCompanion.CanReach(bed, PathEndMode.OnCell, Danger.Some))
                {
                    Fail("授权机械体以 Danger.Some 无法到达床铺。");
                    if (!collecting)
                    {
                        return first;
                    }
                }

                string? bedFail = EvaluateCanSyntheticCompanionUseBedForRemoteLovin(
                    humanSpouse,
                    syntheticCompanion,
                    bed,
                    failures);
                if (bedFail != null && !collecting)
                {
                    return first ?? bedFail;
                }

                if (!syntheticCompanion.CanReserve(bed, bed.SleepingSlotsCount, 0))
                {
                    Fail("授权机械体无法预约该床铺。");
                    if (!collecting)
                    {
                        return first;
                    }
                }
            }

            if (!humanSpouse.CanReserve(syntheticCompanion) || !syntheticCompanion.CanReserve(humanSpouse))
            {
                Fail("双方无法互相预约。");
                if (!collecting)
                {
                    return first;
                }
            }

            return collecting ? (failures!.Count > 0 ? (first ?? failures[0]) : null) : first;
        }

        public static string? EvaluateCanSyntheticCompanionUseBedForRemoteLovin(
            Pawn humanSpouse,
            Pawn syntheticCompanion,
            Building_Bed bed,
            List<string>? failures)
        {
            bool collecting = failures != null;
            string? first = null;

            void Fail(string reason)
            {
                first ??= reason;
                failures?.Add(reason);
            }

            if (bed.Destroyed)
            {
                Fail("床铺已销毁。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (!bed.Spawned)
            {
                Fail("床铺未生成。");
                if (!collecting)
                {
                    return first;
                }
            }

            Map? map = humanSpouse.Map;
            if (map == null || bed.Map != map || syntheticCompanion.Map != map)
            {
                Fail("床铺与双方不在同一地图。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (bed.IsBurning())
            {
                Fail("床铺正在燃烧。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (bed.Medical)
            {
                Fail("床铺是医疗床。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (bed.SleepingSlotsCount <= 1)
            {
                Fail("床铺不是双人及以上床。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (!bed.AnyUnoccupiedSleepingSlot)
            {
                Fail("床铺没有空闲睡眠位。");
                if (!collecting)
                {
                    return first;
                }
            }

            ThingDef? bedDef = bed.def;
            if (bedDef == null || !bedDef.IsBed || bedDef.building == null)
            {
                Fail("床铺 def / building 数据无效。");
                if (!collecting)
                {
                    return first;
                }
            }
            else
            {
                if (!bedDef.building.bed_humanlike)
                {
                    Fail("床铺不是 humanlike bed。");
                    if (!collecting)
                    {
                        return first;
                    }
                }

                if (syntheticCompanion.BodySize > bedDef.building.bed_maxBodySize)
                {
                    Fail(
                        $"授权机械体体型 {syntheticCompanion.BodySize} 超过床铺 bed_maxBodySize "
                        + $"{bedDef.building.bed_maxBodySize}。");
                    if (!collecting)
                    {
                        return first;
                    }
                }
            }

            if (map != null && syntheticCompanion.HarmedByVacuum && bed.Position.GetVacuum(map) >= 0.5f)
            {
                Fail("真空环境会伤害授权机械体。");
                if (!collecting)
                {
                    return first;
                }
            }

            CompAssignableToPawn? assignable = bed.CompAssignableToPawn;
            if (assignable == null)
            {
                Fail("床铺缺少 CompAssignableToPawn。");
                if (!collecting)
                {
                    return first;
                }
            }
            else if (assignable.IdeoligionForbids(syntheticCompanion))
            {
                Fail("IdeoligionForbids 阻止授权机械体使用该床。");
                if (!collecting)
                {
                    return first;
                }
            }

            GuestStatus? guestStatus = syntheticCompanion.GuestStatus;
            bool forPrisoner = guestStatus == GuestStatus.Prisoner;
            bool forSlave = guestStatus == GuestStatus.Slave;
            if (bed.ForPrisoners != forPrisoner)
            {
                Fail(
                    $"囚犯床属性不匹配（床 ForPrisoners={bed.ForPrisoners}，"
                    + $"授权机械体 Prisoner={forPrisoner}）。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (bed.ForSlaves != forSlave)
            {
                Fail(
                    $"奴隶床属性不匹配（床 ForSlaves={bed.ForSlaves}，"
                    + $"授权机械体 Slave={forSlave}）。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (map != null && bed.ForPrisoners && !bed.Position.IsInPrisonCell(map))
            {
                Fail("囚犯床不位于牢房内。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (!syntheticCompanion.Downed && bed.IsForbidden(syntheticCompanion))
            {
                Fail("床铺对授权机械体 Forbidden。");
                if (!collecting)
                {
                    return first;
                }
            }

            bool isOwner = bed.IsOwner(syntheticCompanion, out _);
            if (!isOwner && !RestUtility.BedOwnerWillShare(bed, syntheticCompanion, null))
            {
                Fail("授权机械体不是床主，且 RestUtility.BedOwnerWillShare 不允许共享。");
            }

            return collecting ? (failures!.Count > 0 ? first : null) : first;
        }

        public static string? EvaluateCurrentJobInterruptible(Pawn syntheticCompanion, List<string>? failures)
        {
            bool collecting = failures != null;
            string? first = null;

            void Fail(string reason)
            {
                first ??= reason;
                failures?.Add(reason);
            }

            Job? curJob = syntheticCompanion.CurJob;
            if (curJob == null)
            {
                return null;
            }

            if (curJob.playerForced)
            {
                Fail(
                    $"当前 Job 为玩家强制（playerForced，JobDef={curJob.def?.defName ?? "null"}）。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (curJob.def == null)
            {
                Fail("当前 JobDef 为空。");
                if (!collecting)
                {
                    return first;
                }
            }
            else if (curJob.def.forceCompleteBeforeNextJob)
            {
                Fail(
                    $"当前 JobDef 要求完成后才能接下一个任务（forceCompleteBeforeNextJob，"
                    + $"{curJob.def.defName}）。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (syntheticCompanion.jobs != null && !syntheticCompanion.jobs.IsCurrentJobPlayerInterruptible())
            {
                Fail(
                    $"IsCurrentJobPlayerInterruptible 不允许中断（JobDef="
                    + $"{curJob.def?.defName ?? "null"}）。");
            }

            return collecting ? (failures!.Count > 0 ? first : null) : first;
        }

        public static bool FormatCanLovinCooldown(
            Pawn? pawn,
            int ticksGame,
            out int canLovinTick,
            out int remainingTicks,
            out string readableRemaining)
        {
            canLovinTick = 0;
            remainingTicks = 0;
            readableRemaining = "无";

            if (pawn?.mindState == null)
            {
                readableRemaining = "mindState 缺失";
                return true;
            }

            canLovinTick = pawn.mindState.canLovinTick;
            if (ticksGame >= canLovinTick)
            {
                readableRemaining = "已就绪";
                return false;
            }

            remainingTicks = canLovinTick - ticksGame;
            readableRemaining = remainingTicks.ToStringTicksToPeriod();
            return true;
        }

        public static void AppendBedOccupancySummary(StringBuilder sb, Building_Bed bed)
        {
            sb.AppendLine($"床铺名称：{bed.LabelCap}");
            sb.AppendLine($"床铺位置：{bed.Position}");
            sb.AppendLine($"床铺旋转：{bed.Rotation}");
            sb.AppendLine($"睡眠位数量：{bed.SleepingSlotsCount}");
            sb.AppendLine($"空闲睡眠位：{bed.AnyUnoccupiedSleepingSlot}");
            sb.AppendLine($"医疗床：{bed.Medical}");
            sb.AppendLine($"囚犯床：{bed.ForPrisoners}，奴隶床：{bed.ForSlaves}");

            sb.Append("当前占用者：");
            bool anyOcc = false;
            foreach (Pawn occ in bed.CurOccupants)
            {
                if (anyOcc)
                {
                    sb.Append("，");
                }

                sb.Append(DescribePawn(occ));
                anyOcc = true;
            }

            sb.AppendLine(anyOcc ? string.Empty : "无");

            sb.Append("当前所有者：");
            bool anyOwner = false;
            if (bed.CompAssignableToPawn != null)
            {
                List<Pawn> owners = bed.OwnersForReading;
                for (int i = 0; i < owners.Count; i++)
                {
                    Pawn owner = owners[i];
                    if (owner == null)
                    {
                        continue;
                    }

                    if (anyOwner)
                    {
                        sb.Append("，");
                    }

                    sb.Append(DescribePawn(owner));
                    anyOwner = true;
                }
            }

            sb.AppendLine(anyOwner ? string.Empty : "无");
        }

        public static string DescribePawn(Pawn? pawn)
        {
            if (pawn == null)
            {
                return "null";
            }

            return $"{pawn.LabelShort}（{pawn.ThingID}，thingIDNumber={pawn.thingIDNumber}）";
        }
    }
}
