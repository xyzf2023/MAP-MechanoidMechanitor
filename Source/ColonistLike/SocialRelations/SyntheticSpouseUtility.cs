using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>社交卡上的指定追求与主动分手入口，不直接分配配偶。</summary>
    public static class SyntheticSpouseUtility
    {
        private const string Key = SyntheticCompanionRelationshipUtility.Key;

        public static bool CanShowRelationshipButtons(Pawn pawn) =>
            SyntheticCompanionRelationshipUtility.HasModule(pawn)
            && pawn.Faction == Faction.OfPlayer && pawn.relations != null;

        public static void DrawRelationshipButtons(Rect rect, Pawn pawn)
        {
            Color previousColor = GUI.color;
            try
            {
                // 不继承社交列表的颜色；内部根据可用性着色，绘制后恢复。
                GUI.color = Color.white;
                DrawRelationshipButtonsInternal(rect, pawn);
            }
            finally { GUI.color = previousColor; }
        }

        private static void DrawRelationshipButtonsInternal(Rect rect, Pawn pawn)
        {
            List<Pawn> partners = SyntheticCompanionRelationshipUtility.GetPartners(pawn);
            if (partners.Count == 0)
            {
                string? reason = SyntheticRelationshipFeedback.PursuitMessage(pawn);
                List<FloatMenuOption>? options = null;
                if (reason == null)
                {
                    options = BuildPursuitOptions(pawn, out bool hasAvailableTarget);
                    if (!hasAvailableTarget) reason = (Key + "NoCandidate").Translate();
                }
                // 与原版一致：不可用时变暗，但允许点击以显示拒绝原因。
                GUI.color = reason == null ? Color.white : ColoredText.SubtleGrayColor;
                if (Widgets.ButtonText(rect, (Key + "Pursue").Translate()))
                {
                    if (reason == null && options != null) Find.WindowStack.Add(new FloatMenu(options));
                    else Messages.Message(reason, pawn, MessageTypeDefOf.RejectInput, historical: false);
                }
                TooltipHandler.TipRegion(rect, reason ?? (Key + "PursueDescription").Translate().ToString());
                return;
            }
            string label = partners.Count == 1 && pawn.relations.DirectRelationExists(PawnRelationDefOf.Spouse, partners[0])
                ? "Divorce" : "Breakup";
            if (Widgets.ButtonText(rect, (Key + label).Translate()))
            {
                if (partners.Count == 1) ConfirmEnd(pawn, partners[0]);
                else
                {
                    var options = new List<FloatMenuOption>();
                    foreach (Pawn partner in partners)
                    {
                        Pawn selected = partner;
                        options.Add(new FloatMenuOption(selected.LabelShortCap, () => ConfirmEnd(pawn, selected)));
                    }
                    Find.WindowStack.Add(new FloatMenu(options));
                }
            }
            TooltipHandler.TipRegion(rect, (Key + "EndDescription").Translate());
        }

        private static void ConfirmEnd(Pawn pawn, Pawn partner)
        {
            string stage = pawn.relations.DirectRelationExists(PawnRelationDefOf.Spouse, partner) ? "Divorce" : "Breakup";
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                (Key + stage + "Confirm").Translate(),
                () => SyntheticCompanionRelationshipUtility.TryEndRelationship(pawn, partner), destructive: true));
        }

        private static List<FloatMenuOption> BuildPursuitOptions(Pawn pawn, out bool hasAvailableTarget)
        {
            var options = new List<FloatMenuOption>();
            hasAvailableTarget = false;
            if (pawn.Map != null)
            {
                var candidates = new List<Pawn>(pawn.Map.mapPawns.FreeColonistsSpawned);
                candidates.Sort((a, b) => string.CompareOrdinal(a.LabelShort, b.LabelShort));
                foreach (Pawn target in candidates)
                {
                    if (target == pawn || !target.RaceProps.Humanlike || target.RaceProps.IsMechanoid) continue;
                    if (target.ageTracker != null && target.ageTracker.AgeBiologicalYearsFloat < 16f) continue;
                    Pawn selected = target;
                    string? reason = SyntheticRelationshipFeedback.PursuitMessage(pawn, target);
                    if (reason == null) hasAvailableTarget = true;
                    options.Add(reason == null
                        ? new FloatMenuOption(target.LabelShortCap, () => OrderPursuit(pawn, selected))
                        : new FloatMenuOption(target.LabelShortCap + ": " + reason, null));
                }
            }
            return options;
        }

        private static void OrderPursuit(Pawn pawn, Pawn target)
        {
            string? reason = SyntheticRelationshipFeedback.PursuitMessage(pawn, target);
            if (reason != null)
            {
                Messages.Message(reason, pawn, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            if (pawn.CurJobDef == MAPMechanitor_JobDefOf.MAP_SyntheticRomance && pawn.CurJob.targetA.Pawn == target) return;
            foreach (QueuedJob queued in pawn.jobs.jobQueue)
                if (queued.job.def == MAPMechanitor_JobDefOf.MAP_SyntheticRomance && queued.job.targetA.Pawn == target) return;
            Job job = JobMaker.MakeJob(MAPMechanitor_JobDefOf.MAP_SyntheticRomance, target);
            job.interaction = InteractionDefOf.RomanceAttempt;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }
    }
}
