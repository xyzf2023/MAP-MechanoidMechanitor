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
            return context.FirstSelectedPawn != null;
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(
            Thing clickedThing,
            FloatMenuContext context)
        {
            if (clickedThing is not Building_CommsConsole)
            {
                yield break;
            }

            if (!MechanoidMechanitorMechHiveCommunicationUtility.TryGetContactableMechHive(
                    out _))
            {
                yield break;
            }

            yield return new FloatMenuOption(
                MechanoidMechanitorMechHiveCommunicationUtility.ContactOvermindLabel,
                MechanoidMechanitorMechHiveCommunicationUtility.TryOpenContactOvermindDialog,
                MechanoidMechanitorMechHiveCommunicationUtility.ContactOvermindIcon,
                MechanoidMechanitorMechHiveCommunicationUtility.ContactOvermindIconColor,
                MenuOptionPriority.SummonThreat);
        }
    }
}
