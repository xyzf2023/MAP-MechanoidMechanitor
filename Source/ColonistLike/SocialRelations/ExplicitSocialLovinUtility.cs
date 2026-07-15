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
        /// 在人类配偶已躺在可用双人床、且床上尚无原版伴侣时，查找可响应 Lovin 的授权恋人。
        /// </summary>
        public static Pawn? TryFindEnabledLoverPartnerForRemoteLovin(Pawn humanSpouse)
        {
            if (humanSpouse?.relations == null || humanSpouse.Map == null)
            {
                return null;
            }

            Building_Bed? bed = humanSpouse.CurrentBed();
            if (!IsBedUsableForRemoteLovin(bed))
            {
                return null;
            }

            Pawn? best = null;
            List<DirectPawnRelation> relations = humanSpouse.relations.DirectRelations;
            for (int i = 0; i < relations.Count; i++)
            {
                DirectPawnRelation relation = relations[i];
                if (relation.def != PawnRelationDefOf.Spouse)
                {
                    continue;
                }

                Pawn? lover = relation.otherPawn;
                if (!IsValidEnabledLoverForRemoteLovin(humanSpouse, lover, bed!))
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

        private static bool IsBedUsableForRemoteLovin(Building_Bed? bed)
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

            if (!lover.CanReach(bed, PathEndMode.OnCell, Danger.Deadly))
            {
                return false;
            }

            // 不调用 CanUseBedNow：此时恋人尚未拿到 Lovin Job，CanUseBedEver 仍会因机械体被拒。
            // 预约与所有者共享条件需提前排除；床铺可用性在 Job 开始后由 CanUseBedEver 补丁放行。
            if (!RestUtility.BedOwnerWillShare(bed, lover, null))
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
        /// 玩家强制 Job 或不可中断 Job 不得作为远程 Lovin 候选人。
        /// 使用原版 IsCurrentJobPlayerInterruptible（检查 JobDef.playerInterruptible 与 JobDriver.PlayerInterruptable）。
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

            return lover.jobs.IsCurrentJobPlayerInterruptible();
        }
    }
}
