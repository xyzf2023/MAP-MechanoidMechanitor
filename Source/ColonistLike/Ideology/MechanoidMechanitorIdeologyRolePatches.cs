using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// Full 模式下文化角色候选、分配与失去角色心情安全。
    /// </summary>
    public static class MechanoidMechanitorIdeologyRolePatches
    {
        [HarmonyPatch]
        public static class Patch_Precept_Role_ValidatePawn
        {
            private static MethodBase? cachedTarget;

            private static bool Prepare()
            {
                return TargetMethod() != null;
            }

            private static MethodBase? TargetMethod()
            {
                return cachedTarget ??= AccessTools.Method(typeof(Precept_Role), "ValidatePawn");
            }

            [HarmonyPostfix]
            public static void Postfix(Precept_Role __instance, Pawn p, ref bool __result)
            {
                if (__result
                    || !ModsConfig.IdeologyActive
                    || !MechanoidMechanitorIdeologyAdaptationUtility
                        .CanServeAsIdeologyRoleOrRitualParticipant(p))
                {
                    return;
                }

                if (p.Destroyed || p.Dead || p.Faction == null || !p.Faction.IsPlayer)
                {
                    return;
                }

                __result = __instance.RequirementsMet(p);
            }
        }

        [HarmonyPatch(
            typeof(SocialCardUtility),
            nameof(SocialCardUtility.DrawPawnRoleSelection))]
        public static class Patch_DrawPawnRoleSelection_AllowMechanitor
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn pawn, Rect rect)
            {
                if (!ModsConfig.IdeologyActive
                    || !MechanoidMechanitorIdeologyAdaptationUtility
                        .CanServeAsIdeologyRoleOrRitualParticipant(pawn)
                    || pawn.IsFreeNonSlaveColonist)
                {
                    return true;
                }

                DrawRoleSelectionForFullMechanitor(pawn, rect);
                return false;
            }

            private static void DrawRoleSelectionForFullMechanitor(Pawn pawn, Rect rect)
            {
                if (pawn.Ideo == null)
                {
                    return;
                }

                Precept_Role? currentRole = pawn.Ideo.GetRole(pawn);
                Ideo? primaryIdeo = Faction.OfPlayer?.ideos?.PrimaryIdeo;
                Precept_Ritual? roleChangeRitual =
                    pawn.Ideo.GetPrecept(PreceptDefOf.RoleChange) as Precept_Ritual;
                if (roleChangeRitual?.targetFilter == null)
                {
                    return;
                }

                TargetInfo ritualTarget = roleChangeRitual.targetFilter.BestTarget(
                    pawn,
                    TargetInfo.Invalid);
                List<Precept_Role> roles = RitualUtility.AllRolesForPawn(pawn).ToList();
                bool enabled = roles.Count > 0;
                float y = rect.y + rect.height / 2f - 14f;
                Rect buttonRect = new Rect(rect.width - 150f, y, 140f, 28f)
                {
                    xMax = rect.width - 26f - 4f
                };

                if (!Widgets.ButtonText(
                        buttonRect,
                        "ChooseRole".Translate() + "...",
                        drawBackground: true,
                        doMouseoverSound: true,
                        enabled))
                {
                    return;
                }

                if (!ritualTarget.IsValid)
                {
                    Messages.Message(
                        (Find.IdeoManager.classicMode
                            ? "AbilityDisabledNoRitualSpot"
                            : "AbilityDisabledNoAltarIdeogramOrRitualsSpot").Translate(),
                        pawn,
                        MessageTypeDefOf.RejectInput);
                    return;
                }

                List<FloatMenuOption> options = new List<FloatMenuOption>();
                if (currentRole != null)
                {
                    options.Add(new FloatMenuOption(
                        "RemoveCurrentRole".Translate(),
                        () =>
                        {
                            Dialog_BeginRitual dialog =
                                (Dialog_BeginRitual)roleChangeRitual.GetRitualBeginWindow(
                                    ritualTarget,
                                    null,
                                    null,
                                    pawn,
                                    new Dictionary<string, Pawn> { { "role_changer", pawn } });
                            dialog.SetRoleToChangeTo(null);
                            Find.WindowStack.Add(dialog);
                        },
                        Widgets.PlaceholderIconTex,
                        Color.white));
                }

                for (int i = 0; i < roles.Count; i++)
                {
                    Precept_Role newRole = roles[i];
                    if (newRole == currentRole
                        || !newRole.Active
                        || !newRole.RequirementsMet(pawn)
                        || (newRole.def.leaderRole && pawn.Ideo != primaryIdeo))
                    {
                        continue;
                    }

                    string text = newRole.LabelForPawn(pawn).CapitalizeFirst();
                    if (!pawn.Ideo.classicMode)
                    {
                        text = text + " (" + newRole.def.label + ")";
                    }

                    options.Add(new FloatMenuOption(
                        text,
                        () =>
                        {
                            Dialog_BeginRitual dialog =
                                (Dialog_BeginRitual)roleChangeRitual.GetRitualBeginWindow(
                                    ritualTarget,
                                    null,
                                    null,
                                    pawn,
                                    new Dictionary<string, Pawn> { { "role_changer", pawn } });
                            dialog.SetRoleToChangeTo(newRole);
                            Find.WindowStack.Add(dialog);
                        },
                        newRole.Icon,
                        newRole.ideo.Color)
                    {
                        orderInPriority = newRole.def.displayOrderInImpact
                    });
                }

                if (options.Count > 0)
                {
                    Find.WindowStack.Add(new FloatMenu(options));
                }
            }
        }

        [HarmonyPatch(
            typeof(RitualRoleIdeoRoleChanger),
            nameof(RitualRoleIdeoRoleChanger.AppliesToPawn))]
        public static class Patch_RitualRoleIdeoRoleChanger_AppliesToPawn
        {
            [HarmonyPostfix]
            public static void Postfix(
                Pawn p,
                ref bool __result,
                ref string reason,
                bool skipReason)
            {
                if (__result
                    || !ModsConfig.IdeologyActive
                    || !MechanoidMechanitorIdeologyAdaptationUtility
                        .CanServeAsIdeologyRoleOrRitualParticipant(p))
                {
                    return;
                }

                if (p.Ideo == null)
                {
                    return;
                }

                if (p.Ideo.GetRole(p) == null
                    && !RitualUtility.AllRolesForPawn(p).Any(r => r.RequirementsMet(p)))
                {
                    if (!skipReason)
                    {
                        reason = "MessageRitualNoRolesAvailable".Translate(p);
                    }

                    return;
                }

                if (Faction.OfPlayer?.ideos == null || !Faction.OfPlayer.ideos.Has(p.Ideo))
                {
                    if (!skipReason)
                    {
                        reason = "MessageRitualNotOfPlayerIdeo".Translate(p);
                    }

                    return;
                }

                __result = true;
                reason = null!;
            }
        }

        [HarmonyPatch(
            typeof(RoleRequirement_SupremeGender),
            nameof(RoleRequirement_SupremeGender.Met))]
        public static class Patch_RoleRequirement_SupremeGender_Met
        {
            [HarmonyPostfix]
            public static void Postfix(Pawn pawn, ref bool __result)
            {
                if (__result
                    || !ModsConfig.IdeologyActive
                    || !MechanoidMechanitorIdeologyAdaptationUtility
                        .CanServeAsIdeologyRoleOrRitualParticipant(pawn))
                {
                    return;
                }

                if (pawn.gender == Gender.None)
                {
                    __result = true;
                }
            }
        }

        [HarmonyPatch(typeof(Precept_RoleSingle), nameof(Precept_RoleSingle.Assign))]
        public static class Patch_Precept_RoleSingle_Assign
        {
            [HarmonyPrefix]
            public static void Prefix(Precept_RoleSingle __instance, Pawn p, ref bool addThoughts)
            {
                if (!ModsConfig.IdeologyActive || !addThoughts)
                {
                    return;
                }

                Pawn? oldPawn = __instance.ChosenPawnValue;
                if (oldPawn == null || oldPawn.needs?.mood != null)
                {
                    return;
                }

                if (p != null)
                {
                    Find.LetterStack.ReceiveLetter(
                        "LetterLabelRoleLost".Translate(
                            oldPawn.Named("PAWN"),
                            __instance.Named("ROLE")),
                        "LetterRoleLostDesc".Translate(
                            oldPawn.Named("PAWN"),
                            __instance.Named("ROLE"))
                        + " "
                        + "LetterRoleLostReasonUnassignedDesc".Translate(
                            oldPawn.Named("PAWN")).CapitalizeFirst(),
                        LetterDefOf.NeutralEvent,
                        oldPawn);
                }

                addThoughts = false;
            }

            [HarmonyPostfix]
            public static void Postfix(Pawn p)
            {
                if (p == null
                    || !MechanoidMechanitorIdeologyAdaptationUtility
                        .AllowsIdeologyFullParticipation(p))
                {
                    return;
                }

                p.abilities ??= new Pawn_AbilityTracker(p);
                p.abilities.Notify_TemporaryAbilitiesChanged();
            }
        }

        [HarmonyPatch(typeof(Precept_RoleMulti), nameof(Precept_RoleMulti.Unassign))]
        public static class Patch_Precept_RoleMulti_Unassign
        {
            [HarmonyPrefix]
            public static void Prefix(Pawn p, ref bool generateThoughts)
            {
                if (!ModsConfig.IdeologyActive
                    || !generateThoughts
                    || p == null
                    || p.needs?.mood != null)
                {
                    return;
                }

                generateThoughts = false;
            }
        }
    }
}
