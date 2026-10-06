using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    // 正义任务使用自定义 MakeLord 部件，补齐原版对待抵达首领的识别。
    [HarmonyPatch(typeof(CallBossgroupUtility), nameof(CallBossgroupUtility.GetPendingBossgroup))]
    public static class JusticeBossPendingCallPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref PawnKindDef? __result)
        {
            if (__result != null || Verse.Current.Game == null)
            {
                return;
            }

            foreach (Quest quest in Find.QuestManager.QuestsListForReading)
            {
                if (quest.State != QuestState.Ongoing)
                {
                    continue;
                }

                foreach (QuestPart part in quest.PartsListForReading)
                {
                    if (!(part is QuestPart_JusticeBossGroup justicePart)
                        || justicePart.bosses.NullOrEmpty())
                    {
                        continue;
                    }

                    bool allPending = true;
                    foreach (Pawn boss in justicePart.bosses)
                    {
                        if (boss == null || boss.Destroyed || boss.Spawned || boss.Dead)
                        {
                            allPending = false;
                            break;
                        }
                    }

                    if (allPending)
                    {
                        __result = justicePart.bosses[0].kindDef;
                        return;
                    }
                }
            }
        }
    }
}
