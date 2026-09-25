using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>仅在恋爱业务入口应用保护；原版关系增删、死亡清理和阶段转换保持可用。</summary>
    public static class SyntheticRelationshipPatches
    {
        private static bool InvolvesModule(Pawn a, Pawn b) =>
            SyntheticCompanionRelationshipUtility.HasModule(a) || SyntheticCompanionRelationshipUtility.HasModule(b);

        [HarmonyPatch(typeof(RelationsUtility), nameof(RelationsUtility.RomanceEligiblePair))]
        public static class VanillaRomanceMenu
        {
            public static bool Prefix(Pawn initiator, Pawn target, ref AcceptanceReport __result)
            {
                if (!InvolvesModule(initiator, target)) return true;
                __result = (SyntheticCompanionRelationshipUtility.Key + "UseModuleCommand").Translate().ToString();
                return false;
            }
        }

        [HarmonyPatch(typeof(Pawn_InteractionsTracker), nameof(Pawn_InteractionsTracker.TryInteractWith))]
        public static class InteractionGate
        {
            public static bool Prefix(Pawn ___pawn, Pawn recipient, InteractionDef intDef, ref bool __result)
            {
                if (!InvolvesModule(___pawn, recipient)) return true;
                bool allowed = true;
                if (intDef == InteractionDefOf.RomanceAttempt)
                    allowed = SyntheticCompanionRelationshipUtility.IsDirectedPursuit(___pawn, recipient);
                else if (intDef == InteractionDefOf.MarriageProposal)
                    allowed = SyntheticCompanionRelationshipUtility.ProposalReason(___pawn, recipient) == null;
                else if (SyntheticCompanionSocialUtility.IsCompanionInteraction(intDef))
                    allowed = SyntheticCompanionSocialUtility.CanTalk(___pawn, recipient, intDef);
                else if (intDef.Worker is InteractionWorker_Breakup)
                    allowed = !SyntheticCompanionRelationshipUtility.IsAutomaticEndBlocked(___pawn, recipient);
                if (!allowed) __result = false;
                return allowed;
            }
        }

        [HarmonyPatch(typeof(InteractionWorker_RomanceAttempt), nameof(InteractionWorker_RomanceAttempt.RandomSelectionWeight))]
        public static class NoRandomRomance
        {
            public static bool Prefix(Pawn initiator, Pawn recipient, ref float __result)
            {
                if (!InvolvesModule(initiator, recipient)) return true;
                __result = 0f;
                return false;
            }
        }

        [HarmonyPatch(typeof(InteractionWorker_RomanceAttempt), nameof(InteractionWorker_RomanceAttempt.SuccessChance))]
        public static class RomanceChance
        {
            public static bool Prefix(Pawn initiator, Pawn recipient, ref float __result)
            {
                if (!InvolvesModule(initiator, recipient)) return true;
                __result = SyntheticCompanionRelationshipUtility.IsDirectedPursuit(initiator, recipient) ? 1f : 0f;
                return false;
            }
        }

        [HarmonyPatch(typeof(InteractionWorker_RomanceAttempt), nameof(InteractionWorker_RomanceAttempt.Interacted))]
        public static class RomanceResult
        {
            internal static bool Prefix(Pawn initiator, Pawn recipient, out SyntheticRelationshipContext? __state,
                ref string letterText, ref string letterLabel, ref LetterDef letterDef, ref LookTargets lookTargets)
            {
                __state = null;
                if (!InvolvesModule(initiator, recipient)) return true;
                if (!SyntheticCompanionRelationshipUtility.IsDirectedPursuit(initiator, recipient))
                {
                    letterText = null!; letterLabel = null!; letterDef = null!; lookTargets = null!;
                    return false;
                }
                __state = SyntheticRelationshipContext.Enter(SyntheticRelationshipOperation.Romance, initiator, recipient);
                return true;
            }
            internal static Exception? Finalizer(Pawn initiator, Pawn recipient, Exception? __exception, SyntheticRelationshipContext? __state)
            {
                try
                {
                    // 即使后续信件等反馈抛错，已经建立的新关系也必须采用新关系默认设置。
                    if (__state != null && SyntheticCompanionRelationshipUtility.HasLoveRelation(initiator, recipient))
                        SyntheticCompanionRelationshipUtility.ResetForNewRelationship(initiator);
                }
                finally { __state?.Dispose(); }
                return __exception;
            }
        }

        [HarmonyPatch(typeof(InteractionWorker_RomanceAttempt), "BreakLoverAndFianceRelations")]
        public static class PreserveExistingRomances
        {
            public static bool Prefix(Pawn pawn, ref List<Pawn> oldLoversAndFiances)
            {
                var context = SyntheticRelationshipContext.Current;
                if (context?.Operation != SyntheticRelationshipOperation.Romance || !context.Contains(pawn)) return true;
                oldLoversAndFiances = new List<Pawn>();
                return false;
            }
        }

        [HarmonyPatch(typeof(LovePartnerRelationUtility), nameof(LovePartnerRelationUtility.ExistingLeastLikedPawnWithRelation))]
        public static class ExcludeProtectedOldLovers
        {
            public static void Prefix(Pawn p, ref Func<DirectPawnRelation, bool> validator)
            {
                if (!SyntheticCompanionRelationshipUtility.HasProtectedPartner(p)) return;
                var original = validator;
                validator = r => original(r) && !SyntheticCompanionRelationshipUtility.IsProtectedPair(p, r.otherPawn);
            }
        }

        [HarmonyPatch(typeof(InteractionWorker_RomanceAttempt), nameof(InteractionWorker_RomanceAttempt.CanCreatePsychicBondBetween))]
        public static class PsychicBondRequiresGeneTrackers
        {
            public static bool Prefix(Pawn initiator, Pawn recipient, ref bool __result)
            {
                // 原版成功信件会直接解引用 initiator.genes；模块本身不赋予基因 Tracker。
                if (!InvolvesModule(initiator, recipient) || (initiator.genes != null && recipient.genes != null)) return true;
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(SocialInteractionUtility), nameof(SocialInteractionUtility.CanInitiateInteraction))]
        public static class ModuleVoice
        {
            public static bool Prefix(Pawn pawn, InteractionDef interactionDef, ref bool __result)
            {
                if (!SyntheticCompanionRelationshipUtility.HasModule(pawn)
                    || (interactionDef != InteractionDefOf.RomanceAttempt && interactionDef != InteractionDefOf.MarriageProposal
                        && !SyntheticCompanionSocialUtility.IsCompanionInteraction(interactionDef)))
                    return true;
                __result = SyntheticCompanionRelationshipUtility.CanAct(pawn) && pawn.interactions != null
                    && !pawn.IsInteractionBlocked(interactionDef, isInitiator: true, isRandom: false);
                return false;
            }
        }

        [HarmonyPatch(typeof(Pawn_InteractionsTracker), "CurrentSocialMode", MethodType.Getter)]
        public static class ProposalSocialMode
        {
            public static bool Prefix(Pawn ___pawn, ref RandomSocialMode __result)
            {
                if (!SyntheticCompanionRelationshipUtility.HasModule(___pawn)) return true;
                __result = SyntheticCompanionRelationshipUtility.CanAct(___pawn)
                    ? ___pawn.jobs.curDriver?.DesiredSocialMode() ?? RandomSocialMode.Normal : RandomSocialMode.Off;
                PawnDuty? duty = ___pawn.mindState?.duty;
                if (duty != null && duty.SocialModeMax < __result) __result = duty.SocialModeMax;
                return false;
            }
        }

        [HarmonyPatch(typeof(Pawn_InteractionsTracker), "TryInteractRandomly")]
        public static class NaturalCompanionInteraction
        {
            public static bool Prefix(Pawn ___pawn, ref bool __result)
            {
                if (!SyntheticCompanionRelationshipUtility.HasModule(___pawn)) return true;
                // 外层原版 Tracker 保留社交模式、320 tick 间隔和随机互动时钟。
                // 消耗本次机会，即使选择“不互动”也不能让 wantsRandomInteract 每 91 tick 重抽。
                __result = true;
                if (!SyntheticCompanionRelationshipUtility.CanAct(___pawn) || ___pawn.interactions.InteractedTooRecentlyToInteract())
                    return false;
                foreach (Pawn other in SyntheticCompanionRelationshipUtility.GetPartners(___pawn))
                {
                    if (SyntheticCompanionRelationshipUtility.ProposalReason(___pawn, other) == null)
                    {
                        float weight = SyntheticCompanionRelationshipUtility.ProposalWeight(___pawn, other);
                        if (Rand.Chance(weight / (1f + weight)))
                        {
                            ___pawn.interactions.TryInteractWith(other, InteractionDefOf.MarriageProposal);
                            return false;
                        }
                    }
                    // 不增加第二套时钟；未选择求婚时，尝试原版日常交谈。
                    if (SyntheticCompanionSocialUtility.TryTalk(___pawn, other)) break;
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(InteractionWorker_MarriageProposal), nameof(InteractionWorker_MarriageProposal.RandomSelectionWeight))]
        public static class ProposalWeight
        {
            public static bool Prefix(Pawn initiator, Pawn recipient, ref float __result)
            {
                if (!InvolvesModule(initiator, recipient)) return true;
                __result = SyntheticCompanionRelationshipUtility.ProposalWeight(initiator, recipient);
                return false;
            }
        }

        [HarmonyPatch(typeof(InteractionWorker_MarriageProposal), nameof(InteractionWorker_MarriageProposal.AcceptanceChance))]
        public static class ProposalAcceptance
        {
            public static bool Prefix(Pawn initiator, Pawn recipient, ref float __result)
            {
                if (!InvolvesModule(initiator, recipient)) return true;
                __result = SyntheticCompanionRelationshipUtility.IsProtectedPair(initiator, recipient) ? 1f : 0f;
                return false;
            }
        }

        internal sealed class MarriageState : IDisposable
        {
            private readonly Pawn first, second;
            private readonly MarriageNameChange firstName, secondName;
            private readonly SyntheticRelationshipContext? context;
            public MarriageState(Pawn first, Pawn second, bool ceremony)
            {
                this.first = first; this.second = second;
                firstName = first.relations.nextMarriageNameChange;
                secondName = second.relations.nextMarriageNameChange;
                first.relations.nextMarriageNameChange = MarriageNameChange.NoChange;
                second.relations.nextMarriageNameChange = MarriageNameChange.NoChange;
                if (ceremony) context = SyntheticRelationshipContext.Enter(SyntheticRelationshipOperation.Marriage, first, second);
            }
            public void Dispose()
            {
                first.relations.nextMarriageNameChange = firstName;
                second.relations.nextMarriageNameChange = secondName;
                context?.Dispose();
            }
        }

        [HarmonyPatch(typeof(InteractionWorker_MarriageProposal), nameof(InteractionWorker_MarriageProposal.Interacted))]
        public static class ProposalResult
        {
            internal static bool Prefix(Pawn initiator, Pawn recipient, out MarriageState? __state,
                ref string letterText, ref string letterLabel, ref LetterDef letterDef, ref LookTargets lookTargets)
            {
                __state = null;
                if (!InvolvesModule(initiator, recipient)) return true;
                if (SyntheticCompanionRelationshipUtility.ProposalReason(initiator, recipient) != null)
                {
                    letterText = null!; letterLabel = null!; letterDef = null!; lookTargets = null!;
                    return false;
                }
                __state = new MarriageState(initiator, recipient, false);
                return true;
            }
            internal static Exception? Finalizer(Exception? __exception, MarriageState? __state)
            {
                __state?.Dispose();
                return __exception;
            }
        }

        [HarmonyPatch(typeof(MarriageCeremonyUtility), nameof(MarriageCeremonyUtility.Married))]
        public static class MarriageResult
        {
            internal static void Prefix(Pawn firstPawn, Pawn secondPawn, out MarriageState? __state)
            {
                __state = SyntheticCompanionRelationshipUtility.IsProtectedPair(firstPawn, secondPawn)
                    ? new MarriageState(firstPawn, secondPawn, true) : null;
            }
            internal static Exception? Finalizer(Exception? __exception, MarriageState? __state)
            {
                __state?.Dispose();
                return __exception;
            }
        }

        [HarmonyPatch(typeof(PawnRelationWorker_Fiance), nameof(PawnRelationWorker_Fiance.OnRelationCreated))]
        public static class EngagementName
        {
            public static bool Prefix(Pawn firstPawn, Pawn secondPawn)
            {
                if (!InvolvesModule(firstPawn, secondPawn)) return true;
                // OnRelationCreated 在求婚结算内部调用，会覆盖求婚入口设置的改名策略。
                firstPawn.relations.nextMarriageNameChange = MarriageNameChange.NoChange;
                secondPawn.relations.nextMarriageNameChange = MarriageNameChange.NoChange;
                return false;
            }
        }

        [HarmonyPatch(typeof(SpouseRelationUtility), nameof(SpouseRelationUtility.GetSpouseCount))]
        public static class MechanicalSpouseCount
        {
            public static bool Prefix(Pawn pawn, bool includeDead, ref int __result)
            {
                if (!SyntheticCompanionRelationshipUtility.HasModule(pawn)) return true;
                __result = pawn.relations.GetDirectRelationsCount(PawnRelationDefOf.Spouse, p => includeDead || !p.Dead);
                return false;
            }
        }

        [HarmonyPatch(typeof(LovePartnerRelationUtility), nameof(LovePartnerRelationUtility.ChangeSpouseRelationsToExSpouse))]
        public static class PreserveSpouses
        {
            public static bool Prefix(Pawn pawn)
            {
                var context = SyntheticRelationshipContext.Current;
                if (context?.Operation == SyntheticRelationshipOperation.Marriage && context.Contains(pawn)) return false;
                bool hasProtected = false;
                foreach (DirectPawnRelation relation in pawn.relations.DirectRelations)
                    if (relation.def == PawnRelationDefOf.Spouse && SyntheticCompanionRelationshipUtility.IsProtectedPair(pawn, relation.otherPawn))
                    { hasProtected = true; break; }
                if (!hasProtected) return true;
                var spouses = new List<Pawn>(pawn.GetSpouses(includeDead: true));
                for (int i = spouses.Count - 1; i >= 0; i--)
                {
                    Pawn spouse = spouses[i];
                    if (SyntheticCompanionRelationshipUtility.IsProtectedPair(pawn, spouse)) continue;
                    var ev = new HistoryEvent(pawn.GetHistoryEventForSpouseCountPlusOne(), pawn.Named(HistoryEventArgsNames.Doer));
                    if (spouse.Dead || !ev.DoerWillingToDo())
                    {
                        pawn.relations.RemoveDirectRelation(PawnRelationDefOf.Spouse, spouse);
                        pawn.relations.AddDirectRelation(PawnRelationDefOf.ExSpouse, spouse);
                    }
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(InteractionWorker_Breakup), nameof(InteractionWorker_Breakup.RandomSelectionWeight))]
        public static class BreakupWeight
        {
            public static bool Prefix(Pawn initiator, Pawn recipient, ref float __result)
            {
                if (!SyntheticCompanionRelationshipUtility.IsProtectedPair(initiator, recipient)) return true;
                __result = 0f;
                return false;
            }
        }

        [HarmonyPatch(typeof(InteractionWorker_Breakup), nameof(InteractionWorker_Breakup.Interacted))]
        public static class BreakupResult
        {
            public static bool Prefix(Pawn initiator, Pawn recipient,
                ref string letterText, ref string letterLabel, ref LetterDef letterDef, ref LookTargets lookTargets)
            {
                if (!SyntheticCompanionRelationshipUtility.IsAutomaticEndBlocked(initiator, recipient)) return true;
                letterText = null!; letterLabel = null!; letterDef = null!; lookTargets = null!;
                return false;
            }
        }

        [HarmonyPatch(typeof(SpouseRelationUtility), nameof(SpouseRelationUtility.DoDivorce))]
        public static class AutomaticDivorce
        {
            public static bool Prefix(Pawn initiator, Pawn recipient) =>
                !SyntheticCompanionRelationshipUtility.IsAutomaticEndBlocked(initiator, recipient);
        }

        [HarmonyPatch(typeof(SpouseRelationUtility), nameof(SpouseRelationUtility.RemoveSpousesAsForbiddenByIdeo))]
        public static class IdeologyCleanup
        {
            internal static void Prefix(Pawn pawn, out SyntheticRelationshipContext __state) =>
                __state = SyntheticRelationshipContext.Enter(SyntheticRelationshipOperation.IdeologyCleanup, pawn, null);
            internal static Exception? Finalizer(Exception? __exception, SyntheticRelationshipContext __state)
            {
                __state?.Dispose();
                return __exception;
            }
        }

        [HarmonyPatch(typeof(SpouseRelationUtility), nameof(SpouseRelationUtility.GetLeastLikedSpouseRelation))]
        public static class IdeologyDivorceCandidate
        {
            public static bool Prefix(Pawn pawn, ref DirectPawnRelation __result)
            {
                var context = SyntheticRelationshipContext.Current;
                if (context?.Operation != SyntheticRelationshipOperation.IdeologyCleanup || context.First != pawn
                    || !SyntheticCompanionRelationshipUtility.HasProtectedPartner(pawn)) return true;
                DirectPawnRelation? candidate = null;
                int opinion = int.MaxValue;
                foreach (DirectPawnRelation relation in pawn.relations.DirectRelations)
                {
                    if (relation.def != PawnRelationDefOf.Spouse || relation.otherPawn == null || relation.otherPawn.Dead
                        || SyntheticCompanionRelationshipUtility.IsProtectedPair(pawn, relation.otherPawn)) continue;
                    int next = pawn.relations.OpinionOf(relation.otherPawn);
                    if (candidate == null || next < opinion) { candidate = relation; opinion = next; }
                }
                __result = candidate!;
                return false;
            }
        }

        public static bool HasOpinionData(Pawn pawn) => pawn.RaceProps.Humanlike
            || SyntheticCompanionRelationshipUtility.HasModule(pawn);

        [HarmonyPatch]
        public static class OpinionEligibility
        {
            public static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.OpinionOf));
                yield return AccessTools.Method(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.OpinionExplanation));
            }
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
            {
                var codes = new List<CodeInstruction>(instructions);
                MethodInfo race = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.RaceProps));
                MethodInfo humanlike = AccessTools.PropertyGetter(typeof(RaceProperties), nameof(RaceProperties.Humanlike));
                MethodInfo replacement = AccessTools.Method(typeof(SyntheticRelationshipPatches), nameof(HasOpinionData));
                int replaced = 0;
                for (int i = 0; i < codes.Count - 1; i++)
                    if (codes[i].Calls(race) && codes[i + 1].Calls(humanlike)) replaced++;
                if (replaced != 2)
                {
                    Log.Error("[MAP] 仿生伴侣好感资格补丁结构变化，保持原方法：" + __originalMethod.Name);
                    return codes;
                }
                for (int i = 0; i < codes.Count - 1; i++)
                {
                    if (!codes[i].Calls(race) || !codes[i + 1].Calls(humanlike)) continue;
                    codes[i].opcode = OpCodes.Nop; codes[i].operand = null;
                    codes[i + 1].opcode = OpCodes.Call; codes[i + 1].operand = replacement;
                }
                return codes;
            }
        }
    }
}
