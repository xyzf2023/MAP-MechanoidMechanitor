using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 原版运输窗口只在机械体已受监管时将其列入机械体分组。
    /// 借人任务中的独立机械师不满足该条件，需要补入同一窗口。
    /// </summary>
    [HarmonyPatch(typeof(Dialog_LoadTransporters), "CalculateAndRecacheTransferables")]
    internal static class MAPLendShuttleLoadDialogPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            List<CompTransporter> ___transporters,
            List<TransferableOneWay> ___transferables,
            TransferableOneWayWidget ___pawnsTransfer)
        {
            if (___transporters == null || ___transporters.Count == 0
                || ___transferables == null || ___pawnsTransfer == null)
            {
                return;
            }

            CompShuttle? shuttle = ___transporters[0]?.parent?.TryGetComp<CompShuttle>();
            if (!MAPLendShuttleUtility.IsLendShuttle(shuttle))
            {
                return;
            }

            List<TransferableOneWay> extra = new List<TransferableOneWay>();
            foreach (TransferableOneWay transferable in ___transferables)
            {
                if (transferable?.AnyThing is not Pawn pawn
                    || !MAPLendShuttleUtility.IsSupportedLendPassenger(pawn, shuttle)
                    // 保留原版已有条目，避免被列出两次。
                    || pawn.IsFreeNonSlaveColonist || pawn.IsSlave || pawn.IsPrisoner
                    || pawn.IsAnimal
                    || (pawn.IsColonyMech && pawn.OverseerSubject != null
                        && pawn.OverseerSubject.State == OverseerSubjectState.Overseen))
                {
                    continue;
                }

                extra.Add(transferable);
            }

            if (extra.Count > 0)
            {
                ___pawnsTransfer.AddSection("MechsSection".Translate(), extra);
            }
        }
    }
}
