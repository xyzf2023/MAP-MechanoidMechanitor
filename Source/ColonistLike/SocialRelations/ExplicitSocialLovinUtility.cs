using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 远程恋人配偶 Lovin 候选查询。不扫描地图，只遍历发起者 DirectRelations。
    /// </summary>
    public static class ExplicitSocialLovinUtility
    {
        /// <summary>
        /// 授权恋人正在床上执行 JobDefOf.Lovin 时，使用与 Humanlike 相同的床上绘制分支。
        /// </summary>
        public static bool ShouldUseHumanlikeBedLovinRender(Pawn? pawn, bool isPortrait = false)
        {
            if (isPortrait || pawn == null)
            {
                return false;
            }

            if (!ExplicitSocialRelationUtility.IsOptedIn(pawn))
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

            return pawn.CurrentBed() != null;
        }

        public static bool ShouldUseHumanlikeBedLovinRender(Pawn? pawn, PawnRenderFlags flags)
        {
            if (flags.FlagSet(PawnRenderFlags.Portrait))
            {
                return false;
            }

            return ShouldUseHumanlikeBedLovinRender(pawn, isPortrait: false);
        }

        /// <summary>
        /// Lovin 频率计算中，授权恋人采用的有效年龄（原版 FlatHill 峰值区间内）。
        /// </summary>
        public const float LovinEffectiveAgeYearsForOptedInLover = 18f;

        /// <summary>
        /// 与原版 LovePartnerRelationUtility.LovinMtbSinglePawnFactor 完全相同的年龄 FlatHill。
        /// GenMath.FlatHill(0f, 14f, 16f, 25f, 80f, 0.2f, age)
        /// </summary>
        public static float EvaluateLovinAgeFlatHill(float ageYears)
        {
            return GenMath.FlatHill(0f, 14f, 16f, 25f, 80f, 0.2f, ageYears);
        }

        /// <summary>
        /// 直接按原版 LovinMtbSinglePawnFactor 计算授权恋人单体系数，但年龄固定为有效年龄 18 岁。
        /// 保留疼痛与意识；成功写入 result 时返回 true（由 Prefix 跳过原版）。
        /// </summary>
        public static bool TryComputeOptedInLoverLovinMtbSinglePawnFactor(
            Pawn? pawn,
            out float result)
        {
            result = 0f;
            if (pawn == null || !ExplicitSocialRelationUtility.IsOptedIn(pawn))
            {
                return false;
            }

            if (pawn.health?.hediffSet == null || pawn.health.capacities == null)
            {
                return false;
            }

            float effectiveAgeFactor =
                EvaluateLovinAgeFlatHill(LovinEffectiveAgeYearsForOptedInLover);
            if (effectiveAgeFactor <= 0f
                || float.IsNaN(effectiveAgeFactor)
                || float.IsInfinity(effectiveAgeFactor))
            {
                return false;
            }

            // 严格镜像原版：疼痛 → 意识 → 再除以年龄 FlatHill。
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

        /// <summary>
        /// 人类配偶发起、授权恋人远程响应的 Lovin Job（执行者是人类发起者）。
        /// </summary>
        public static bool IsRemoteHumanSpouseLovinJob(Pawn? actor, Pawn? partner, Building_Bed? bed)
        {
            if (actor == null || partner == null || bed == null || bed.Destroyed)
            {
                return false;
            }

            // 发起者必须不是授权恋人，伴侣必须是授权恋人。
            if (ExplicitSocialRelationUtility.IsOptedIn(actor)
                || !ExplicitSocialRelationUtility.IsOptedIn(partner))
            {
                return false;
            }

            if (actor.RaceProps == null || !actor.RaceProps.Humanlike)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 授权恋人配套响应人类配偶的 Lovin Job（执行者是授权恋人）。
        /// </summary>
        public static bool IsRemoteLoverCompanionLovinJob(Pawn? actor, Pawn? partner, Building_Bed? bed)
        {
            if (actor == null || partner == null || bed == null || bed.Destroyed)
            {
                return false;
            }

            if (!ExplicitSocialRelationUtility.IsOptedIn(actor)
                || ExplicitSocialRelationUtility.IsOptedIn(partner))
            {
                return false;
            }

            if (partner.RaceProps == null || !partner.RaceProps.Humanlike)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// ThinkNode_ChancePerHour_Lovin：仅当躺床发起者是 Humanlike，且 GetPartnerInMyBed
        /// 实际结果为已开「与配偶爱爱」的授权恋人配偶时，才套用特殊 MTB。
        /// </summary>
        public static bool IsFrequencyBoostRemoteEnabledLoverPartner(
            Pawn? initiator,
            Pawn? partner)
        {
            if (initiator == null || partner == null || initiator == partner)
            {
                return false;
            }

            if (ExplicitSocialRelationUtility.IsOptedIn(initiator)
                || !ExplicitSocialRelationUtility.IsOptedIn(partner))
            {
                return false;
            }

            if (initiator.RaceProps == null || !initiator.RaceProps.Humanlike)
            {
                return false;
            }

            CompExplicitSocialRelationUser? comp =
                partner.GetComp<CompExplicitSocialRelationUser>();
            if (comp == null || !comp.LovinWithSpouseEnabled)
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

        /// <summary>
        /// 已开始并正确绑定的殖民者—授权恋人同床 Lovin（不依赖开关当前是否仍开启）。
        /// </summary>
        public static bool IsBoundHumanLoverSpouseLovinDriver(JobDriver? driver)
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
            Pawn? lover;
            if (IsRemoteLoverCompanionLovinJob(actor, partner, bed))
            {
                lover = actor;
                human = partner;
            }
            else if (IsRemoteHumanSpouseLovinJob(actor, partner, bed))
            {
                human = actor;
                lover = partner;
            }
            else
            {
                return false;
            }

            if (human.relations == null || lover.relations == null)
            {
                return false;
            }

            if (!lover.relations.DirectRelationExists(PawnRelationDefOf.Spouse, human)
                || !human.relations.DirectRelationExists(PawnRelationDefOf.Spouse, lover))
            {
                return false;
            }

            // 不要求对方此刻仍持有 Lovin：后结束的一方结束时，对方可能已离开该 Job。
            return true;
        }

        /// <summary>
        /// 从 Lovin Job 读取 TargetIndex.A / B（伴侣与床铺）。
        /// </summary>
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

        /// <summary>
        /// 恋人仍绑定本次远程 Lovin（可在赶路中）；供人类等待 Toil 的 FailOn。
        /// </summary>
        public static bool IsRemoteLoverStillBoundForHumanWait(
            Pawn humanSpouse,
            Pawn? lover,
            Building_Bed bed)
        {
            if (!IsLivingSpawnedPawn(lover) || lover!.Map != humanSpouse.Map)
            {
                return false;
            }

            if (bed.Destroyed || !bed.Spawned)
            {
                return false;
            }

            if (lover.CurJobDef != JobDefOf.Lovin || lover.CurJob == null)
            {
                return false;
            }

            Job loverJob = lover.CurJob;
            if (loverJob.GetTarget(TargetIndex.A).Thing != humanSpouse)
            {
                return false;
            }

            return loverJob.GetTarget(TargetIndex.B).Thing == bed;
        }

        /// <summary>
        /// 恋人已真正入床，可进入原版最终 LayDown 倒计时。
        /// </summary>
        public static bool IsRemoteLoverPhysicallyReadyInBed(
            Pawn humanSpouse,
            Pawn? lover,
            Building_Bed bed)
        {
            if (!IsRemoteLoverStillBoundForHumanWait(humanSpouse, lover, bed))
            {
                return false;
            }

            if (!lover!.GetPosture().InBed())
            {
                return false;
            }

            return lover.CurrentBed() == bed;
        }

        /// <summary>
        /// 人类配偶仍绑定本次远程 Lovin；供恋人全程 AddFailCondition（true=应失败）。
        /// </summary>
        public static bool ShouldFailRemoteLoverCompanionLovin(
            Pawn lover,
            Pawn? humanSpouse,
            Building_Bed? bed)
        {
            return !IsRemoteHumanStillBoundForLoverCompanion(lover, humanSpouse, bed);
        }

        /// <summary>
        /// 人类配偶仍在执行针对该恋人的 Lovin，且床铺有效。
        /// </summary>
        public static bool IsRemoteHumanStillBoundForLoverCompanion(
            Pawn lover,
            Pawn? humanSpouse,
            Building_Bed? bed)
        {
            if (!IsLivingSpawnedPawn(humanSpouse) || humanSpouse!.Map != lover.Map)
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
            if (humanJob.GetTarget(TargetIndex.A).Thing != lover)
            {
                return false;
            }

            return humanJob.GetTarget(TargetIndex.B).Thing == bed;
        }

        private static bool IsLivingSpawnedPawn(Pawn? pawn)
        {
            return pawn != null && !pawn.Destroyed && !pawn.Dead && pawn.Spawned;
        }

        /// <summary>
        /// 在人类配偶已躺在可用双人床、且床上尚无原版伴侣时，查找可响应 Lovin 的授权恋人。
        /// </summary>
        public static Pawn? TryFindEnabledLoverPartnerForRemoteLovin(Pawn? humanSpouse)
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

            // IsValidHumanSpouseInitiator 已确认 relations 非空；此处再判一次供可空流分析。
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

                Pawn? lover = relation.otherPawn;
                if (!IsValidEnabledLoverForRemoteLovin(humanSpouse, lover, bed))
                {
                    continue;
                }

                if (best == null || lover!.thingIDNumber < best.thingIDNumber)
                {
                    best = lover;
                }
            }

            return best;
        }

        /// <summary>
        /// 只读复刻原版 GetPartnerInMyBed 床上占用者查找，不经过模组 Postfix。
        /// </summary>
        internal static Pawn? TryFindVanillaLovePartnerOccupyingBed(Pawn pawn)
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

        /// <summary>
        /// 直接遍历 DirectRelations 收集 Spouse（不受 IsFlesh 限制）。
        /// </summary>
        internal static void CollectDirectSpousePawns(Pawn pawn, List<Pawn> into)
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

                if (!into.Contains(relation.otherPawn))
                {
                    into.Add(relation.otherPawn);
                }
            }
        }

        /// <summary>
        /// 与 TryFindEnabledLoverPartnerForRemoteLovin 相同的选型键：thingIDNumber 升序。
        /// </summary>
        internal static Pawn? SelectPreferredLoverByThingId(List<Pawn> lovers)
        {
            Pawn? best = null;
            for (int i = 0; i < lovers.Count; i++)
            {
                Pawn lover = lovers[i];
                if (best == null || lover.thingIDNumber < best.thingIDNumber)
                {
                    best = lover;
                }
            }

            return best;
        }

        /// <summary>
        /// 发起者必须是存活、已生成、具备必要 Tracker 的 Humanlike，且不能是授权恋人本人。
        /// </summary>
        internal static bool IsValidHumanSpouseInitiator(Pawn? pawn)
        {
            return EvaluateHumanSpouseInitiator(pawn, null) == null;
        }

        /// <summary>
        /// 返回首个发起者阻断原因；通过时返回 null。failures 非空时收集全部失败项（不改变正式结果语义）。
        /// </summary>
        internal static string? EvaluateHumanSpouseInitiator(Pawn? pawn, List<string>? failures)
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

            if (ExplicitSocialRelationUtility.IsOptedIn(pawn))
            {
                Fail("发起者错误地是授权恋人本人（必须由人类配偶发起）。");
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

        internal static bool IsBedStructurallyEligibleForRemoteLovin(Building_Bed? bed)
        {
            return EvaluateBedStructurallyEligible(bed, null) == null;
        }

        internal static string? EvaluateBedStructurallyEligible(Building_Bed? bed, List<string>? failures)
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
                if (!collecting)
                {
                    return first;
                }
            }

            return collecting ? (failures!.Count > 0 ? first : null) : first;
        }

        internal static bool IsValidEnabledLoverForRemoteLovin(
            Pawn humanSpouse,
            Pawn? lover,
            Building_Bed bed)
        {
            return EvaluateEnabledLoverForRemoteLovin(humanSpouse, lover, bed, null) == null;
        }

        /// <summary>
        /// 正式候选恋人条件；failures 非空时收集全部阻断原因。
        /// </summary>
        internal static string? EvaluateEnabledLoverForRemoteLovin(
            Pawn humanSpouse,
            Pawn? lover,
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

            if (lover == null)
            {
                Fail("恋人引用为空。");
                return first;
            }

            if (lover == humanSpouse)
            {
                Fail("恋人与发起者是同一 Pawn。");
                if (!collecting)
                {
                    return first;
                }
            }

            CompExplicitSocialRelationUser? comp =
                lover.GetComp<CompExplicitSocialRelationUser>();
            if (comp == null)
            {
                Fail("恋人不具备 CompExplicitSocialRelationUser。");
                if (!collecting)
                {
                    return first;
                }
            }
            else if (!comp.LovinWithSpouseEnabled)
            {
                Fail("「与配偶爱爱」开关未开启。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (lover.Destroyed)
            {
                Fail("恋人已销毁。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (lover.Dead)
            {
                Fail("恋人已死亡。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (!lover.Spawned)
            {
                Fail("恋人未在地图上生成。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (lover.Map != humanSpouse.Map)
            {
                Fail("恋人与发起者不在同一地图。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (lover.Faction != Faction.OfPlayer)
            {
                Fail("恋人不属于玩家阵营。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (lover.relations == null)
            {
                Fail("恋人缺少 relations Tracker。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (lover.health == null)
            {
                Fail("恋人缺少 health Tracker。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (lover.jobs == null)
            {
                Fail("恋人缺少 jobs Tracker。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (lover.mindState == null)
            {
                Fail("恋人缺少 mindState Tracker。");
                if (!collecting)
                {
                    return first;
                }
            }

            // 正式路径只要求恋人对发起者存在 Spouse（与变更前一致）。
            if (lover.relations == null
                || !lover.relations.DirectRelationExists(PawnRelationDefOf.Spouse, humanSpouse))
            {
                Fail("恋人对发起者不存在直接 Spouse 关系。");
                if (!collecting)
                {
                    return first;
                }
            }
            else if (collecting
                && (humanSpouse.relations == null
                    || !humanSpouse.relations.DirectRelationExists(
                        PawnRelationDefOf.Spouse,
                        lover)))
            {
                Fail("发起者对恋人不存在直接 Spouse 关系（双方关系不一致）。");
            }

            if (lover.health != null && !lover.health.capacities.CanBeAwake)
            {
                Fail("恋人不具备 CanBeAwake。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (lover.Downed)
            {
                Fail("恋人处于倒地状态。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (lover.Drafted)
            {
                Fail("恋人已被征召。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (lover.InMentalState)
            {
                Fail("恋人处于精神状态。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (lover.IsBurning())
            {
                Fail("恋人正在着火。");
                if (!collecting)
                {
                    return first;
                }
            }

            string? interruptFail = EvaluateCurrentJobInterruptible(lover, failures);
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
                if (!lover.CanReach(bed, PathEndMode.OnCell, Danger.Some))
                {
                    Fail("恋人以 Danger.Some 无法到达床铺。");
                    if (!collecting)
                    {
                        return first;
                    }
                }

                string? bedFail = EvaluateCanLoverUseBedForRemoteLovin(
                    humanSpouse,
                    lover,
                    bed,
                    failures);
                if (bedFail != null && !collecting)
                {
                    return first ?? bedFail;
                }

                if (!lover.CanReserve(bed, bed.SleepingSlotsCount, 0))
                {
                    Fail("恋人无法预约该床铺。");
                    if (!collecting)
                    {
                        return first;
                    }
                }
            }

            if (!humanSpouse.CanReserve(lover) || !lover.CanReserve(humanSpouse))
            {
                Fail("双方无法互相预约。");
                if (!collecting)
                {
                    return first;
                }
            }

            return collecting ? (failures!.Count > 0 ? (first ?? failures[0]) : null) : first;
        }

        internal static bool CanLoverUseBedForRemoteLovin(
            Pawn humanSpouse,
            Pawn lover,
            Building_Bed bed)
        {
            return EvaluateCanLoverUseBedForRemoteLovin(humanSpouse, lover, bed, null) == null;
        }

        /// <summary>
        /// 受控镜像原版 RestUtility.CanUseBedNow / CanUseBedEver 中与本场景相关的硬门槛。
        /// 不调用 CanUseBedNow：恋人此时尚未持有 JobDefOf.Lovin，现有 CanUseBedEver 补丁不会放行机械体。
        /// </summary>
        internal static string? EvaluateCanLoverUseBedForRemoteLovin(
            Pawn humanSpouse,
            Pawn lover,
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
            if (map == null || bed.Map != map || lover.Map != map)
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

                if (lover.BodySize > bedDef.building.bed_maxBodySize)
                {
                    Fail(
                        $"恋人体型 {lover.BodySize} 超过床铺 bed_maxBodySize {bedDef.building.bed_maxBodySize}。");
                    if (!collecting)
                    {
                        return first;
                    }
                }
            }

            if (map != null && lover.HarmedByVacuum && bed.Position.GetVacuum(map) >= 0.5f)
            {
                Fail("真空环境会伤害恋人。");
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
            else if (assignable.IdeoligionForbids(lover))
            {
                Fail("IdeoligionForbids 阻止恋人使用该床。");
                if (!collecting)
                {
                    return first;
                }
            }

            GuestStatus? guestStatus = lover.GuestStatus;
            bool forPrisoner = guestStatus == GuestStatus.Prisoner;
            bool forSlave = guestStatus == GuestStatus.Slave;
            if (bed.ForPrisoners != forPrisoner)
            {
                Fail(
                    $"囚犯床属性不匹配（床 ForPrisoners={bed.ForPrisoners}，恋人 Prisoner={forPrisoner}）。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (bed.ForSlaves != forSlave)
            {
                Fail(
                    $"奴隶床属性不匹配（床 ForSlaves={bed.ForSlaves}，恋人 Slave={forSlave}）。");
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

            if (!lover.Downed && bed.IsForbidden(lover))
            {
                Fail("床铺对恋人 Forbidden。");
                if (!collecting)
                {
                    return first;
                }
            }

            bool isOwner = bed.IsOwner(lover, out _);
            if (!isOwner && !RestUtility.BedOwnerWillShare(bed, lover, null))
            {
                Fail("恋人不是床主，且 RestUtility.BedOwnerWillShare 不允许共享。");
                if (!collecting)
                {
                    return first;
                }
            }

            return collecting ? (failures!.Count > 0 ? first : null) : first;
        }

        internal static bool CanSafelyInterruptCurrentJobForForcedLovin(Pawn lover)
        {
            return EvaluateCurrentJobInterruptible(lover, null) == null;
        }

        /// <summary>
        /// 玩家强制 Job、须完成当前 Job，或不可中断 Job 不得作为远程 Lovin 候选人。
        /// </summary>
        internal static string? EvaluateCurrentJobInterruptible(Pawn lover, List<string>? failures)
        {
            bool collecting = failures != null;
            string? first = null;

            void Fail(string reason)
            {
                first ??= reason;
                failures?.Add(reason);
            }

            Job? curJob = lover.CurJob;
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
                    $"当前 JobDef 要求完成后才能接下一个任务（forceCompleteBeforeNextJob，{curJob.def.defName}）。");
                if (!collecting)
                {
                    return first;
                }
            }

            if (lover.jobs != null && !lover.jobs.IsCurrentJobPlayerInterruptible())
            {
                Fail(
                    $"IsCurrentJobPlayerInterruptible 不允许中断（JobDef={curJob.def?.defName ?? "null"}）。");
                if (!collecting)
                {
                    return first;
                }
            }

            return collecting ? (failures!.Count > 0 ? first : null) : first;
        }

        /// <summary>
        /// 将 canLovinTick 格式化为可读状态；冷却未结束时返回 true（阻断）。
        /// </summary>
        internal static bool FormatCanLovinCooldown(
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

        /// <summary>
        /// 诊断用：追加床铺占用者与所有者摘要（只读）。
        /// </summary>
        internal static void AppendBedOccupancySummary(StringBuilder sb, Building_Bed bed)
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

        internal static string DescribePawn(Pawn? pawn)
        {
            if (pawn == null)
            {
                return "null";
            }

            return $"{pawn.LabelShort}（{pawn.ThingID}，thingIDNumber={pawn.thingIDNumber}）";
        }
    }
}
