using RimWorld;
using Verse;
using Verse.Sound;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体折跃表现的唯一入口。只负责朝向、原版 Skip 视觉与安全的声音播放；
    /// 不创建生效中的 Effecter，也不修改合体事务状态。
    /// Skip 视觉 Effecter 全部来自 Core；Psycast_Skip_Exit 声音带
    /// MayRequireRoyalty，必须确认 Royalty 激活且 Def 可用后再调用。
    /// </summary>
    internal static class MechFusionVisualUtility
    {
        internal static void PlayFusionTransition(Pawn? source, Pawn? wearer)
        {
            FaceEachOther(source, wearer);
            SpawnSkipEffecters(source, wearer);
            PlaySkipSounds(source, wearer);
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
