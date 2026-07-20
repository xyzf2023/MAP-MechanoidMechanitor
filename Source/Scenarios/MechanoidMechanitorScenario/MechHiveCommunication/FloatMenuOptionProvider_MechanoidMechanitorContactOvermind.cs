using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public class FloatMenuOptionProvider_MechanoidMechanitorContactOvermind
        : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;

        protected override bool Undrafted => true;

        protected override bool Multiselect => false;

        protected override bool MechanoidCanDo => true;

        protected override bool AppliesInt(FloatMenuContext context)
        {
            return MechanoidMechanitorMechHiveCommunicationUtility.IsValidContactPawn(
                context.FirstSelectedPawn);
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(
            Thing clickedThing,
            FloatMenuContext context)
        {
            if (clickedThing is not Building_CommsConsole console)
            {
                yield break;
            }

            Pawn? selectedPawn = context.FirstSelectedPawn;
            if (!MechanoidMechanitorMechHiveCommunicationUtility.IsValidContactPawn(
                    selectedPawn))
            {
                yield break;
            }

            if (!MechanoidMechanitorMechHiveCommunicationUtility.TryGetContactableMechHive(
                    out _))
            {
                yield break;
            }

            Pawn pawn = selectedPawn!;
            if (!pawn.CanReach(console, PathEndMode.InteractionCell, Danger.Some))
            {
                yield return new FloatMenuOption("CannotUseNoPath".Translate(), null);
                yield break;
            }

            if (console.Spawned
                && console.Map != null
                && console.Map.gameConditionManager.ElectricityDisabled(console.Map))
            {
                yield return new FloatMenuOption("CannotUseSolarFlare".Translate(), null);
                yield break;
            }

            if (!console.CanUseCommsNow)
            {
                yield return new FloatMenuOption("CannotUseNoPower".Translate(), null);
                yield break;
            }

            FloatMenuOption option = new FloatMenuOption(
                MechanoidMechanitorMechHiveCommunicationUtility.ContactOvermindLabel,
                () => MechanoidMechanitorMechHiveCommunicationUtility
                    .TryOrderContactOvermindJob(pawn, console),
                MechanoidMechanitorMechHiveCommunicationUtility.ContactOvermindIcon,
                MechanoidMechanitorMechHiveCommunicationUtility.ContactOvermindIconColor,
                MenuOptionPriority.SummonThreat);
            yield return FloatMenuUtility.DecoratePrioritizedTask(option, pawn, console);
        }
    }
}
