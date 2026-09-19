using RimWorld;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体表现的统一入口。可选展开动画由 Job 持有；未配置者保留原版 Skip。
    /// 手动解除只在这里分流到动画 Job，正式事务仍由 TeardownService 执行。
    /// Skip 视觉 Effecter 全部来自 Core；Psycast_Skip_Exit 声音带
    /// MayRequireRoyalty，必须确认 Royalty 激活且 Def 可用后再调用。
    /// </summary>
    internal static class MechFusionVisualUtility
    {
        internal static ThingDef? GetTransitionMoteDef(Pawn? source)
        {
            return source?.def.GetCompProperties<CompProperties_MechFusionInnate>()
                ?.transitionMoteDef;
        }

        internal static MechFusionTransitionVisual? BeginExpandedTransition(
            Pawn actor, Pawn source, Job owner, Pawn? visualTarget = null)
        {
            return MechFusionTransitionVisual.TryCreate(
                actor, owner, GetTransitionMoteDef(source), visualTarget);
        }

        internal static bool RequestManualTeardown(MechFusionSession session)
        {
            Pawn? wearer = session.WearerPawn;
            if (GetTransitionMoteDef(session.SourcePawn) == null
                || wearer?.Spawned != true || wearer.Dead || wearer.Downed
                || wearer.InMentalState || wearer.jobs == null)
            {
                // 非战车及无法执行地图动画的场景保持现有解除路径。
                return MechFusionTeardownService.TryTeardown(
                    session, MechFusionExitReason.Manual, force: false);
            }

            if (!session.IsActive || MechanicalFlightUtility.IsAirborne(wearer)
                || wearer.CurJobDef == MAPMechanitor_JobDefOf.MAP_MechFusionReleaseTransition
                || session.FusionApparel == null || session.FusionApparel.Destroyed)
            {
                return false;
            }

            // 外甲实例绑定这次会话，即使指令排队，也不能解除后来开始的另一场合体。
            Job job = JobMaker.MakeJob(
                MAPMechanitor_JobDefOf.MAP_MechFusionReleaseTransition,
                wearer, session.FusionApparel);
            return wearer.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        internal static bool IsMergeSourceHidden(Pawn pawn)
        {
            return !MechFusionRenderUtility.IsRenderingSourcePawn(pawn)
                && pawn.jobs?.curDriver is JobDriver_MechFusionApproach approach
                && approach.IsTransitionSourceHidden;
        }

        internal static bool IsReleasing(Pawn? pawn)
        {
            return pawn?.jobs?.curDriver is JobDriver_MechFusionReleaseTransition release
                && release.IsShowingHuman;
        }

        internal static void CancelReleaseTransition(MechFusionSession session)
        {
            if (session.WearerPawn?.jobs?.curDriver
                is JobDriver_MechFusionReleaseTransition release)
            {
                release.CancelVisualForSession(session);
            }
        }

        internal static void PlayCompletedFusionTransition(
            Map map, IntVec3 sourcePosition, Pawn wearer)
        {
            // 正式合体后 source 已离图，使用提交前捕获的位置播放入口效果。
            EffecterDefOf.Skip_EntryNoDelay.Spawn(sourcePosition, map).Cleanup();
            SoundDefOf.Psycast_Skip_Entry?.PlayOneShot(new TargetInfo(sourcePosition, map));
            SpawnSkipEffecters(null, wearer);
            PlaySkipSounds(null, wearer);
        }

        internal static void PlayFusionTransition(Pawn? source, Pawn? wearer)
        {
            FaceEachOther(source, wearer);
            SpawnSkipEffecters(source, wearer);
            PlaySkipSounds(source, wearer);
        }

        internal static void PlayTeardownTransition(Pawn? source, Pawn? wearer)
        {
            PlayFusionTransition(source, wearer);
        }

        internal static void FaceEachOther(Pawn? source, Pawn? wearer)
        {
            FaceTowards(source, wearer);
            FaceTowards(wearer, source);
        }

        private static void FaceTowards(Pawn? pawn, Pawn? target)
        {
            if (pawn?.Spawned != true
                || target?.Spawned != true
                || pawn.Map == null
                || pawn.Map != target.Map
                || pawn.Downed
                || pawn.InMentalState)
            {
                return;
            }

            IntVec3 delta = target.Position - pawn.Position;
            if (delta.x == 0 && delta.z == 0)
            {
                return;
            }

            pawn.Rotation = Rot4.FromAngleFlat(
                delta.ToVector3().AngleFlat());
        }

        private static void SpawnSkipEffecters(Pawn? source, Pawn? wearer)
        {
            if (source?.Spawned == true && source.Map != null)
            {
                EffecterDefOf.Skip_EntryNoDelay
                    .Spawn(source, source.Map)
                    .Cleanup();
            }

            if (wearer?.Spawned == true && wearer.Map != null)
            {
                EffecterDefOf.Skip_ExitNoDelay
                    .Spawn(wearer, wearer.Map)
                    .Cleanup();
            }
        }

        private static void PlaySkipSounds(Pawn? source, Pawn? wearer)
        {
            if (source?.Spawned == true
                && source.Map != null
                && SoundDefOf.Psycast_Skip_Entry != null)
            {
                SoundDefOf.Psycast_Skip_Entry.PlayOneShot(
                    new TargetInfo(source.Position, source.Map));
            }

            if (!ModsConfig.RoyaltyActive
                || wearer?.Spawned != true
                || wearer.Map == null
                || SoundDefOf.Psycast_Skip_Exit == null)
            {
                return;
            }

            SoundDefOf.Psycast_Skip_Exit.PlayOneShot(
                new TargetInfo(wearer.Position, wearer.Map));
        }
    }
}
