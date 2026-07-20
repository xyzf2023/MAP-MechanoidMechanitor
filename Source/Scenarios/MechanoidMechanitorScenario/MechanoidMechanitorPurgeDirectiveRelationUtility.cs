using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class MechanoidMechanitorPurgeDirectiveRelationUtility
    {
        public static bool ShouldApplyNonHostileMechHiveRestrictions()
        {
            if (!GameComponent_MechanoidMechanitorStoryState.IsPurgeDirectiveActive)
            {
                return false;
            }

            Game? game = Current.Game;
            if (game == null)
            {
                return false;
            }

            GameComponent_MechanoidMechanitorStoryState? storyState =
                game.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            Faction? mechHive = storyState?.CachedMechHive;
            Faction? player = Faction.OfPlayerSilentFail;
            if (mechHive == null || player == null)
            {
                return true;
            }

            FactionRelation? playerRelation = player.RelationWith(mechHive, allowNull: true);
            FactionRelation? mechHiveRelation = mechHive.RelationWith(player, allowNull: true);
            return playerRelation?.kind != FactionRelationKind.Hostile
                && mechHiveRelation?.kind != FactionRelationKind.Hostile;
        }
    }
}
