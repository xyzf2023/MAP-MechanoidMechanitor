using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [HarmonyPatch(typeof(Dialog_BillConfig), "GeneratePawnRestrictionOptions")]
    public static class JusticeMechGestatorBillPatches
    {
        [HarmonyPostfix]
        public static void Postfix(
            Bill_Production ___bill,
            ref IEnumerable<Widgets.DropdownMenuElement<Pawn>> __result)
        {
            if (!ModsConfig.BiotechActive)
            {
                return;
            }

            Bill_Production? bill = ___bill;

            if (bill == null
                || bill.recipe == null
                || !bill.recipe.mechanitorOnlyRecipe
                || bill.billStack?.billGiver is not Building_MechGestator)
            {
                return;
            }

            WorkGiverDef? workGiver = bill.billStack.billGiver.GetWorkgiver();
            if (workGiver == null)
            {
                return;
            }

            List<Widgets.DropdownMenuElement<Pawn>> options = __result.ToList();
            HashSet<Pawn> existingPayloads = new HashSet<Pawn>();
            foreach (Widgets.DropdownMenuElement<Pawn> element in options)
            {
                if (element.payload != null)
                {
                    existingPayloads.Add(element.payload);
                }
            }

            SkillDef? workSkill = bill.recipe.workSkill;

            IOrderedEnumerable<Pawn> candidates = Find.Maps
                .SelectMany(map => map.mapPawns.AllPawnsSpawned)
                .Where(pawn => IsCandidate(pawn) && !existingPayloads.Contains(pawn))
                .OrderBy(pawn => pawn.WorkTypeIsDisabled(workGiver.workType))
                .ThenByDescending(pawn => pawn.workSettings!.WorkIsActive(workGiver.workType));

            if (workSkill != null)
            {
                candidates = candidates.ThenByDescending(pawn => pawn.skills?.GetSkill(workSkill).Level ?? 0);
            }

            IEnumerable<Pawn> sortedCandidates = candidates.ThenBy(pawn => pawn.LabelShortCap);

            foreach (Pawn pawn in sortedCandidates)
            {
                options.Add(BuildMenuElementForPawn(bill, pawn, workGiver));
            }

            __result = options;
        }

        private static bool IsCandidate(Pawn? pawn)
        {
            if (pawn == null || !ModsConfig.BiotechActive)
            {
                return false;
            }

            if (!pawn.Spawned || pawn.Dead)
            {
                return false;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                return false;
            }

            if (!MAPMechanitorNodeUtility.IsMechanitorNodeController(pawn))
            {
                return false;
            }

            if (MAPMechanitorNodeUtility.RequiresExternalOverseer(pawn))
            {
                return false;
            }

            if (pawn.mechanitor == null)
            {
                return false;
            }

            if (!MechanitorUtility.IsMechanitor(pawn))
            {
                return false;
            }

            if (pawn.workSettings == null)
            {
                return false;
            }

            return true;
        }

        private static Widgets.DropdownMenuElement<Pawn> BuildMenuElementForPawn(
            Bill_Production bill,
            Pawn pawn,
            WorkGiverDef workGiver)
        {
            if (MAPMechGestatorRecipeUtility.IsPawnDisabledForGestationRecipe(bill.recipe, pawn))
            {
                string reasonKey = MAPMechGestatorRecipeUtility.GetDisabledReasonKey(bill.recipe);

                return new Widgets.DropdownMenuElement<Pawn>
                {
                    option = new FloatMenuOption(
                        string.Format("{0} ({1})", pawn.LabelShortCap, reasonKey.Translate()),
                        null),
                    payload = pawn
                };
            }

            if (pawn.WorkTypeIsDisabled(workGiver.workType))
            {
                return new Widgets.DropdownMenuElement<Pawn>
                {
                    option = new FloatMenuOption(
                        string.Format("{0} ({1})", pawn.LabelShortCap, "WillNever".Translate(workGiver.label)),
                        null),
                    payload = pawn
                };
            }

            if (bill.recipe.workSkill != null && pawn.skills == null)
            {
                return new Widgets.DropdownMenuElement<Pawn>
                {
                    option = new FloatMenuOption(
                        string.Format(
                            "{0} ({1})",
                            pawn.LabelShortCap,
                            "MAP_MechanoidMechanitor.Bill.Reason.MissingSkillsTracker".Translate()),
                        null),
                    payload = pawn
                };
            }

            if (bill.recipe.workSkill != null && !pawn.workSettings!.WorkIsActive(workGiver.workType))
            {
                return new Widgets.DropdownMenuElement<Pawn>
                {
                    option = new FloatMenuOption(
                        string.Format(
                            "{0} ({1} {2}, {3})",
                            pawn.LabelShortCap,
                            pawn.skills!.GetSkill(bill.recipe.workSkill).Level,
                            bill.recipe.workSkill.label,
                            "NotAssigned".Translate()),
                        delegate
                        {
                            bill.SetPawnRestriction(pawn);
                        }),
                    payload = pawn
                };
            }

            if (!pawn.workSettings!.WorkIsActive(workGiver.workType))
            {
                return new Widgets.DropdownMenuElement<Pawn>
                {
                    option = new FloatMenuOption(
                        string.Format("{0} ({1})", pawn.LabelShortCap, "NotAssigned".Translate()),
                        delegate
                        {
                            bill.SetPawnRestriction(pawn);
                        }),
                    payload = pawn
                };
            }

            if (bill.recipe.workSkill != null)
            {
                return new Widgets.DropdownMenuElement<Pawn>
                {
                    option = new FloatMenuOption(
                        string.Format(
                            "{0} ({1} {2})",
                            pawn.LabelShortCap,
                            pawn.skills!.GetSkill(bill.recipe.workSkill).Level,
                            bill.recipe.workSkill.label),
                        delegate
                        {
                            bill.SetPawnRestriction(pawn);
                        }),
                    payload = pawn
                };
            }

            return new Widgets.DropdownMenuElement<Pawn>
            {
                option = new FloatMenuOption(
                    pawn.LabelShortCap,
                    delegate
                    {
                        bill.SetPawnRestriction(pawn);
                    }),
                payload = pawn
            };
        }
    }
}
