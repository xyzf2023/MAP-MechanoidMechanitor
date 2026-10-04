using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    /// <summary>玩家自律机械体无需监管关系即可由任意机械师拆除，执行仍复用原版任务。</summary>
    [HarmonyPatch(typeof(FloatMenuOptionProvider_Mechanitor),
        nameof(FloatMenuOptionProvider_Mechanitor.GetOptionsFor),
        new System.Type[] { typeof(Pawn), typeof(FloatMenuContext) })]
    internal static class AutonomousMechDisassemblyMenuPatch
    {
        [HarmonyPostfix]
        public static IEnumerable<FloatMenuOption> Postfix(
            IEnumerable<FloatMenuOption> __result, Pawn clickedPawn, FloatMenuContext context)
        {
            Pawn? actor = context?.FirstSelectedPawn;
            if (actor == null || actor == clickedPawn
                || actor.Faction != Faction.OfPlayer
                || !MechanitorUtility.IsMechanitor(actor)
                || !AutonomousMechUtility.IsPlayerAutonomousMech(clickedPawn)
                || clickedPawn.Faction != Faction.OfPlayer)
            {
                foreach (FloatMenuOption option in __result)
                    yield return option;
                yield break;
            }

            string blockedLabel = "CannotDisassembleMech".Translate(clickedPawn.LabelCap)
                + ": " + "MustBeOverseer".Translate().CapitalizeFirst();
            string disassembleLabel = "DisassembleMech".Translate(clickedPawn.LabelCap).ToString();
            bool replaced = false;
            foreach (FloatMenuOption option in __result)
            {
                // 只替换原版监管者限制项，保留控制保护、维修和其他 MOD 的菜单。
                if (option.Disabled && option.Label == blockedLabel)
                {
                    if (!replaced && !clickedPawn.IsFighting())
                        yield return MakeDisassembleOption(actor, clickedPawn, disassembleLabel);
                    replaced = true;
                    continue;
                }
                yield return option;
            }
        }

        private static FloatMenuOption MakeDisassembleOption(Pawn actor, Pawn target, string label)
        {
            if (!actor.CanReach(target, PathEndMode.Touch, Danger.Deadly))
                return new FloatMenuOption("CannotDisassembleMech".Translate(target.LabelCap)
                    + ": " + "NoPath".Translate().CapitalizeFirst(), null);

            return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(label, delegate
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "ConfirmDisassemblingMech".Translate(target.LabelCap) + ":\n"
                    + MechanitorUtility.IngredientsFromDisassembly(target.def)
                        .Select(item => item.Summary).ToLineList("  - "), delegate
                    {
                        // 确认窗口开启后可能发生死亡、阵营或自律资格变化，派发前再检查。
                        if (actor.Destroyed || actor.Dead || actor.jobs == null
                            || actor.Faction != Faction.OfPlayer
                            || !MechanitorUtility.IsMechanitor(actor)
                            || !AutonomousMechUtility.IsPlayerAutonomousMech(target)
                            || target.Faction != Faction.OfPlayer || !target.Spawned
                            || target.IsFighting())
                            return;
                        actor.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.DisassembleMech, target),
                            JobTag.Misc);
                    }, destructive: true));
            }, MenuOptionPriority.Low, null, null, 0f, null, null,
                playSelectionSound: true, -20), actor, new LocalTargetInfo(target));
        }
    }
}
