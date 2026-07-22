using System.Collections.Generic;
using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// Basic：锁定开局主流文化，仍刷新次要文化列表与人数缓存。
    /// Partial/Full：使用统一成员源重算主流文化。
    /// </summary>
    [HarmonyPatch(
        typeof(FactionIdeosTracker),
        nameof(FactionIdeosTracker.RecalculateIdeosBasedOnPlayerPawns))]
    public static class MechanoidMechanitorIdeology_FactionIdeosTracker_Recalculate_Patch
    {
        private static readonly AccessTools.FieldRef<FactionIdeosTracker, Faction> FactionField =
            AccessTools.FieldRefAccess<FactionIdeosTracker, Faction>("faction");

        private static readonly AccessTools.FieldRef<FactionIdeosTracker, Ideo> PrimaryIdeoField =
            AccessTools.FieldRefAccess<FactionIdeosTracker, Ideo>("primaryIdeo");

        private static readonly AccessTools.FieldRef<FactionIdeosTracker, List<Ideo>> IdeosMinorField =
            AccessTools.FieldRefAccess<FactionIdeosTracker, List<Ideo>>("ideosMinor");

        [HarmonyPrefix]
        public static bool Prefix(FactionIdeosTracker __instance)
        {
            if (!ModsConfig.IdeologyActive)
            {
                return true;
            }

            MechanoidMechanitorIdeologyAdaptationLevel level =
                MechanoidMechanitorIdeologyAdaptationUtility.GetEffectiveLevel();
            if (level == MechanoidMechanitorIdeologyAdaptationLevel.Disabled)
            {
                return true;
            }

            Faction faction = FactionField(__instance);
            if (faction == null || !faction.IsPlayer)
            {
                return true;
            }

            if (Current.ProgramState != ProgramState.Playing
                || Find.WindowStack.IsOpen<Dialog_ConfigureIdeo>())
            {
                return false;
            }

            List<Ideo> ideosMinor = IdeosMinorField(__instance);
            Dictionary<Ideo, int> tmpPlayerIdeos = new Dictionary<Ideo, int>();
            ideosMinor.Clear();

            List<Pawn> members = MechanoidMechanitorIdeologyMemberUtility
                .GetEffectiveIdeologyMembers();
            for (int i = 0; i < members.Count; i++)
            {
                Pawn pawn = members[i];
                if (pawn.HomeFaction != Faction.OfPlayer && pawn.Faction != Faction.OfPlayer)
                {
                    continue;
                }

                Ideo? ideo = pawn.Ideo;
                if (ideo == null)
                {
                    continue;
                }

                if (tmpPlayerIdeos.TryGetValue(ideo, out int count))
                {
                    tmpPlayerIdeos[ideo] = count + 1;
                }
                else
                {
                    tmpPlayerIdeos[ideo] = 1;
                }
            }

            Ideo primaryIdeo = PrimaryIdeoField(__instance);

            if (level == MechanoidMechanitorIdeologyAdaptationLevel.Basic)
            {
                Ideo? locked = GameComponent_MechanoidMechanitorStoryState.GetLockedPrimaryIdeo();
                if (locked != null)
                {
                    PrimaryIdeoField(__instance) = locked;
                    primaryIdeo = locked;
                }
            }
            else
            {
                int bestCount = 0;
                Ideo? challenger = null;
                foreach (KeyValuePair<Ideo, int> pair in tmpPlayerIdeos)
                {
                    if (pair.Value > bestCount)
                    {
                        bestCount = pair.Value;
                        challenger = pair.Key;
                    }
                }

                int primaryCount = 0;
                if (primaryIdeo != null)
                {
                    tmpPlayerIdeos.TryGetValue(primaryIdeo, out primaryCount);
                }

                if (challenger != null && bestCount > primaryCount)
                {
                    if (primaryIdeo != null)
                    {
                        Find.LetterStack.ReceiveLetter(
                            "LetterLabelNewPrimaryIdeo".Translate(
                                primaryIdeo.Named("OLDIDEO"),
                                challenger.Named("NEWIDEO")),
                            "LetterNewPrimaryIdeo".Translate(
                                primaryIdeo.Named("OLDIDEO"),
                                challenger.Named("NEWIDEO"),
                                Faction.OfPlayer.Named("FACTION")),
                            LetterDefOf.NeutralEvent);
                        primaryIdeo.Notify_NotPrimaryAnymore(challenger);
                    }

                    if (primaryIdeo != null
                        && challenger.ColonistBelieverCountCached
                            < Ideo.MinBelieversToEnableObligations)
                    {
                        Find.LetterStack.ReceiveLetter(
                            "LetterTitleObligationsActivated".Translate(challenger),
                            "LetterTitleObligationsActivatedIdeoBecameMajor".Translate(
                                challenger.Named("IDEO")),
                            LetterDefOf.NeutralEvent);
                    }

                    PrimaryIdeoField(__instance) = challenger;
                    primaryIdeo = challenger;
                }
            }

            foreach (KeyValuePair<Ideo, int> pair in tmpPlayerIdeos)
            {
                if (pair.Key != primaryIdeo && !ideosMinor.Contains(pair.Key))
                {
                    ideosMinor.Add(pair.Key);
                }
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(Ideo), nameof(Ideo.RecacheColonistBelieverCount))]
    public static class MechanoidMechanitorIdeology_Ideo_RecacheColonistBelieverCount_Patch
    {
        private static readonly AccessTools.FieldRef<Ideo, int> ColonistBelieverCountCachedField =
            AccessTools.FieldRefAccess<Ideo, int>("colonistBelieverCountCached");

        [HarmonyPrefix]
        public static bool Prefix(Ideo __instance, ref int __result)
        {
            if (!ModsConfig.IdeologyActive)
            {
                return true;
            }

            if (!MechanoidMechanitorIdeologyAdaptationUtility.IsAtLeast(
                    MechanoidMechanitorIdeologyAdaptationLevel.Partial))
            {
                return true;
            }

            if (Current.ProgramState != ProgramState.Playing
                || Find.WindowStack.IsOpen<Dialog_ConfigureIdeo>())
            {
                __result = 0;
                return false;
            }

            int previous = ColonistBelieverCountCachedField(__instance);
            int count = 0;
            List<Pawn> members = MechanoidMechanitorIdeologyMemberUtility
                .GetEffectiveIdeologyMembers(
                    excludeCryptosleep: true,
                    excludeQuestLodgers: true);
            for (int i = 0; i < members.Count; i++)
            {
                Pawn pawn = members[i];
                if (pawn.Ideo == __instance && !pawn.IsSlave)
                {
                    count++;
                }
            }

            ColonistBelieverCountCachedField(__instance) = count;
            foreach (Precept precept in __instance.PreceptsListForReading)
            {
                if (precept is Precept_Role role)
                {
                    role.RecacheActivity();
                }
            }

            if (Faction.OfPlayer?.ideos != null
                && Faction.OfPlayer.ideos.IsMinor(__instance))
            {
                if (count < Ideo.MinBelieversToEnableObligations
                    && previous != -1
                    && previous >= Ideo.MinBelieversToEnableObligations)
                {
                    Find.LetterStack.ReceiveLetter(
                        "LetterTitleObligationsDeactivated".Translate(__instance.name),
                        "LetterTitleObligationsDeactivatedTooFewBelievers".Translate(
                            __instance.Named("IDEO"),
                            Ideo.MinBelieversToEnableObligations),
                        LetterDefOf.NeutralEvent);
                }

                if (count >= Ideo.MinBelieversToEnableObligations
                    && previous != -1
                    && previous < Ideo.MinBelieversToEnableObligations)
                {
                    Find.LetterStack.ReceiveLetter(
                        "LetterTitleObligationsActivated".Translate(__instance.name),
                        "LetterTitleObligationsActivatedEnoughBelievers".Translate(
                            __instance.Named("IDEO"),
                            Ideo.MinBelieversToEnableObligations),
                        LetterDefOf.NeutralEvent);
                }
            }

            __result = count;
            return false;
        }
    }
}
