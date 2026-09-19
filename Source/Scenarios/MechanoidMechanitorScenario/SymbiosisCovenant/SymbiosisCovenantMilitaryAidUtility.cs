using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
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

        // M1：以下字段中，activeAidFaction 仅用于显示、外交检查与查询辅助筛选。
        // 共同防卫援军的唯一身份依据是 activeAidTag。
        public Faction? activeAidFaction;
        public string? activeAidTag;
        public float activeAidSupportPoints;
        public float activeAidTriggerRaidPoints;
        public int activeAidStartTick;

        public SymbiosisCovenantMilitaryAidMapState() { }

        public void ExposeData()
        {
            Scribe_References.Look(ref map, "map");
            Scribe_Values.Look(ref cooldownEndTick, "cooldownEndTick", 0);
            Scribe_References.Look(ref activeAidFaction, "activeAidFaction");
            Scribe_Values.Look(ref activeAidTag, "activeAidTag");
            Scribe_Values.Look(
                ref activeAidSupportPoints,
                "activeAidSupportPoints",
                0f);
            Scribe_Values.Look(
                ref activeAidTriggerRaidPoints,
                "activeAidTriggerRaidPoints",
                0f);
            Scribe_Values.Look(
                ref activeAidStartTick,
                "activeAidStartTick",
                0);
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

            // M1：生成唯一 questTag，写入 RaidFriendly，再用 Tag 精确确认援军 Lord。
            string aidTag = MakeAidTag(map, responder);
            IncidentParms aidParms = BuildAidParms(map, responder, supportPoints, aidTag);
            bool executed = IncidentDefOf.RaidFriendly.Worker.TryExecute(aidParms);
            List<Lord> taggedLords = FindTaggedAidLords(map, aidTag, responder);
            if (!executed && taggedLords.Count == 0)
            {
                failureReason = "MAP_MechanoidMechanitor.Symbiosis.MilitaryAid.Failed.DeploymentImpossible".Translate(responder.NameColored);
                return false;
            }

            MarkAidAccepted(
                component,
                map,
                responder,
                aidTag,
                letter.triggerRaidPoints,
                supportPoints);
            if (taggedLords.Count == 0)
            {
                Log.Warning("[MAP-机械族机械师] 共同防卫事件已执行，但尚未找到带标记的援军 Lord；保留本次身份与接受冷却。");
            }
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
            if (state == null || string.IsNullOrEmpty(state.activeAidTag))
            {
                return false;
            }

            if (FindTaggedAidLords(map, state.activeAidTag, state.activeAidFaction).Count > 0)
            {
                return true;
            }

            // 抵达/第三方生成可能延后建立 Lord；接受冷却内保留身份供后续精确匹配。
            if (state.cooldownEndTick <= (Find.TickManager?.TicksGame ?? 0))
            {
                ClearActiveAidState(state);
            }
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

            // M1 旧存档兼容：升级前只记录了 activeAidFaction，没有唯一 activeAidTag。
            // 无法可靠判断当前地图哪个同派系 AssistColony 是旧盟约援军，
            // 因此直接清除活动援军身份（保留 cooldownEndTick）。
            for (int i = 0; i < runtime.mapStates.Count; i++)
            {
                SymbiosisCovenantMilitaryAidMapState state = runtime.mapStates[i];
                if (state.activeAidFaction != null
                    && string.IsNullOrEmpty(state.activeAidTag))
                {
                    ClearActiveAidState(state);
                }
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

        // M3：仅清除共同防卫冷却，不影响活动援军、信件、pending raid 或 activeAidTag。
        public static bool DevClearCurrentMapCooldown()
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

            SymbiosisCovenantMilitaryAidMapState state = FindMapState(component, map, true)!;
            state.cooldownEndTick = 0;
            return true;
        }

        // M3：DEV 入口。使用当前地图真实 Pending Raid 中保存的真实 raidPoints 强制发送援助询问。
        // 保留所有正常资格检查，只跳过 Rand.Chance(chance)。成功才删除 pending；失败保留 pending
        // （例如空投 Raid 尚未形成满足条件的 ActiveThreat 时，可稍后再次尝试）。
        public static bool DevForcePendingOfferForCurrentMap()
        {
            if (!Prefs.DevMode || Find.CurrentMap == null || Find.TickManager == null)
            {
                return false;
            }

            GameComponent_SymbiosisCovenantState? component =
                GameComponent_SymbiosisCovenantState.CurrentComponent;
            if (component == null)
            {
                return false;
            }

            Map map = Find.CurrentMap;

            CleanupState(component);

            SymbiosisCovenantMilitaryAidRuntimeState runtime = GetState(component);

            SymbiosisCovenantMilitaryAidPendingRaid? pending =
                runtime.pendingRaids.FirstOrDefault(item => item.map == map);

            if (pending == null)
            {
                return false;
            }

            int now = Find.TickManager.TicksGame;

            // DEV 仍保留正式的 600 tick 等待；forceOffer 只用于跳过 Rand.Chance，
            // 不能提前到 evaluateAtTick 之前评估。
            if (now < pending.evaluateAtTick)
            {
                return false;
            }

            if (!EvaluatePendingRaid(component, pending, forceOffer: true))
            {
                // 失败时保留 pending，便于敌人成为 ActiveThreat 后再次尝试。
                return false;
            }

            runtime.pendingRaids.Remove(pending);
            return true;
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
                EvaluatePendingRaid(component, item, forceOffer: false);
            }
        }

        // M3：DEV 可通过 forceOffer=true 跳过随机概率；正常游戏调用 forceOffer: false。
        // 除新增参数与返回值外，原有业务条件（ActiveThreat 阈值、Responders、SupportPoints、
        // SelectResponder、Letter 生成）均保持不变。
        private static bool EvaluatePendingRaid(
            GameComponent_SymbiosisCovenantState component,
            SymbiosisCovenantMilitaryAidPendingRaid pending,
            bool forceOffer = false)
        {
            Map? map = pending.map;
            Faction? attacker = pending.attackerFaction;
            SymbiosisCovenantMilitaryAidLevelSettings? settings = GetCurrentSettings(component);
            if (map == null || attacker == null || settings == null || !Find.Maps.Contains(map) || !map.IsPlayerHome
                || !attacker.HostileTo(Faction.OfPlayer) || pending.raidPoints <= 0f)
            {
                return false;
            }

            CleanupState(component);
            if (HasPendingOffer(map) || IsInCooldown(component, map) || HasActiveCovenantAid(component, map))
            {
                return false;
            }

            SymbiosisCovenantMilitaryAidDef config = SymbiosisCovenantMilitaryAidDefOf.MAP_SymbiosisCovenant_MilitaryAidConfig;
            if (!HasActiveThreatFromFaction(map, attacker, out float activeCombatPower)
                || activeCombatPower <= config.minimumInitialActiveThreatCombatPower)
            {
                return false;
            }

            float supportPoints = pending.raidPoints * settings.supportPointsFactor;
            if (supportPoints <= 0f)
            {
                return false;
            }
            List<Faction> responders = GetEligibleResponders(component, map, attacker, supportPoints);
            if (responders.Count == 0)
            {
                return false;
            }

            float chance = Mathf.Min(config.maxOfferChance, settings.offerChance + config.GetResponderChanceBonus(responders.Count));
            if (!forceOffer && !Rand.Chance(chance))
            {
                return false;
            }

            Faction? responder = SelectResponder(component, responders, config.repeatedResponderWeight);
            if (responder == null)
            {
                return false;
            }

            new ChoiceLetter_SymbiosisCovenantMilitaryAidOffer(map, responder, attacker, pending.raidPoints, supportPoints).Send();
            return true;
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

        private static IncidentParms BuildAidParms(
            Map map,
            Faction faction,
            float supportPoints,
            string? questTag = null)
        {
            IncidentParms parms = new()
            {
                target = map,
                faction = faction,
                points = supportPoints,
                raidStrategy = RaidStrategyDefOf.ImmediateAttackFriendly,
                questTag = questTag
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

        private static void MarkAidAccepted(
            GameComponent_SymbiosisCovenantState component,
            Map map,
            Faction responder,
            string aidTag,
            float triggerRaidPoints,
            float supportPoints)
        {
            SymbiosisCovenantMilitaryAidDef config = SymbiosisCovenantMilitaryAidDefOf.MAP_SymbiosisCovenant_MilitaryAidConfig;

            SymbiosisCovenantMilitaryAidMapState state = FindMapState(component, map, true)!;

            int now = Find.TickManager?.TicksGame ?? 0;

            state.cooldownEndTick = now + Math.Max(0, config.acceptedCooldownTicks);

            state.activeAidFaction = responder;
            state.activeAidTag = aidTag;
            state.activeAidSupportPoints = supportPoints;
            state.activeAidTriggerRaidPoints = triggerRaidPoints;
            state.activeAidStartTick = now;

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

        // M1：唯一共同防卫 questTag 前缀。不使用派系显示名/翻译字符串，保证稳定且可读。
        private const string AidQuestTagPrefix = "MAP_SymbiosisCovenantMilitaryAid";

        private static string MakeAidTag(Map map, Faction responder)
        {
            int tick = Find.TickManager?.TicksGame ?? 0;
            return AidQuestTagPrefix
                + "_"
                + map.GetUniqueLoadID()
                + "_"
                + tick
                + "_"
                + responder.GetUniqueLoadID();
        }

        // M1：清除活动援军身份，但不得清除 cooldownEndTick（援军离场≠冷却立即结束）。
        private static void ClearActiveAidState(
            SymbiosisCovenantMilitaryAidMapState state)
        {
            state.activeAidFaction = null;
            state.activeAidTag = null;
            state.activeAidSupportPoints = 0f;
            state.activeAidTriggerRaidPoints = 0f;
            state.activeAidStartTick = 0;
        }

        private static bool LordHasAidTag(Lord lord, string tag)
        {
            return lord.LordJob is LordJob_AssistColony
                && lord.questTags != null
                && lord.questTags.Contains(tag);
        }

        private static List<Lord> FindTaggedAidLords(
            Map map,
            string? tag,
            Faction? faction = null)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return new List<Lord>();
            }

            return map.lordManager.lords
                .Where(lord =>
                    lord != null
                    && lord.LordJob is LordJob_AssistColony
                    && lord.AnyActivePawn
                    && (faction == null || lord.faction == faction)
                    && lord.questTags != null
                    && lord.questTags.Contains(tag))
                .ToList();
        }

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

                // M1：活动援军身份以唯一 activeAidTag 为准。
                if (!string.IsNullOrEmpty(state.activeAidTag))
                {
                    if (state.cooldownEndTick <= now
                        && FindTaggedAidLords(state.map, state.activeAidTag, state.activeAidFaction).Count == 0)
                    {
                        ClearActiveAidState(state);
                    }
                }
                else if (state.activeAidFaction != null)
                {
                    // 旧存档兜底：无 Tag 时不再把同派系 Lord 当作盟约援军。
                    state.activeAidFaction = null;
                }

                if (state.cooldownEndTick <= 0
                    && string.IsNullOrEmpty(state.activeAidTag)
                    && state.activeAidFaction == null)
                {
                    runtime.mapStates.RemoveAt(i);
                }
            }
        }

        // M3：共同防卫 DEV 实时状态快照。M1 的 activeAidTag 也在此进入 DEV 校验入口。
        public sealed class SymbiosisCovenantMilitaryAidDevSnapshot
        {
            public Map? Map;
            public int CovenantLevel;

            public float BaseOfferChance;
            public float MaxOfferChance;
            public float SupportPointsFactor;

            public Faction? CurrentThreatFaction;
            public float CurrentThreatCombatPower;

            public int EligibleResponderCount;
            public float ResponderChanceBonus;
            public float EffectiveOfferChance;

            public bool PendingEvaluation;

            // M3：真实监听到的 Pending Raid 数据（由 RaidEnemy Harmony Postfix 写入）。
            // 供 DEV 快照、DEV 窗口与未来 AutoTest 直接确认监听结果与真实 Raid Points。
            public Faction? PendingRaidAttackerFaction;
            public float PendingRaidPoints;
            public int PendingRaidEvaluateAtTick;
            public int PendingRaidTicksRemaining;

            public bool PendingOffer;

            public Faction? LastResponderFaction;

            public Faction? ActiveAidFaction;
            public string? ActiveAidTag;

            public float ActiveAidTriggerRaidPoints;
            public float ActiveAidSupportPoints;
            public int ActiveAidStartTick;

            public int TaggedAssistLordCount;

            public int CooldownRemainingTicks;
        }

        public static SymbiosisCovenantMilitaryAidDevSnapshot GetDevSnapshot(
            GameComponent_SymbiosisCovenantState component,
            Map? map)
        {
            SymbiosisCovenantMilitaryAidDef config = SymbiosisCovenantMilitaryAidDefOf.MAP_SymbiosisCovenant_MilitaryAidConfig;
            SymbiosisCovenantMilitaryAidLevelSettings? settings = GetCurrentSettings(component);

            SymbiosisCovenantMilitaryAidDevSnapshot snap = new SymbiosisCovenantMilitaryAidDevSnapshot
            {
                Map = map,
                CovenantLevel = component.CovenantLevel,
                MaxOfferChance = config.maxOfferChance,
                LastResponderFaction = GetState(component).lastResponderFaction
            };

            if (settings != null)
            {
                snap.BaseOfferChance = settings.offerChance;
                snap.SupportPointsFactor = settings.supportPointsFactor;
            }

            if (map != null)
            {
                SymbiosisCovenantMilitaryAidPendingRaid? pending =
                    GetState(component).pendingRaids
                        .FirstOrDefault(item => item.map == map);

                snap.PendingEvaluation = pending != null;
                if (pending != null)
                {
                    snap.PendingRaidAttackerFaction = pending.attackerFaction;
                    snap.PendingRaidPoints = pending.raidPoints;
                    snap.PendingRaidEvaluateAtTick = pending.evaluateAtTick;
                    snap.PendingRaidTicksRemaining = Find.TickManager != null
                        ? Math.Max(0, pending.evaluateAtTick - Find.TickManager.TicksGame)
                        : 0;
                }

                snap.PendingOffer = HasPendingOffer(map);

                SymbiosisCovenantMilitaryAidMapState? state = FindMapState(component, map, false);
                if (state != null)
                {
                    snap.ActiveAidFaction = state.activeAidFaction;
                    snap.ActiveAidTag = state.activeAidTag;
                    snap.ActiveAidTriggerRaidPoints = state.activeAidTriggerRaidPoints;
                    snap.ActiveAidSupportPoints = state.activeAidSupportPoints;
                    snap.ActiveAidStartTick = state.activeAidStartTick;
                    snap.CooldownRemainingTicks = Find.TickManager != null
                        ? Math.Max(0, state.cooldownEndTick - Find.TickManager.TicksGame)
                        : 0;
                    if (!string.IsNullOrEmpty(state.activeAidTag))
                    {
                        snap.TaggedAssistLordCount =
                            FindTaggedAidLords(map, state.activeAidTag, state.activeAidFaction).Count;
                    }
                }
            }

            // 实时威胁与合法响应成员（仅 L3+ 且存在当前威胁时计算有效概率）。
            if (map != null && settings != null && component.CovenantLevel >= 3)
            {
                Faction? attacker = FindActiveHostileFaction(map);
                if (attacker != null)
                {
                    snap.CurrentThreatFaction = attacker;
                    if (HasActiveThreatFromFaction(map, attacker, out float combatPower))
                    {
                        snap.CurrentThreatCombatPower = combatPower;
                    }

                    // 注意：DEV 估算用的援军点数 = 当前威胁 CombatPower × supportPointsFactor，
                    // 仅用于资格检查候选，不代表真实触发 Raid Points。
                    float estimatedSupport = snap.CurrentThreatCombatPower * settings.supportPointsFactor;
                    List<Faction> responders = GetEligibleResponders(component, map, attacker, estimatedSupport);
                    snap.EligibleResponderCount = responders.Count;
                    snap.ResponderChanceBonus = config.GetResponderChanceBonus(responders.Count);
                    // M3：合法响应成员为 0 时，正式 EvaluatePendingRaid 会直接退出，
                    // 因此有效响应概率必须为 0%（原先由独立 Harmony Patch 修正，现收回此处）。
                    snap.EffectiveOfferChance = responders.Count > 0
                        ? Mathf.Min(
                            config.maxOfferChance,
                            snap.BaseOfferChance + snap.ResponderChanceBonus)
                        : 0f;
                }
            }

            return snap;
        }
    }
}
