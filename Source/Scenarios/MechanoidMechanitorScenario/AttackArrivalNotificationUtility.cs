using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    /// <summary>
    /// 节点与前哨共用原版攻击抵达的地图通知；外交转敌仍由各自入口负责。
    /// </summary>
    internal static class AttackArrivalNotificationUtility
    {
        internal static void PrepareLetter(
            Map map, bool newMap, bool arrivedByTransporter,
            ref TaggedString label, ref TaggedString text)
        {
            if (!newMap)
            {
                return;
            }

            Find.TickManager.Notify_GeneratedPotentiallyHostileMap();
            PawnRelationUtility.Notify_PawnsSeenByPlayer_Letter(
                map.mapPawns.AllPawns, ref label, ref text,
                (arrivedByTransporter
                    ? "LetterRelatedPawnsInMapWherePlayerLanded"
                    : "LetterRelatedPawnsSettlement").Translate(Faction.OfPlayer.def.pawnsPlural),
                informEvenIfSeenBefore: true);
        }

        internal static void NotifyEntered()
        {
            Find.GoodwillSituationManager.RecalculateAll(canSendHostilityChangedLetter: true);
        }
    }
}
