using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor
{
    // 原版自动装载只安排殖民者和有监管者的机械族；独立机械师需要加入同一个装载 Lord。
    [HarmonyPatch(typeof(TransporterUtility), nameof(TransporterUtility.MakeLordsAsAppropriate))]
    internal static class MAPLendShuttleLoadingLordPatch
    {
        [HarmonyPostfix]
        private static void Postfix(List<Pawn> pawns, List<CompTransporter> transporters, Map map)
        {
            if (pawns == null || transporters == null || transporters.Count != 1 || map == null)
            {
                return;
            }

            CompTransporter transporter = transporters[0];
            CompShuttle? shuttle = transporter?.parent?.TryGetComp<CompShuttle>();
            if (transporter == null || transporter.groupID < 0
                || !MAPLendShuttleUtility.IsLendShuttle(shuttle))
            {
                return;
            }

            Lord? lord = TransporterUtility.FindLord(transporter.groupID, map);
            foreach (Pawn pawn in pawns)
            {
                if (!MAPLendShuttleUtility.IsSupportedLendPassenger(pawn, shuttle)
                    || !pawn.Spawned || pawn.Map != map || pawn.Downed || pawn.jobs == null)
                {
                    continue;
                }

                if (lord == null)
                {
                    lord = LordMaker.MakeNewLord(Faction.OfPlayer,
                        new LordJob_LoadAndEnterTransporters(transporter.groupID), map);
                }

                if (lord.ownedPawns.Contains(pawn))
                {
                    continue;
                }

                pawn.GetLord()?.Notify_PawnLost(pawn, PawnLostCondition.ForcedToJoinOtherLord);
                lord.AddPawn(pawn);
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
        }
    }
}
