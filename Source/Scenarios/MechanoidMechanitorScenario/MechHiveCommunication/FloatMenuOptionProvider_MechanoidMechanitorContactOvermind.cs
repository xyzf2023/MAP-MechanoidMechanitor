using System.Collections.Generic;
using RimWorld;
using Verse;

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
            if (clickedThing is not Building_CommsConsole)
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
            yield return new FloatMenuOption(
                MechanoidMechanitorMechHiveCommunicationUtility.ContactOvermindLabel,
                () => MechanoidMechanitorMechHiveCommunicationUtility
                    .TryOpenContactOvermindDialog(pawn),
                MechanoidMechanitorMechHiveCommunicationUtility.ContactOvermindIcon,
                MechanoidMechanitorMechHiveCommunicationUtility.ContactOvermindIconColor,
                MenuOptionPriority.SummonThreat);
        }
    }
}
