using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 合体期间 Gizmo 的唯一补充入口。开始合体改由双向右键菜单下达；
    /// 这里保留能源、结构稳定值、源战车激光和手动解除合体。
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    internal static class MechFusionGizmoPatch
    {
        public static IEnumerable<Gizmo> Postfix(
            IEnumerable<Gizmo> __result,
            Pawn __instance)
        {
            foreach (Gizmo gizmo in __result)
            {
                yield return gizmo;
            }

            if (!MechFusionEnergyUtility.TryGetActiveSessionForWearer(
                    __instance,
                    out MechFusionSession? session)
                || session == null)
            {
                yield break;
            }

            yield return new Gizmo_MechFusionBar(
                session,
                MechFusionBarKind.Energy);
            if (session.MaxStability > 0f)
            {
                yield return new Gizmo_MechFusionBar(
                    session,
                    MechFusionBarKind.Stability);
            }

            CompChariotLaserSystem? laserComp =
                session.SourcePawn?.GetComp<CompChariotLaserSystem>();
            if (laserComp != null)
            {
                foreach (Gizmo gizmo in
                         ChariotLaserCommandUtility.GetGizmos(
                             __instance,
                             laserComp))
                {
                    yield return gizmo;
                }
            }

            yield return BuildManualReleaseCommand(__instance, session);
        }

        private static Command BuildManualReleaseCommand(
            Pawn wearer,
            MechFusionSession session)
        {
            Command_Action command = new Command_Action
            {
                defaultLabel =
                    "MAP_MechanoidMechanitor.Fusion.Release.Label".Translate(),
                defaultDesc =
                    "MAP_MechanoidMechanitor.Fusion.Release.Description"
                        .Translate(),
                icon = TexCommand.ReleaseAnimals,
                action = delegate
                {
                    MechFusionTeardownService.TryTeardown(
                        session,
                        MechFusionExitReason.Manual,
                        force: false);
                }
            };

            if (session.IsEnding)
            {
                command.Disable(
                    "MAP_MechanoidMechanitor.Fusion.Release.InProgress"
                        .Translate());
            }
            else if (MechanicalFlightUtility.IsAirborne(wearer))
            {
                command.Disable(
                    "MAP_MechanoidMechanitor.Fusion.Release.Flying".Translate());
            }

            return command;
        }
    }
}
