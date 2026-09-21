using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    // 直接复用原版敌对战斗决策，包括视线、威胁过滤、远程射击位置及近战追击。
    internal sealed class SunBossRallyCombat : JobGiver_AIFightEnemies
    {
        private static readonly SunBossRallyCombat Instance = new SunBossRallyCombat();
        internal static Job? TryGetJob(Pawn pawn) => Instance.TryGiveJob(pawn);
    }

    public sealed class JobDriver_SunBossRally : JobDriver_Goto
    {
        protected override IEnumerable<Toil> MakeNewToils()
        {
            foreach (Toil toil in base.MakeNewToils())
            {
                toil.AddPreTickAction(() =>
                {
                    if (!pawn.IsHashIntervalTick(15) || pawn.Downed || pawn.stances?.stunner?.Stunned == true) return;
                    Job? combat = SunBossRallyCombat.TryGetJob(pawn);
                    if (combat != null) pawn.jobs.StartJob(combat, JobCondition.InterruptForced);
                });
                yield return toil;
            }
        }
    }
}
