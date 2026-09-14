using System;
using System.Globalization;
using MAP_MechanoidMechanitor;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor.Scenarios
{
    [StaticConstructorOnStartup]
    public static class MechanoidMechanitorMechHiveCommunicationUtility
    {
        private const string MechanoidFactionIconPath =
            "World/WorldObjects/Expanding/Mechanoids";

        private static readonly Texture2D cachedContactOvermindIcon =
            ContentFinder<Texture2D>.Get(MechanoidFactionIconPath);

        private static Color? cachedContactOvermindIconColor;

        public static string ContactOvermindLabel =>
            "MAP_MechanoidMechanitor.PurgeDirective.Communication.ContactOvermind".Translate();

        public static Texture2D ContactOvermindIcon => cachedContactOvermindIcon;

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
                || !GameComponent_MechanoidMechanitorStoryState.IsPurgeDirectiveActive)
            {
                return false;
            }

            GameComponent_MechanoidMechanitorStoryState? storyState =
                Current.Game.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            if (storyState == null)
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

        public static void TryOrderContactOvermindJob(Pawn? pawn, Building_CommsConsole? console)
        {
            if (!IsValidContactPawn(pawn) || console == null || !console.Spawned)
            {
                return;
            }

            if (pawn!.Map == null || console.Map == null || pawn.Map != console.Map)
            {
                return;
            }

            if (!console.CanUseCommsNow)
            {
                return;
            }

            if (!TryGetContactableMechHive(out _))
            {
                return;
            }

            Job job = JobMaker.MakeJob(
                MAPMechanitor_JobDefOf.MAP_ContactMechanoidOvermind,
                console);
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            PlayerKnowledgeDatabase.KnowledgeDemonstrated(
                ConceptDefOf.OpeningComms,
                KnowledgeAmount.Total);
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
                new Dialog_MechanoidOvermindCommunication(mechHive, preferredMap, pawn));
        }

        public static string ResolveContactPawnDisplayName(Pawn? pawn)
        {
            if (pawn == null)
            {
                return "?";
            }

            string label = pawn.LabelShortCap;
            if (!string.IsNullOrEmpty(label))
            {
                return label;
            }

            label = pawn.LabelCap;
            if (!string.IsNullOrEmpty(label))
            {
                return label;
            }

            return pawn.def?.label?.CapitalizeFirst() ?? "?";
        }

        public static string ResolveContactLocalTimeText()
        {
            try
            {
                return DateTime.Now.ToString(
                    "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "[MAP] Failed to read local time for mechanoid overmind dialogue: " + ex);
                return "MAP_MechanoidMechanitor.PurgeDirective.Communication.Dialogue.TimeUnavailable"
                    .Translate();
            }
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
