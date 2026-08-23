using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class HediffComp_PortableComms : HediffComp
    {
        private static Texture2D? cachedIcon;

        private static Texture2D Icon =>
            cachedIcon ??=
                ContentFinder<Texture2D>.Get("UI/Commands/LaunchReport");

        public override IEnumerable<Gizmo> CompGetGizmos()
        {
            Pawn? pawn = parent?.pawn;
            if (pawn == null || pawn.Dead || pawn.Destroyed)
            {
                yield break;
            }

            // 只允许玩家所属 Pawn 提供这个主动通讯 Gizmo。
            if (pawn.Faction == null || !pawn.Faction.IsPlayerSafe())
            {
                yield break;
            }

            Command_Action command = new Command_Action
            {
                defaultLabel =
                    "MAP_MicroCommunicator_CommandLabel".Translate(),
                defaultDesc =
                    "MAP_MicroCommunicator_CommandDesc".Translate(),
                icon = Icon,
                action = () =>
                    PortableCommsUtility.OpenCommsMenu(pawn)
            };

            if (!PortableCommsUtility.CanUsePortableComms(
                    pawn,
                    out string disabledReason))
            {
                command.Disable(disabledReason);
            }

            yield return command;
        }
    }

    public sealed class HediffCompProperties_PortableComms
        : HediffCompProperties
    {
        public HediffCompProperties_PortableComms()
        {
            compClass = typeof(HediffComp_PortableComms);
        }
    }
}
