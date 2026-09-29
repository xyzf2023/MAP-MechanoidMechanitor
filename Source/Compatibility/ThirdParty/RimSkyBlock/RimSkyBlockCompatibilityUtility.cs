using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty.RimSkyBlock
{
    internal static class RimSkyBlockCompatibilityUtility
    {
        internal const string PackageId = "Yexiaoyang.RimSkyBlock";
        internal const string CoronationDefName = "RSB_CoronationRitual";

        // 仅查询正式身份；自律资格和普通机械体的固定工作技能不构成帝国代表资格。
        internal static bool IsPlayerMechanitor(Pawn? pawn)
        {
            return pawn != null && !pawn.Dead && !pawn.Destroyed
                && pawn.RaceProps.IsMechanoid && pawn.Faction == Faction.OfPlayer
                && !pawn.IsPrisoner && !pawn.IsSlave && pawn.HostFaction == null
                && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(pawn);
        }

        internal static bool CanReceiveTitle(Pawn? pawn)
        {
            return IsPlayerMechanitor(pawn)
                && MechanoidMechanitorRoyaltyUtility.IsRoyaltyEligibleMechanitor(pawn);
        }

        internal static bool CanParticipateInRitual(Pawn? pawn)
        {
            return IsPlayerMechanitor(pawn) && ModsConfig.IdeologyActive
                && MechanoidMechanitorIdeologyAdaptationUtility
                    .CanServeAsIdeologyRoleOrRitualParticipant(pawn);
        }

        internal static List<Pawn> AppendMapMechanitors(
            List<Pawn> source, MapPawns mapPawns, bool ritualParticipants)
        {
            List<Pawn>? result = null;
            foreach (Pawn pawn in GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors)
            {
                if (!(ritualParticipants ? CanParticipateInRitual(pawn) : CanReceiveTitle(pawn))
                    || !pawn.Spawned || pawn.Map?.mapPawns != mapPawns
                    || (result ?? source).Contains(pawn))
                {
                    continue;
                }

                // 不向原版、剧本兼容或性能 MOD 的共享缓存列表写入。
                result ??= new List<Pawn>(source);
                result.Add(pawn);
            }
            return result ?? source;
        }
    }
}
