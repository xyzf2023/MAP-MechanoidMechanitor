using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class MechanoidMechanitorMechHiveCommunicationUtility
    {
        private const string MechanoidFactionIconPath =
            "World/WorldObjects/Expanding/Mechanoids";

        private static Texture2D? cachedContactOvermindIcon;

        private static Color? cachedContactOvermindIconColor;

        public static string ContactOvermindLabel =>
            "MAP_MechanoidMechanitor.MechHiveCommunication.ContactOvermind".Translate();

        public static Texture2D ContactOvermindIcon
        {
            get
            {
                if (cachedContactOvermindIcon == null)
                {
                    cachedContactOvermindIcon =
                        ContentFinder<Texture2D>.Get(MechanoidFactionIconPath);
                }

                return cachedContactOvermindIcon;
            }
        }

        public static Color ContactOvermindIconColor
        {
            get
            {
                if (!cachedContactOvermindIconColor.HasValue)
                {
                    Faction? mechanoidFaction =
                        Find.FactionManager?.FirstFactionOfDef(FactionDefOf.Mechanoid);
                    if (mechanoidFaction == null)
                    {
                        return Color.white;
                    }

                    cachedContactOvermindIconColor = mechanoidFaction.Color;
                }

                return cachedContactOvermindIconColor.Value;
            }
        }

        public static bool IsValidContactPawn(Pawn? pawn)
        {
            return pawn != null
                && !pawn.Dead
                && !pawn.Destroyed
                && pawn.Faction != null
                && pawn.Faction.IsPlayerSafe();
        }

        public static bool TryGetContactableMechHive(out Faction mechHive)
        {
            mechHive = null!;
            if (Current.Game == null
                || !GameComponent_MechanoidMechanitorScenarioState.IsEnabled)
            {
                return false;
            }

            GameComponent_MechanoidMechanitorStoryState? storyState =
                Current.Game.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            if (storyState == null || !storyState.PurgeDirectiveEnabled)
            {
                return false;
            }

            Faction? cachedMechHive = storyState.CachedMechHive;
            if (cachedMechHive == null || !storyState.IsCurrentMechHive(cachedMechHive))
            {
                return false;
            }

            Faction? player = Faction.OfPlayerSilentFail;
            if (player == null)
            {
                return false;
            }

            if (!AreMutualAllies(player, cachedMechHive))
            {
                return false;
            }

            mechHive = cachedMechHive;
            return true;
        }

        public static void TryOpenContactOvermindDialog(Pawn? pawn, Map? preferredMap = null)
        {
            if (!IsValidContactPawn(pawn))
            {
                return;
            }

            if (!TryGetContactableMechHive(out Faction mechHive))
            {
                return;
            }

            Find.WindowStack.Add(
                new Dialog_MechanoidOvermindCommunication(mechHive, preferredMap));
        }

        private static bool AreMutualAllies(Faction player, Faction mechHive)
        {
            FactionRelation? playerRelation = player.RelationWith(mechHive, allowNull: true);
            FactionRelation? mechRelation = mechHive.RelationWith(player, allowNull: true);
            return playerRelation != null
                && mechRelation != null
                && playerRelation.kind == FactionRelationKind.Ally
                && mechRelation.kind == FactionRelationKind.Ally;
        }
    }
}
