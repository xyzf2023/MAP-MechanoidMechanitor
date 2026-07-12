using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MAP_MechanoidMechanitor
{
    public static class ManagedResearchAbilitySyncUtility
    {
        public static void SyncPawn(Pawn? pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead)
            {
                return;
            }

            IReadOnlyList<ManagedResearchAbilityDescriptor> all =
                ManagedResearchAbilityCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                SyncPawnAbility(pawn, all[i]);
            }
        }

        public static void SyncAllRelevantPawns()
        {
            HashSet<Pawn> targets = CollectRelevantPawns();
            foreach (Pawn pawn in targets)
            {
                SyncPawn(pawn);
            }
        }

        public static void SyncPawnAbility(
            Pawn? pawn,
            ManagedResearchAbilityDescriptor descriptor)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead || descriptor == null)
            {
                return;
            }

            AbilityDef? abilityDef = descriptor.AbilityDef;
            if (abilityDef == null)
            {
                return;
            }

            bool shouldHave = ResearchFeatureUnlockUtility.ShouldPawnHaveAbility(pawn, descriptor);
            Pawn_AbilityTracker? tracker = pawn.abilities;
            Ability? existing = tracker?.GetAbility(abilityDef);

            if (shouldHave)
            {
                if (existing != null)
                {
                    return;
                }

                if (!TryEnsureAbilityTracker(pawn, out tracker) || tracker == null)
                {
                    return;
                }

                tracker.GainAbility(abilityDef);
                return;
            }

            if (existing == null || tracker == null)
            {
                return;
            }

            SafeRemoveAbility(pawn, abilityDef);
        }

        public static bool SafeRemoveAbility(Pawn? pawn, AbilityDef? abilityDef)
        {
            if (pawn == null || pawn.Destroyed || abilityDef == null)
            {
                return true;
            }

            try
            {
                Pawn_AbilityTracker? tracker = pawn.abilities;
                if (tracker == null)
                {
                    return true;
                }

                Ability? ability = tracker.GetAbility(abilityDef);
                if (ability == null)
                {
                    return true;
                }

                Pawn_JobTracker? jobs = pawn.jobs;
                if (jobs != null)
                {
                    Job? curJob = pawn.CurJob;
                    if (curJob != null && ReferenceEquals(curJob.ability, ability))
                    {
                        jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                    }

                    JobQueue? queue = jobs.jobQueue;
                    if (queue != null && queue.Count > 0)
                    {
                        queue.RemoveAll(
                            pawn,
                            job => job != null && ReferenceEquals(job.ability, ability));
                    }
                }

                tracker.RemoveAbility(abilityDef);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[MAP-机械族机械师] 安全移除能力或工作清理异常：" +
                    $"ability={abilityDef.defName}，" +
                    $"pawn={pawn.LabelShort}（{pawn.ThingID}）：{ex}");
                return false;
            }
        }

        public static ManagedResearchAbilityDescriptor? FindDescriptor(AbilityDef? abilityDef)
        {
            if (abilityDef == null)
            {
                return null;
            }

            IReadOnlyList<ManagedResearchAbilityDescriptor> all =
                ManagedResearchAbilityCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                ManagedResearchAbilityDescriptor descriptor = all[i];
                if (descriptor.AbilityDef == abilityDef)
                {
                    return descriptor;
                }
            }

            return null;
        }

        public static HashSet<Pawn> CollectRelevantPawns()
        {
            HashSet<Pawn> result = new HashSet<Pawn>();

            IReadOnlyList<Pawn> registered =
                GameComponent_MechanoidMechanitorRegistry.CurrentRegisteredMechanitors;
            for (int i = 0; i < registered.Count; i++)
            {
                TryAddSyncTarget(result, registered[i]);
            }

            TryAddSyncTarget(
                result,
                GameComponent_MechanoidMechanitorRegistry.CurrentMechanicalConsciousnessHost);

            TryAddPlayerJusticeFromWorld(result);
            return result;
        }

        private static void TryAddPlayerJusticeFromWorld(HashSet<Pawn> result)
        {
            if (Current.Game == null || Find.World == null)
            {
                return;
            }

            List<Pawn> playerFactionPawns =
                PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_OfPlayerFaction;
            for (int i = 0; i < playerFactionPawns.Count; i++)
            {
                Pawn pawn = playerFactionPawns[i];
                if (JusticePawnUtility.IsJustice(pawn))
                {
                    TryAddSyncTarget(result, pawn);
                }
            }
        }

        private static void TryAddSyncTarget(HashSet<Pawn> result, Pawn? pawn)
        {
            if (pawn == null
                || pawn.Destroyed
                || pawn.Dead
                || pawn.Faction == null
                || !pawn.Faction.IsPlayerSafe())
            {
                return;
            }

            result.Add(pawn);
        }

        private static bool TryEnsureAbilityTracker(Pawn pawn, out Pawn_AbilityTracker? tracker)
        {
            tracker = pawn.abilities;
            if (tracker != null)
            {
                return true;
            }

            if (!pawn.RaceProps.IsMechanoid)
            {
                return false;
            }

            pawn.abilities = new Pawn_AbilityTracker(pawn);
            tracker = pawn.abilities;
            if (tracker != null)
            {
                return true;
            }

            Log.Error(
                "[MAP-机械族机械师] 符合资格但无法创建 Pawn_AbilityTracker：" +
                $"pawn={pawn.LabelShort}（{pawn.ThingID}）。");
            return false;
        }
    }
}
