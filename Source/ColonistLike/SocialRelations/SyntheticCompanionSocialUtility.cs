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
    /// <summary>使用机械体现有随机社交机会，与伴侣双向执行原版闲聊和深入交谈。</summary>
    public static class SyntheticCompanionSocialUtility
    {
        private static bool skillSafetyReady;

        public static bool IsCompanionInteraction(InteractionDef? interaction) =>
            interaction == InteractionDefOf.Chitchat || interaction == InteractionDefOf.DeepTalk;

        public static bool CanTalk(Pawn first, Pawn second, InteractionDef interaction)
        {
            if (!IsCompanionInteraction(interaction)
                || !SyntheticCompanionRelationshipUtility.IsProtectedPair(first, second)
                || !SyntheticCompanionRelationshipUtility.CanAct(first)
                || !SyntheticCompanionRelationshipUtility.CanAct(second)
                || first.Map != second.Map || first.Faction != Faction.OfPlayer || second.Faction != Faction.OfPlayer
                || first.HostileTo(second) || first.interactions == null || second.interactions == null
                || first.interactions.InteractedTooRecentlyToInteract() || second.interactions.InteractedTooRecentlyToInteract())
                return false;
            if (!skillSafetyReady && first.skills == null) return false;
            if (first.jobs.curDriver?.DesiredSocialMode() == RandomSocialMode.Off
                || second.jobs.curDriver?.DesiredSocialMode() == RandomSocialMode.Off
                || first.mindState?.duty?.SocialModeMax == RandomSocialMode.Off
                || second.mindState?.duty?.SocialModeMax == RandomSocialMode.Off
                || first.IsInteractionBlocked(interaction, isInitiator: true, isRandom: true)
                || second.IsInteractionBlocked(interaction, isInitiator: false, isRandom: true))
                return false;
            if (!SyntheticCompanionRelationshipUtility.HasModule(first)
                && !SocialInteractionUtility.CanInitiateRandomInteraction(first)) return false;
            if (!SyntheticCompanionRelationshipUtility.HasModule(second)
                && !SocialInteractionUtility.CanReceiveRandomInteraction(second)) return false;
            return first.interactions.CanInteractNowWith(second, interaction);
        }

        internal static bool TryTalk(Pawn mech, Pawn partner)
        {
            // 共用一次机会，不额外给人类增加独立计时器，也不放开全部随机互动。
            Pawn first = Rand.Bool ? mech : partner;
            Pawn second = first == mech ? partner : mech;
            float chat = Weight(first, second, InteractionDefOf.Chitchat);
            float deep = Weight(first, second, InteractionDefOf.DeepTalk);
            if (chat + deep <= 0f) return false;
            InteractionDef chosen = Rand.Value * (chat + deep) < chat
                ? InteractionDefOf.Chitchat : InteractionDefOf.DeepTalk;
            return first.interactions.TryInteractWith(second, chosen);
        }

        private static float Weight(Pawn first, Pawn second, InteractionDef interaction) =>
            CanTalk(first, second, interaction) ? Mathf.Max(0f, interaction.Worker.RandomSelectionWeight(first, second)) : 0f;

        // 仅缺少技能的模块伴侣交谈跳过经验；有技能时完全沿用原版 Learn。
        public static void LearnInteractionSkill(Pawn_SkillTracker? skills, SkillDef skill, float xp,
            bool direct, bool ignoreLearnRate, Pawn initiator, Pawn recipient, InteractionDef interaction)
        {
            if (skills == null && IsCompanionInteraction(interaction)
                && SyntheticCompanionRelationshipUtility.IsProtectedPair(initiator, recipient)) return;
            skills!.Learn(skill, xp, direct, ignoreLearnRate);
        }

        [HarmonyPatch(typeof(Pawn_InteractionsTracker), nameof(Pawn_InteractionsTracker.TryInteractWith))]
        public static class InteractionSkillSafety
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codes = new List<CodeInstruction>(instructions);
                MethodInfo learn = AccessTools.Method(typeof(Pawn_SkillTracker), nameof(Pawn_SkillTracker.Learn),
                    new[] { typeof(SkillDef), typeof(float), typeof(bool), typeof(bool) });
                MethodInfo safeLearn = AccessTools.Method(typeof(SyntheticCompanionSocialUtility), nameof(LearnInteractionSkill));
                FieldInfo pawnField = AccessTools.Field(typeof(Pawn_InteractionsTracker), "pawn");
                int calls = codes.FindAll(code => code.Calls(learn)).Count;
                skillSafetyReady = calls == 2;
                if (!skillSafetyReady)
                {
                    Log.Error("[MAP] 伴侣交谈的技能经验补丁结构变化；无技能机械体暂停发起交谈。");
                    foreach (CodeInstruction code in codes) yield return code;
                    yield break;
                }
                // 保持原版方法整体，包括记忆、互动 Worker、气泡、记录及时间更新。
                for (int i = 0; i < codes.Count; i++)
                {
                    CodeInstruction code = codes[i];
                    if (!code.Calls(learn)) { yield return code; continue; }
                    // 沿用原指令的标签/异常块，分支仍从附加参数的第一条指令进入。
                    code.opcode = OpCodes.Ldarg_0;
                    code.operand = null;
                    yield return code;
                    yield return new CodeInstruction(OpCodes.Ldfld, pawnField);
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Ldarg_2);
                    yield return new CodeInstruction(OpCodes.Call, safeLearn);
                }
            }
        }
    }
}
