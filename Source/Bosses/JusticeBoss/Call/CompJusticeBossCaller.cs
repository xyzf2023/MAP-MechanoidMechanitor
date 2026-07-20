using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class CompProperties_JusticeBossCaller : CompProperties
    {
        public CompProperties_JusticeBossCaller()
        {
            compClass = typeof(CompJusticeBossCaller);
        }
    }

    public class CompJusticeBossCaller : ThingComp
    {
        private static readonly CachedTexture CallIcon =
            new CachedTexture("UI/Icons/SummonMechThreat");

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction != Faction.OfPlayer || !parent.Spawned || parent.Map == null)
            {
                yield break;
            }

            AcceptanceReport disabledReason = GetDisabledReason();
            Command_Action command = new Command_Action
            {
                defaultLabel =
                    "MAP_MechanoidMechanitor.JusticeBoss.Call.GizmoLabel".Translate(),
                defaultDesc =
                    "MAP_MechanoidMechanitor.JusticeBoss.Call.GizmoDesc".Translate(),
                icon = CallIcon.Texture,
                action = OnClicked,
            };

            if (!disabledReason.Accepted)
            {
                command.Disable(disabledReason.Reason);
            }

            yield return command;
        }

        private AcceptanceReport GetDisabledReason()
        {
            CompPowerTrader? power = parent.TryGetComp<CompPowerTrader>();
            if (power != null && !power.PowerOn)
            {
                return "MAP_MechanoidMechanitor.JusticeBoss.Call.DisabledNoPower".Translate();
            }

            return JusticeBossCallUtility.CanCall(parent.Map);
        }

        private void OnClicked()
        {
            AcceptanceReport disabledReason = GetDisabledReason();
            if (!disabledReason.Accepted)
            {
                Messages.Message(
                    disabledReason.Reason,
                    parent,
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            Find.WindowStack.Add(
                Dialog_MessageBox.CreateConfirmation(
                    "MAP_MechanoidMechanitor.JusticeBoss.Call.ConfirmText".Translate(),
                    ConfirmCall,
                    destructive: true,
                    title: "MAP_MechanoidMechanitor.JusticeBoss.Call.ConfirmTitle".Translate()));
        }

        private void ConfirmCall()
        {
            if (parent.Map == null)
            {
                return;
            }

            JusticeBossCallUtility.TryCall(parent.Map);
        }
    }
}