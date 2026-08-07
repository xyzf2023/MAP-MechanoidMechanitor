using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 原版授爵结算会直接向所有非目标参与者写入 mood memory，
    /// 但授爵仪式的候选过滤本身允许无 mood 的机械族参与。
    /// 若现有 Ideology Full 模式安全重放会接管，则完全保留其原有行为；
    /// 否则在机械族机械师作为授爵目标时移除无法接收 mood memory 的额外参与者，
    /// 防止仪式结束阶段空引用。授爵目标自身始终保留在 presence 中。
    /// </summary>
    [HarmonyPatch(
        typeof(RitualOutcomeEffectWorker_Bestowing),
        nameof(RitualOutcomeEffectWorker_Bestowing.Apply))]
    public static class Patch_RitualOutcomeEffectWorker_Bestowing_MechanitorParticipants
    {
        [HarmonyPrefix]
        public static void Prefix(
            Dictionary<Pawn, int> totalPresence,
            LordJob_Ritual jobRitual)
        {
            if (totalPresence == null
                || jobRitual is not LordJob_BestowingCeremony bestowing
                || !MechanoidMechanitorRoyaltyUtility
                    .IsRoyaltyEligibleMechanitor(bestowing.target))
            {
                return;
            }

            // Full 意识形态适配已有完整安全重放，并刻意保留机械族参与者计入质量。
            if (MechanoidMechanitorIdeologyMoodSafetyPatches
                .InvolvesFullMechanitorWithoutMood(totalPresence))
            {
                return;
            }

            List<Pawn> remove = new List<Pawn>();
            foreach (KeyValuePair<Pawn, int> entry in totalPresence)
            {
                Pawn pawn = entry.Key;
                if (pawn == null
                    || ReferenceEquals(pawn, bestowing.target))
                {
                    continue;
                }

                if (pawn.needs?.mood?.thoughts?.memories == null)
                {
                    remove.Add(pawn);
                }
            }

            for (int i = 0; i < remove.Count; i++)
            {
                totalPresence.Remove(remove[i]);
            }
        }
    }
}
