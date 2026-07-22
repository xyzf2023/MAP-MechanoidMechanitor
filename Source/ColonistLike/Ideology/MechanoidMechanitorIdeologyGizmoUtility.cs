using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MAP_MechanoidMechanitor.Scenarios;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// Partial/Full 下机械族机械师的意识形态切换 Gizmo。
    /// </summary>
    public static class MechanoidMechanitorIdeologyGizmoUtility
    {
        private const string ChangeIdeoLabelKey =
            "MAP_MechanoidMechanitor.Ideology.ChangeIdeo";
        private const string ChangeIdeoDescKey =
            "MAP_MechanoidMechanitor.Ideology.ChangeIdeoDesc";
        private const string CurrentIdeoSuffixKey =
            "MAP_MechanoidMechanitor.Ideology.CurrentIdeoSuffix";

        public static IEnumerable<Gizmo> GetGizmos(Pawn pawn)
        {
            if (!ModsConfig.IdeologyActive
                || !MechanoidMechanitorIdeologyAdaptationUtility.AllowsIdeologyMembership(pawn)
                || pawn.Faction != Faction.OfPlayer
                || Find.IdeoManager == null
                || Find.IdeoManager.classicMode)
            {
                yield break;
            }

            yield return new Command_Action
            {
                defaultLabel = ChangeIdeoLabelKey.Translate(),
                defaultDesc = ChangeIdeoDescKey.Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/ChangeIdeo", false)
                    ?? TexCommand.ForbidOff,
                action = () => OpenIdeoFloatMenu(pawn)
            };
        }

        private static void OpenIdeoFloatMenu(Pawn pawn)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            Ideo? current = pawn.Ideo;
            List<Ideo> ordered = BuildOrderedIdeoList(current);
            for (int i = 0; i < ordered.Count; i++)
            {
                Ideo ideo = ordered[i];
                bool isCurrent = ideo == current;
                string label = ideo.name;
                if (isCurrent)
                {
                    label = label + " " + CurrentIdeoSuffixKey.Translate();
                }

                FloatMenuOption option = new FloatMenuOption(
                    label,
                    isCurrent
                        ? null
                        : () =>
                        {
                            MechanoidMechanitorIdeologyAdaptationUtility.TrySetIdeo(
                                pawn,
                                ideo,
                                refreshFactionCounts: true);
                        },
                    ideo.Icon,
                    ideo.Color);
                options.Add(option);
            }

            if (options.Count == 0)
            {
                return;
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static List<Ideo> BuildOrderedIdeoList(Ideo? current)
        {
            List<Ideo> all = Find.IdeoManager.IdeosListForReading.ToList();
            Ideo? primary = Faction.OfPlayer?.ideos?.PrimaryIdeo;
            List<Ideo> minors = Faction.OfPlayer?.ideos?.IdeosMinorListForReading
                ?? new List<Ideo>();

            List<Ideo> ordered = new List<Ideo>(all.Count);
            HashSet<Ideo> seen = new HashSet<Ideo>();

            void TryAdd(Ideo? ideo)
            {
                if (ideo == null || !seen.Add(ideo))
                {
                    return;
                }

                ordered.Add(ideo);
            }

            TryAdd(primary);
            for (int i = 0; i < minors.Count; i++)
            {
                TryAdd(minors[i]);
            }

            List<Ideo> others = all
                .Where(ideo => !seen.Contains(ideo))
                .OrderBy(ideo => ideo.name)
                .ToList();
            for (int i = 0; i < others.Count; i++)
            {
                TryAdd(others[i]);
            }

            return ordered;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class MechanoidMechanitorIdeologyGizmoPatches
    {
        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (Gizmo gizmo in __result)
            {
                yield return gizmo;
            }

            if (__instance == null || !ModsConfig.IdeologyActive)
            {
                yield break;
            }

            foreach (Gizmo gizmo in MechanoidMechanitorIdeologyGizmoUtility.GetGizmos(__instance))
            {
                yield return gizmo;
            }
        }
    }
}
