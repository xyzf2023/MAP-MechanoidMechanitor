using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public sealed class CompProperties_MechanoidOvermindContactUnlockedLetter : CompProperties
    {
        public CompProperties_MechanoidOvermindContactUnlockedLetter()
        {
            compClass = typeof(CompMechanoidOvermindContactUnlockedLetter);
        }
    }

    public sealed class CompMechanoidOvermindContactUnlockedLetter : ThingComp
    {
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (respawningAfterLoad)
            {
                return;
            }

            if (Current.Game == null)
            {
                return;
            }

            Faction? player = Faction.OfPlayerSilentFail;
            if (player == null)
            {
                return;
            }

            if (parent.Faction != player)
            {
                return;
            }

            if (!MechanoidMechanitorMechHiveCommunicationUtility.TryGetContactableMechHive(
                    out Faction mechHive))
            {
                return;
            }

            GameComponent_MechanoidMechanitorStoryState? storyState =
                Current.Game.GetComponent<GameComponent_MechanoidMechanitorStoryState>();
            if (storyState == null)
            {
                return;
            }

            MechanoidMechanitorPurgeDirectiveRuntimeState? runtime =
                storyState.PurgeDirectiveRuntimeState;
            if (runtime == null || runtime.ContactOvermindUnlockedLetterSent)
            {
                return;
            }

            Find.LetterStack.ReceiveLetter(
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.ContactUnlockedLetter.Label"
                    .Translate(),
                "MAP_MechanoidMechanitor.PurgeDirective.Communication.ContactUnlockedLetter.Text"
                    .Translate(),
                LetterDefOf.NeutralEvent,
                parent,
                mechHive);
            runtime.MarkContactOvermindUnlockedLetterSent();
        }
    }
}
