using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor.Scenarios
{
    public static class SymbiosisCovenantDiplomacyUtility
    {
        public static bool ShouldShowSecretContact(Faction? faction)
        {
            if (!GameComponent_SymbiosisCovenantState.IsActive)
            {
                return false;
            }

            GameComponent_SymbiosisCovenantState? state =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (state == null || !state.Initialized || !state.PublicDeclarationBroadcast)
            {
                return false;
            }

            if (faction == null || faction.IsPlayer || !faction.HasGoodwill)
            {
                return false;
            }

            Faction? player = Faction.OfPlayerSilentFail;
            if (player == null || faction.HostileTo(player))
            {
                return false;
            }

            if (!MechanoidMechanitorOrdinaryFactionUtility.IsOrdinaryFaction(faction))
            {
                return false;
            }

            SymbiosisCovenantFactionRecord? record = state.GetRecord(faction);
            if (record == null || record.CovenantMember)
            {
                return false;
            }

            return record.Trust >= -25;
        }

        public static bool CanStartSecretContact(
            Faction? faction,
            out string disabledReason)
        {
            disabledReason = string.Empty;
            if (!ShouldShowSecretContact(faction))
            {
                disabledReason =
                    "MAP_MechanoidMechanitor.Symbiosis.SecretContact.Unavailable"
                        .Translate();
                return false;
            }

            GameComponent_SymbiosisCovenantState? state =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            SymbiosisCovenantFactionRecord? record = state?.GetRecord(faction);
            if (record == null)
            {
                disabledReason =
                    "MAP_MechanoidMechanitor.Symbiosis.SecretContact.Unavailable"
                        .Translate();
                return false;
            }

            if (record.ProposalPending)
            {
                disabledReason =
                    "MAP_MechanoidMechanitor.Symbiosis.SecretContact.Pending"
                        .Translate();
                return false;
            }

            if (record.PermanentlyRefusesInvitation)
            {
                disabledReason =
                    "MAP_MechanoidMechanitor.Symbiosis.SecretContact.PermanentRefuse"
                        .Translate();
                return false;
            }

            if (record.NextInvitationTick > CurrentTick)
            {
                int remainingTicks = record.NextInvitationTick - CurrentTick;
                disabledReason =
                    "MAP_MechanoidMechanitor.Symbiosis.SecretContact.Cooling"
                        .Translate(GenDate.ToStringTicksToPeriod(remainingTicks));
                return false;
            }

            return true;
        }

        public static DiaOption CreateSecretContactOption(
            Pawn negotiator,
            Faction faction)
        {
            DiaOption option = new DiaOption(
                "MAP_MechanoidMechanitor.Symbiosis.SecretContact".Translate());
            if (CanStartSecretContact(faction, out string disabledReason))
            {
                option.link = BuildSecretContactDialogue(negotiator, faction);
            }
            else
            {
                option.Disable(disabledReason);
            }

            return option;
        }

        public static DiaNode BuildSecretContactDialogue(
            Pawn negotiator,
            Faction faction)
        {
            DiaNode firstNode = new DiaNode(
                "MAP_MechanoidMechanitor.Symbiosis.SecretContact.Node1".Translate());

            DiaOption explainOption = new DiaOption(
                "MAP_MechanoidMechanitor.Symbiosis.SecretContact.Explain".Translate());
            explainOption.link = BuildPositionNode(negotiator, faction);
            DiaOption endOption = new DiaOption(
                "MAP_MechanoidMechanitor.Symbiosis.SecretContact.End".Translate())
            {
                resolveTree = true
            };
            firstNode.options.Add(explainOption);
            firstNode.options.Add(endOption);

            return firstNode;
        }

        private static DiaNode BuildPositionNode(Pawn negotiator, Faction faction)
        {
            DiaNode node = new DiaNode(
                "MAP_MechanoidMechanitor.Symbiosis.SecretContact.Node2".Translate());

            DiaOption proposeOption = new DiaOption(
                "MAP_MechanoidMechanitor.Symbiosis.SecretContact.Propose".Translate());
            proposeOption.link = BuildProposalNode(negotiator, faction);

            DiaOption declineOption = new DiaOption(
                "MAP_MechanoidMechanitor.Symbiosis.SecretContact.Decline".Translate())
            {
                resolveTree = true
            };

            node.options.Add(proposeOption);
            node.options.Add(declineOption);
            return node;
        }

        private static DiaNode BuildProposalNode(Pawn negotiator, Faction faction)
        {
            DiaNode node = new DiaNode(
                "MAP_MechanoidMechanitor.Symbiosis.SecretContact.Node3".Translate());

            DiaOption issueOption = new DiaOption(
                "MAP_MechanoidMechanitor.Symbiosis.SecretContact.Issue".Translate());
            issueOption.action =
                () => GameComponent_SymbiosisCovenantState.CurrentComponent
                    ?.TryBeginCovenantProposal(faction);
            issueOption.resolveTree = true;

            DiaOption backOption = new DiaOption(
                "MAP_MechanoidMechanitor.Symbiosis.SecretContact.Back".Translate());
            backOption.link = BuildPositionNode(negotiator, faction);

            node.options.Add(issueOption);
            node.options.Add(backOption);
            return node;
        }

        private static int CurrentTick => Find.TickManager?.TicksGame ?? 0;
    }
}
