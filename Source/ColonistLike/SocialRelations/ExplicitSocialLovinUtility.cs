using System.Collections.Generic;
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
        /// 发起者必须是存活、已生成、具备必要 Tracker 的 Humanlike，且不能是授权恋人本人。
        /// </summary>
        private static bool IsValidHumanSpouseInitiator(Pawn? pawn)
        {
            if (pawn == null || ExplicitSocialRelationUtility.IsOptedIn(pawn))
            {
                return false;
            }

            if (pawn.Destroyed || pawn.Dead || !pawn.Spawned)
            {
                return false;
            }

            if (pawn.RaceProps == null || !pawn.RaceProps.Humanlike)
            {
                return false;
            }

            if (pawn.Map == null
                || pawn.relations == null
                || pawn.health == null
                || pawn.jobs == null
                || pawn.mindState == null)
            {
                return false;
            }

            return true;
        }

        private static bool IsBedStructurallyEligibleForRemoteLovin(Building_Bed? bed)
        {
            if (bed == null || !bed.Spawned || bed.Destroyed)
            {
                return false;
            }

            if (bed.Medical || bed.SleepingSlotsCount <= 1)
            {
                return false;
            }

            // 人类已占一格，必须还有空闲睡眠位供恋人使用。
            return bed.AnyUnoccupiedSleepingSlot;
        }

        private static bool IsValidEnabledLoverForRemoteLovin(
            Pawn humanSpouse,
            Pawn? lover,
            Building_Bed bed)
        {
            if (lover == null || lover == humanSpouse)
            {
                return false;
            }

            CompExplicitSocialRelationUser? comp =
                lover.GetComp<CompExplicitSocialRelationUser>();
            if (comp == null || !comp.LovinWithSpouseEnabled)
            {
                return false;
            }

            if (lover.Destroyed || lover.Dead || !lover.Spawned)
            {
                return false;
            }

            if (lover.Map != humanSpouse.Map)
            {
                return false;
            }

            if (lover.Faction != Faction.OfPlayer)
            {
                return false;
            }

            if (lover.relations == null
                || lover.health == null
                || lover.jobs == null
                || lover.mindState == null)
            {
                return false;
            }

            if (!lover.relations.DirectRelationExists(PawnRelationDefOf.Spouse, humanSpouse))
            {
                return false;
            }

            if (!lover.health.capacities.CanBeAwake
                || lover.Downed
                || lover.Drafted
                || lover.InMentalState
                || lover.IsBurning())
            {
                return false;
            }

            if (!CanSafelyInterruptCurrentJobForForcedLovin(lover))
            {
                return false;
            }

            if (!lover.CanReach(bed, PathEndMode.OnCell, Danger.Some))
            {
                return false;
            }

            if (!CanLoverUseBedForRemoteLovin(humanSpouse, lover, bed))
            {
                return false;
            }

            // 提前排除必然导致原版预约失败的情况；双方互约仍由 JobGiver_DoLovin 再验一次。
            if (!lover.CanReserve(bed, bed.SleepingSlotsCount, 0))
            {
                return false;
            }

            if (!humanSpouse.CanReserve(lover) || !lover.CanReserve(humanSpouse))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 受控镜像原版 RestUtility.CanUseBedNow / CanUseBedEver 中与本场景相关的硬门槛。
        /// 不调用 CanUseBedNow：恋人此时尚未持有 JobDefOf.Lovin，现有 CanUseBedEver 补丁不会放行机械体。
        /// 不临时修改 CurJob，也不使用全局预检标记。目的是避免原版配套 Lovin Job 开始后立刻因床铺条件失败。
        /// </summary>
        private static bool CanLoverUseBedForRemoteLovin(
            Pawn humanSpouse,
            Pawn lover,
            Building_Bed bed)
        {
            if (bed.Destroyed || !bed.Spawned)
            {
                return false;
            }

            Map? map = humanSpouse.Map;
            if (map == null || bed.Map != map || lover.Map != map)
            {
                return false;
            }

            if (bed.IsBurning())
            {
                return false;
            }

            if (bed.Medical || bed.SleepingSlotsCount <= 1)
            {
                return false;
            }

            if (!bed.AnyUnoccupiedSleepingSlot)
            {
                return false;
            }

            ThingDef? bedDef = bed.def;
            if (bedDef == null || !bedDef.IsBed || bedDef.building == null)
            {
                return false;
            }

            // 镜像 CanUseBedEver 中与「人类床 + 体型」相关的部分；故意跳过 IsMechanoid / Humanlike 门槛。
            if (!bedDef.building.bed_humanlike)
            {
                return false;
            }

            if (lover.BodySize > bedDef.building.bed_maxBodySize)
            {
                return false;
            }

            if (lover.HarmedByVacuum && bed.Position.GetVacuum(map) >= 0.5f)
            {
                return false;
            }

            CompAssignableToPawn? assignable = bed.CompAssignableToPawn;
            if (assignable == null)
            {
                return false;
            }

            if (assignable.IdeoligionForbids(lover))
            {
                return false;
            }

            GuestStatus? guestStatus = lover.GuestStatus;
            bool forPrisoner = guestStatus == GuestStatus.Prisoner;
            bool forSlave = guestStatus == GuestStatus.Slave;
            if (bed.ForPrisoners != forPrisoner)
            {
                return false;
            }

            if (bed.ForSlaves != forSlave)
            {
                return false;
            }

            if (bed.ForPrisoners && !bed.Position.IsInPrisonCell(map))
            {
                return false;
            }

            // 自动 Lovin 不得绕过玩家 Forbidden；不必依赖 IsColonist。
            if (!lover.Downed && bed.IsForbidden(lover))
            {
                return false;
            }

            bool isOwner = bed.IsOwner(lover, out _);
            if (!isOwner && !RestUtility.BedOwnerWillShare(bed, lover, null))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 玩家强制 Job、须完成当前 Job，或不可中断 Job 不得作为远程 Lovin 候选人。
        /// IsCurrentJobPlayerInterruptible 覆盖 JobDef.playerInterruptible 与 JobDriver.PlayerInterruptable。
        /// </summary>
        private static bool CanSafelyInterruptCurrentJobForForcedLovin(Pawn lover)
        {
            Job? curJob = lover.CurJob;
            if (curJob == null)
            {
                return true;
            }

            if (curJob.playerForced)
            {
                return false;
            }

            if (curJob.def == null)
            {
                return false;
            }

            if (curJob.def.forceCompleteBeforeNextJob)
            {
                return false;
            }

            return lover.jobs.IsCurrentJobPlayerInterruptible();
        }
    }
}
