using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class ImplantEffectUtility
    {
        public static HashSet<Pawn> CollectControlledMechsAndSelf(
            Pawn? provider,
            bool includeMechanoidMechanitorSelf)
        {
            HashSet<Pawn> result = new HashSet<Pawn>();
            if (!IsValidProvider(provider))
            {
                return result;
            }

            Pawn_MechanitorTracker? mechanitor = provider!.mechanitor;
            if (mechanitor != null)
            {
                List<Pawn>? controlledPawns = mechanitor.ControlledPawns;
                if (controlledPawns != null)
                {
                    for (int i = 0; i < controlledPawns.Count; i++)
                    {
                        Pawn controlledPawn = controlledPawns[i];
                        if (!IsValidMechRecipient(controlledPawn))
                        {
                            continue;
                        }

                        bool isActuallyControlledByProvider;
                        if (MAPMechanitorNodeUtility.HasNode(controlledPawn)
                            && MAPMechanitorNodeUtility.UsesVanillaControlPath(controlledPawn))
                        {
                            isActuallyControlledByProvider =
                                MAPOverseerRelationDirectionUtility.IsActualOverseerOf(
                                    provider,
                                    controlledPawn);
                        }
                        else
                        {
                            isActuallyControlledByProvider =
                                controlledPawn.GetOverseer() == provider;
                        }

                        if (isActuallyControlledByProvider)
                        {
                            result.Add(controlledPawn);
                        }
                    }
                }
            }

            if (includeMechanoidMechanitorSelf
                && IsValidMechRecipient(provider)
                && MechanoidMechanitorRoleUtility.IsMechanoidMechanitor(provider))
            {
                result.Add(provider);
            }

            return result;
        }

        public static bool HasHediff(Pawn? pawn, HediffDef? hediffDef)
        {
            return pawn?.health?.hediffSet != null
                && hediffDef != null
                && pawn.health.hediffSet.HasHediff(hediffDef);
        }

        private static bool IsValidProvider(Pawn? pawn)
        {
            return pawn != null
                && !pawn.Destroyed
                && !pawn.Dead
                && pawn.health?.hediffSet != null;
        }

        private static bool IsValidMechRecipient(Pawn? pawn)
        {
            return pawn != null
                && !pawn.Destroyed
                && !pawn.Dead
                && pawn.RaceProps.IsMechanoid
                && pawn.health?.hediffSet != null;
        }
    }
}
