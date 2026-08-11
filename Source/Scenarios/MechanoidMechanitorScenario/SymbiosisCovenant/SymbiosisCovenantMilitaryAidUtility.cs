using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace MAP_MechanoidMechanitor.Scenarios
{
    internal sealed class SymbiosisCovenantMilitaryAidPendingRaid : IExposable
    {
        public Map? map;
        public Faction? attackerFaction;
        public float raidPoints;
        public int evaluateAtTick;

        public SymbiosisCovenantMilitaryAidPendingRaid() { }

        public void ExposeData()
        {
            Scribe_References.Look(ref map, "map");
            Scribe_References.Look(ref attackerFaction, "attackerFaction");
            Scribe_Values.Look(ref raidPoints, "raidPoints", 0f);
            Scribe_Values.Look(ref evaluateAtTick, "evaluateAtTick", 0);
        }
    }

    internal sealed class SymbiosisCovenantMilitaryAidMapState : IExposable
    {
        public Map? map;
        public int cooldownEndTick;
        public Faction? activeAidFaction;

        public SymbiosisCovenantMilitaryAidMapState() { }

        public void ExposeData()
        {
            Scribe_References.Look(ref map, "map");
            Scribe_Values.Look(ref cooldownEndTick, "cooldownEndTick", 0);
            Scribe_References.Look(ref activeAidFaction, "activeAidFaction");
        }
    }

    internal sealed class SymbiosisCovenantMilitaryAidRuntimeState
    {
        public Faction? lastResponderFaction;
        public List<SymbiosisCovenantMilitaryAidPendingRaid> pendingRaids = new();
        public List<SymbiosisCovenantMilitaryAidMapState> mapStates = new();

        public SymbiosisCovenantMilitaryAidRuntimeState() { }
    }

    public static class SymbiosisCovenantMilitaryAidUtility
    {
        private static readonly ConditionalWeakTable<GameComponent_SymbiosisCovenantState, SymbiosisCovenantMilitaryAidRuntimeState> States = new();

        private static SymbiosisCovenantMilitaryAidRuntimeState GetState(GameComponent_SymbiosisCovenantState component)
            => States.GetOrCreateValue(component);

        public static void NotifyRaidSucceeded(IncidentWorker_RaidEnemy worker, IncidentParms parms)
        {
            SymbiosisCovenantMilitaryAidDef config = SymbiosisCovenantMilitaryAidDefOf.MAP_SymbiosisCovenant_MilitaryAidConfig;
            if (!config.CanTriggerFrom(worker.def)
                || parms.target is not Map map
                || !map.IsPlayerHome
                || parms.faction == null
                || !parms.faction.HostileTo(Faction.OfPlayer)
                || parms.points <= 0f)
            {
                return;
            }

            GameComponent_SymbiosisCovenantState? component = GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (component == null || GetCurrentSettings(component) == null)
            {
                return;
            }

            CleanupState(component);
            if (HasPendingEvaluation(component, map)
                || HasPendingOffer(map)
                || IsInCooldown(component, map)
                || HasActiveCovenantAid(component, map))
            {
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            GetState(component).pendingRaids.Add(new SymbiosisCovenantMilitaryAidPendingRaid
            {
                map = map,
                attackerFaction = parms.faction,
                raidPoints = parms.points,
                evaluateAtTick = now + Math.Max(1, config.evaluationDelayTicks)
            });
        }

        public static bool TryAcceptOffer(ChoiceLetter_SymbiosisCovenantMilitaryAidOffer letter, out TaggedString failureReason)
        {
            failureReason = TaggedString.Empty;
            Map? map = letter.triggerMap;
            Faction? responder = letter.supportFaction;
            Faction? attacker = letter.attackerFaction;
            if (map == null || responder == null || attacker == null || !Find.Maps.Contains(map) || !map.IsPlayerHome)
            {
                failureReason = "MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Failed.MapInvalid".Translate();
                return false;
            }

            GameComponent_SymbiosisCovenantState? component = GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (component == null || GetCurrentSettings(component) == null)
            {
                failureReason = "MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Failed.CovenantUnavailable".Translate();
                return false;
            }

            CleanupState(component);
            if (component.GetRecord(responder)?.CovenantMember != true
                || responder.defeated
                || responder.deactivated
                || responder.HostileTo(Faction.OfPlayer)
                || !responder.HostileTo(attacker))
            {
                failureReason = "MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Failed.ResponderUnavailable".Translate(responder.NameColored);
                return false;
            }
            if (IsInCooldown(component, map) || HasActiveCovenantAid(component, map))
            {
                failureReason = "MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Failed.AlreadySupported".Translate();
                return false;
            }
            if (!HasActiveThreatFromFaction(map, attacker, out _))
            {
                failureReason = "MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Failed.ThreatEnded".Translate();
                return false;
            }

            float supportPoints = letter.supportPoints;
            if (supportPoints <= 0f || !CanProvideAid(responder, map, supportPoints))
            {
                failureReason = "MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Failed.DeploymentImpossible".Translate(responder.NameColored);
                return false;
            }

            int assistLordCountBefore = CountAssistLords(map, responder);
            IncidentParms aidParms = BuildAidParms(map, responder, supportPoints);
            if (!IncidentDefOf.RaidFriendly.Worker.TryExecute(aidParms)
                || CountAssistLords(map, responder) <= assistLordCountBefore)
            {
                failureReason = "MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Failed.DeploymentImpossible".Translate(responder.NameColored);
                return false;
            }

            MarkAidAccepted(component, map, responder);
            return true;
        }

        public static List<Faction> GetEligibleResponders(GameComponent_SymbiosisCovenantState component, Map map, Faction attacker, float supportPoints)
        {
            List<Faction> result = new();
            IReadOnlyList<SymbiosisCovenantFactionRecord> records = component.GetRecordsSorted();
            for (int i = 0; i < records.Count; i++)
            {
                Faction? faction = records[i].Faction;
                if (!records[i].CovenantMember
                    || faction == null
                    || faction == attacker
                    || faction.IsPlayer
                    || faction.defeated
                    || faction.deactivated
                    || faction.Hidden
                    || faction.temporary
                    || faction.HostileTo(Faction.OfPlayer)
                    || !faction.HostileTo(attacker)
                    || !CanProvideAid(faction, map, supportPoints))
                {
                    continue;
                }
                result.Add(faction);
            }
            return result;
        }

        public static bool HasPendingOffer(Map map)
        {
            LetterStack? stack = Find.LetterStack;
            if (stack == null)
            {
                return false;
            }
            List<Letter> letters = stack.LettersListForReading;
            for (int i = 0; i < letters.Count; i++)
            {
                if (letters[i] is ChoiceLetter_SymbiosisCovenantMilitaryAidOffer offer
                    && offer.triggerMap == map
                    && !offer.TimeoutPassed)
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsInCooldown(GameComponent_SymbiosisCovenantState component, Map map)
        {
            SymbiosisCovenantMilitaryAidMapState? state = FindMapState(component, map, false);
            return state != null && Find.TickManager != null && state.cooldownEndTick > Find.TickManager.TicksGame;
        }

        public static bool HasActiveCovenantAid(GameComponent_SymbiosisCovenantState component, Map map)
        {
            SymbiosisCovenantMilitaryAidMapState? state = FindMapState(component, map, false);
            if (state?.activeAidFaction == null)
            {
                return false;
            }
            if (HasAssistLord(map, state.activeAidFaction))
            {
                return true;
            }
            state.activeAidFaction = null;
            return false;
        }

        public static void Tick(GameComponent_SymbiosisCovenantState component)
        {
            if (Find.TickManager == null)
            {
                return;
            }
            int now = Find.TickManager.TicksGame;
            ProcessPendingRaids(component, now);
            if (now % 2500 == 0)
            {
                CleanupState(component);
            }
        }

        public static void ExposeData(GameComponent_SymbiosisCovenantState component)
        {
            SymbiosisCovenantMilitaryAidRuntimeState runtime = GetState(component);
            Scribe_References.Look(ref runtime.lastResponderFaction, "symbiosisCovenantLastMilitaryAidResponderFaction");
            Scribe_Collections.Look(ref runtime.pendingRaids, "symbiosisCovenantMilitaryAidPendingRaids", LookMode.Deep);
            Scribe_Collections.Look(ref runtime.mapStates, "symbiosisCovenantMilitaryAidMapStates", LookMode.Deep);
            if (Scribe.mode != LoadSaveMode.PostLoadInit)
            {
                return;
            }

            runtime.pendingRaids ??= new List<SymbiosisCovenantMilitaryAidPendingRaid>();
            runtime.mapStates ??= new List<SymbiosisCovenantMilitaryAidMapState>();
            runtime.pendingRaids.RemoveAll(p => p == null || p.map == null || p.attackerFaction == null);
            runtime.mapStates.RemoveAll(s => s == null || s.map == null);
            if (runtime.lastResponderFaction != null && runtime.lastResponderFaction.defeated)
            {
                runtime.lastResponderFaction = null;
            }
        }

        public static bool DevForceOfferForCurrentThreat()
        {
            if (!Prefs.DevMode || Find.CurrentMap == null)
            {
                return false;
            }
            Map map = Find.CurrentMap;
            GameComponent_SymbiosisCovenantState? component = GameComponent_SymbiosisCovenantState.CurrentComponent;
            SymbiosisCovenantMilitaryAidLevelSettings? settings = GetCurrentSettings(component);
            if (component == null || settings == null)
            {
                return false;
            }

            CleanupState(component);
            if (HasPendingEvaluation(component, map) || HasPendingOffer(map) || IsInCooldown(component, map) || HasActiveCovenantAid(component, map))
            {
                return false;
            }

            Faction? attacker = FindActiveHostileFaction(map);
            if (attacker == null)
            {
                return false;
            }
            float raidPoints = Math.Max(1f, map.attackTargetsCache.TargetsHostileToColony
                .Where(target => GenHostility.IsActiveThreatToPlayer(target))
                .OfType<Pawn>()
                .Where(p => p.Faction == attacker)
                .Sum(p => p.kindDef.combatPower));
            float supportPoints = raidPoints * settings.supportPointsFactor;
            List<Faction> responders = GetEligibleResponders(component, map, attacker, supportPoints);
            Faction? responder = SelectResponder(component, responders, SymbiosisCovenantMilitaryAidDefOf.MAP_SymbiosisCovenant_MilitaryAidConfig.repeatedResponderWeight);
            if (responder == null)
            {
                return false;
            }

            new ChoiceLetter_SymbiosisCovenantMilitaryAidOffer(map, responder, attacker, raidPoints, supportPoints).Send();
            return true;
        }

        public static bool DevClearCurrentMapState()
        {
            if (!Prefs.DevMode || Find.CurrentMap == null)
            {
                return false;
            }
            Map map = Find.CurrentMap;
            GameComponent_SymbiosisCovenantState? component = GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (component == null)
            {
                return false;
            }

            SymbiosisCovenantMilitaryAidRuntimeState runtime = GetState(component);
            runtime.pendingRaids.RemoveAll(p => p.map == map);
            runtime.mapStates.RemoveAll(s => s.map == map);
            LetterStack? stack = Find.LetterStack;
            if (stack != null)
            {
                List<Letter> letters = stack.LettersListForReading;
                for (int i = letters.Count - 1; i >= 0; i--)
                {
                    if (letters[i] is ChoiceLetter_SymbiosisCovenantMilitaryAidOffer offer && offer.triggerMap == map)
                    {
                        stack.RemoveLetter(letters[i]);
                    }
                }
            }
            return true;
        }

        public static string GetDevStatus()
        {
            Map? map = Find.CurrentMap;
            GameComponent_SymbiosisCovenantState? component = GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (map == null || component == null)
            {
                return "MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.NoMap".Translate();
            }
            CleanupState(component);
            SymbiosisCovenantMilitaryAidMapState? state = FindMapState(component, map, false);
            int remaining = state == null || Find.TickManager == null ? 0 : Math.Max(0, state.cooldownEndTick - Find.TickManager.TicksGame);
            return "MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Dev.Status".Translate(
                HasPendingEvaluation(component, map).ToString(),
                HasPendingOffer(map).ToString(),
                HasActiveCovenantAid(component, map).ToString(),
                remaining.ToStringTicksToPeriod());
        }

        private static void ProcessPendingRaids(GameComponent_SymbiosisCovenantState component, int now)
        {
            List<SymbiosisCovenantMilitaryAidPendingRaid> pending = GetState(component).pendingRaids;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (now < pending[i].evaluateAtTick)
                {
                    continue;
                }
                SymbiosisCovenantMilitaryAidPendingRaid item = pending[i];
                pending.RemoveAt(i);
                EvaluatePendingRaid(component, item);
            }
        }

        private static void EvaluatePendingRaid(GameComponent_SymbiosisCovenantState component, SymbiosisCovenantMilitaryAidPendingRaid pending)
        {
            Map? map = pending.map;
            Faction? attacker = pending.attackerFaction;
            SymbiosisCovenantMilitaryAidLevelSettings? settings = GetCurrentSettings(component);
            if (map == null || attacker == null || settings == null || !Find.Maps.Contains(map) || !map.IsPlayerHome
                || !attacker.HostileTo(Faction.OfPlayer) || pending.raidPoints <= 0f)
            {
                return;
            }

            CleanupState(component);
            if (HasPendingOffer(map) || IsInCooldown(component, map) || HasActiveCovenantAid(component, map))
            {
                return;
            }

            SymbiosisCovenantMilitaryAidDef config = SymbiosisCovenantMilitaryAidDefOf.MAP_SymbiosisCovenant_MilitaryAidConfig;
            if (!HasActiveThreatFromFaction(map, attacker, out float activeCombatPower)
                || activeCombatPower <= config.minimumInitialActiveThreatCombatPower)
            {
                return;
            }

            float supportPoints = pending.raidPoints * settings.supportPointsFactor;
            if (supportPoints <= 0f)
            {
                return;
            }
            List<Faction> responders = GetEligibleResponders(component, map, attacker, supportPoints);
            if (responders.Count == 0)
            {
                return;
            }

            float chance = Mathf.Min(config.maxOfferChance, settings.offerChance + config.GetResponderChanceBonus(responders.Count));
            if (!Rand.Chance(chance))
            {
                return;
            }
            Faction? responder = SelectResponder(component, responders, config.repeatedResponderWeight);
            if (responder != null)
            {
                new ChoiceLetter_SymbiosisCovenantMilitaryAidOffer(map, responder, attacker, pending.raidPoints, supportPoints).Send();
            }
        }

        private static SymbiosisCovenantMilitaryAidLevelSettings? GetCurrentSettings(GameComponent_SymbiosisCovenantState? component)
        {
            if (!GameComponent_SymbiosisCovenantState.IsActive || component == null || component.CovenantLevel < 3)
            {
                return null;
            }
            return SymbiosisCovenantLevelEffectUtility.GetMilitaryAidSettingsForLevel(component.CovenantLevel);
        }

        private static Faction? SelectResponder(GameComponent_SymbiosisCovenantState component, List<Faction> responders, float repeatedResponderWeight)
        {
            if (responders.Count == 0)
            {
                return null;
            }
            Faction? last = GetState(component).lastResponderFaction;
            return responders.TryRandomElementByWeight(f => f == last ? repeatedResponderWeight : 1f, out Faction selected)
                ? selected
                : responders.RandomElement();
        }

        private static bool CanProvideAid(Faction faction, Map map, float supportPoints)
        {
            if (supportPoints <= 0f)
            {
                return false;
            }
            IncidentParms parms = BuildAidParms(map, faction, supportPoints);
            // 与原版 RaidFriendly 的派系资格检查一致：这里不按最低 Combat 点数提前拒绝。
            // 实际生成时 IncidentWorker_Raid.AdjustedRaidPoints 会把极低点数抬到最低合法编组。
            if (!RaidStrategyDefOf.ImmediateAttackFriendly.Worker.CanUseWith(parms, null))
            {
                return false;
            }
            if ((int)faction.def.techLevel < (int)TechLevel.Industrial
                && !PawnsArrivalModeDefOf.EdgeWalkIn.Worker.CanUseWith(parms))
            {
                return false;
            }
            PawnGroupMakerParms makerParms = IncidentParmsUtility.GetDefaultPawnGroupMakerParms(
                PawnGroupKindDefOf.Combat, parms, ensureCanGenerateAtLeastOnePawn: true);
            return PawnGroupMakerUtility.TryGetRandomPawnGroupMaker(makerParms, out _);
        }

        private static IncidentParms BuildAidParms(Map map, Faction faction, float supportPoints)
        {
            IncidentParms parms = new()
            {
                target = map,
                faction = faction,
                points = supportPoints,
                raidStrategy = RaidStrategyDefOf.ImmediateAttackFriendly
            };
            if ((int)faction.def.techLevel >= (int)TechLevel.Industrial)
            {
                parms.raidArrivalModeForQuickMilitaryAid = true;
            }
            else
            {
                parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;
            }
            return parms;
        }

        private static bool HasActiveThreatFromFaction(Map map, Faction faction, out float combatPower)
        {
            combatPower = 0f;
            bool found = false;
            foreach (IAttackTarget target in map.attackTargetsCache.TargetsHostileToColony)
            {
                if (!GenHostility.IsActiveThreatToPlayer(target) || target is not Thing thing || thing.Faction != faction)
                {
                    continue;
                }
                found = true;
                if (thing is Pawn pawn)
                {
                    combatPower += pawn.kindDef.combatPower;
                }
            }
            return found;
        }

        private static Faction? FindActiveHostileFaction(Map map)
        {
            foreach (IAttackTarget target in map.attackTargetsCache.TargetsHostileToColony)
            {
                if (GenHostility.IsActiveThreatToPlayer(target)
                    && target is Thing thing
                    && thing.Faction != null
                    && thing.Faction.HostileTo(Faction.OfPlayer))
                {
                    return thing.Faction;
                }
            }
            return null;
        }

        private static bool HasPendingEvaluation(GameComponent_SymbiosisCovenantState component, Map map)
            => GetState(component).pendingRaids.Any(p => p.map == map);

        private static void MarkAidAccepted(GameComponent_SymbiosisCovenantState component, Map map, Faction responder)
        {
            SymbiosisCovenantMilitaryAidDef config = SymbiosisCovenantMilitaryAidDefOf.MAP_SymbiosisCovenant_MilitaryAidConfig;
            SymbiosisCovenantMilitaryAidMapState state = FindMapState(component, map, true)!;
            state.cooldownEndTick = Find.TickManager.TicksGame + Math.Max(0, config.acceptedCooldownTicks);
            state.activeAidFaction = responder;
            GetState(component).lastResponderFaction = responder;
        }

        private static SymbiosisCovenantMilitaryAidMapState? FindMapState(GameComponent_SymbiosisCovenantState component, Map map, bool create)
        {
            SymbiosisCovenantMilitaryAidRuntimeState runtime = GetState(component);
            SymbiosisCovenantMilitaryAidMapState? state = runtime.mapStates.FirstOrDefault(s => s.map == map);
            if (state != null || !create)
            {
                return state;
            }
            state = new SymbiosisCovenantMilitaryAidMapState { map = map };
            runtime.mapStates.Add(state);
            return state;
        }

        private static int CountAssistLords(Map map, Faction faction)
            => map.lordManager.lords.Count(l => l.faction == faction && l.LordJob is LordJob_AssistColony && l.AnyActivePawn);

        private static bool HasAssistLord(Map map, Faction faction)
            => CountAssistLords(map, faction) > 0;

        private static void CleanupState(GameComponent_SymbiosisCovenantState component)
        {
            SymbiosisCovenantMilitaryAidRuntimeState runtime = GetState(component);
            int now = Find.TickManager?.TicksGame ?? 0;
            runtime.pendingRaids.RemoveAll(p => p.map == null || p.attackerFaction == null || !Find.Maps.Contains(p.map));
            for (int i = runtime.mapStates.Count - 1; i >= 0; i--)
            {
                SymbiosisCovenantMilitaryAidMapState state = runtime.mapStates[i];
                if (state.map == null || !Find.Maps.Contains(state.map))
                {
                    runtime.mapStates.RemoveAt(i);
                    continue;
                }
                if (state.cooldownEndTick <= now)
                {
                    state.cooldownEndTick = 0;
                }
                if (state.activeAidFaction != null && !HasAssistLord(state.map, state.activeAidFaction))
                {
                    state.activeAidFaction = null;
                }
                if (state.cooldownEndTick <= 0 && state.activeAidFaction == null)
                {
                    runtime.mapStates.RemoveAt(i);
                }
            }
        }
    }
}
