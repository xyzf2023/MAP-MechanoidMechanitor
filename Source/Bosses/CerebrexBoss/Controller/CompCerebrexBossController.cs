using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_CerebrexBossController : CompProperties
    {
        public CompProperties_CerebrexBossController()
        {
            compClass = typeof(CompCerebrexBossController);
        }
    }

    /// <summary>
    /// 主脑（CerebrexCore）独立战斗控制器。所有战斗状态都由本组件保存在主脑建筑实例上，
    /// 不写入 MapComponent / GameComponent / WorldComponent / 任务组件。
    /// 对原版主脑的地图生成、稳定器、900 tick 互动、结局逻辑一律不修改。
    /// </summary>
    public sealed class CompCerebrexBossController : ThingComp
    {
        public const int FirstSummonDelayTicks = 1200;
        public const int SummonIntervalTicks = 900;
        public const int MechsPerWave = 8;
        public const int MaxLivingSummonedMechs = 32;

        public const int DropRetryDelayTicks = 250;
        public const int MaxDropRetryAttempts = 20;

        public const int EmpCooldownMinTicks = 2400;
        public const int EmpCooldownMaxTicks = 3600;
        public const int EmpBaseDurationTicks = 1200;
        public const float EmpRadius = 75f;
        public const float EmpPropagationSpeed = 1f;
        public const float OriginalShockwaveRadius = 30.9f;

        public const int BandwidthCooldownMinTicks = 3000;
        public const int BandwidthCooldownMaxTicks = 4500;
        public const int BandwidthDurationTicks = 1800;

        public const int MobileCombatCheckIntervalTicks = 60;

        private bool initialized;
        private bool stopped;

        private int nextSummonTick;
        private int nextEmpTick;
        private int nextBandwidthTick;
        private int mobileCombatTickCounter;

        private List<Pawn> summonedMechs = new List<Pawn>();
        private List<PawnKindDef> pendingSummonKinds = new List<PawnKindDef>();
        private int summonDropRetryCount;
        private Lord? assaultLord;

        private List<PendingCerebrexEmpHit> pendingEmpHits = new List<PendingCerebrexEmpHit>();
        private Dictionary<Thing, int> disabledPowerBuildings = new Dictionary<Thing, int>();

        private bool bandwidthInterferenceActive;
        private Pawn? bandwidthTarget;
        private Pawn? bandwidthOverseer;
        private int bandwidthInterferenceEndTick;
        private List<Pawn> bandwidthBerserkPawns = new List<Pawn>();
        private List<Pawn> bandwidthTargetsUsedThisBattle = new List<Pawn>();

        public Lord? GetAssaultLord() => assaultLord;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (initialized)
            {
                return;
            }

            InitializeTimers();
            initialized = true;
        }

        private void InitializeTimers()
        {
            int now = Find.TickManager.TicksGame;
            nextSummonTick = now + FirstSummonDelayTicks;
            nextEmpTick = now + Rand.RangeInclusive(EmpCooldownMinTicks, EmpCooldownMaxTicks);
            nextBandwidthTick = now + Rand.RangeInclusive(BandwidthCooldownMinTicks, BandwidthCooldownMaxTicks);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref initialized, "initialized", false);
            Scribe_Values.Look(ref stopped, "stopped", false);
            Scribe_Values.Look(ref nextSummonTick, "nextSummonTick", 0);
            Scribe_Values.Look(ref nextEmpTick, "nextEmpTick", 0);
            Scribe_Values.Look(ref nextBandwidthTick, "nextBandwidthTick", 0);
            Scribe_Collections.Look(ref summonedMechs, "summonedMechs", LookMode.Reference);
            Scribe_Collections.Look(ref pendingSummonKinds, "pendingSummonKinds", LookMode.Def);
            Scribe_Values.Look(ref summonDropRetryCount, "summonDropRetryCount", 0);
            Scribe_References.Look(ref assaultLord, "assaultLord");
            Scribe_Collections.Look(ref pendingEmpHits, "pendingEmpHits", LookMode.Deep);
            Scribe_Collections.Look(ref disabledPowerBuildings, "disabledPowerBuildings", LookMode.Reference, LookMode.Value);
            Scribe_Values.Look(ref bandwidthInterferenceActive, "bandwidthInterferenceActive", false);
            Scribe_References.Look(ref bandwidthTarget, "bandwidthTarget");
            Scribe_References.Look(ref bandwidthOverseer, "bandwidthOverseer");
            Scribe_Values.Look(ref bandwidthInterferenceEndTick, "bandwidthInterferenceEndTick", 0);
            Scribe_Collections.Look(ref bandwidthBerserkPawns, "bandwidthBerserkPawns", LookMode.Reference);
            Scribe_Collections.Look(ref bandwidthTargetsUsedThisBattle, "bandwidthTargetsUsedThisBattle", LookMode.Reference);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                PostLoadCleanup();
            }
        }

        private void PostLoadCleanup()
        {
            summonedMechs ??= new List<Pawn>();
            pendingSummonKinds ??= new List<PawnKindDef>();
            pendingEmpHits ??= new List<PendingCerebrexEmpHit>();
            bandwidthBerserkPawns ??= new List<Pawn>();
            bandwidthTargetsUsedThisBattle ??= new List<Pawn>();
            disabledPowerBuildings ??= new Dictionary<Thing, int>();

            summonedMechs.RemoveAll(p => p == null || p.Dead || p.Destroyed || p.Discarded);
            bandwidthBerserkPawns.RemoveAll(p => p == null || p.Dead || p.Destroyed || p.Discarded);
            bandwidthTargetsUsedThisBattle.RemoveAll(p => p == null || p.Dead || p.Destroyed || p.Discarded);
            pendingEmpHits.RemoveAll(h => h == null || h.target == null || h.target.Destroyed || h.target.Discarded);

            int nowTicks = Find.TickManager.TicksGame;
            List<Thing> expired = new List<Thing>();
            foreach (KeyValuePair<Thing, int> kv in disabledPowerBuildings)
            {
                if (kv.Key == null || kv.Key.Destroyed)
                {
                    if (kv.Key != null)
                    {
                        expired.Add(kv.Key);
                    }

                    continue;
                }

                if (kv.Value > nowTicks)
                {
                    CerebrexPowerDisruptionUtility.Register(kv.Key, kv.Value);
                }
            }

            foreach (Thing t in expired)
            {
                disabledPowerBuildings.Remove(t);
            }

            disabledPowerBuildings.Remove(null!);

            if (bandwidthInterferenceActive)
            {
                bool valid = bandwidthTarget != null
                    && !bandwidthTarget.Dead
                    && !bandwidthTarget.Destroyed
                    && !bandwidthTarget.Discarded
                    && bandwidthOverseer != null
                    && bandwidthOverseer.mechanitor != null
                    && ModsConfig.OdysseyActive;
                if (!valid)
                {
                    EndBandwidthInterference(startCooldown: false, reason: "loadfix");
                }
                else
                {
                    HediffDef? def = DefDatabase<HediffDef>.GetNamedSilentFail("MAP_CerebrexBandwidthInterference");
                    if (def != null && bandwidthTarget!.health.hediffSet.GetFirstHediffOfDef(def) == null)
                    {
                        EndBandwidthInterference(startCooldown: false, reason: "loadfix");
                    }
                }
            }

            if (summonedMechs.Count > 0 && assaultLord == null && parent.Spawned && parent.Map != null && CanRunAutomatically())
            {
                EnsureAssaultLord();
            }
        }

        public override void CompTick()
        {
            if (!initialized || !ModsConfig.OdysseyActive || parent.Map == null)
            {
                return;
            }

            // 1. 处理待命中的 EMP 命中（即使关系变化，已释放的冲击波仍可完成）。
            ProcessPendingEmpHits();

            // 2. 清理过期的建筑瘫痪记录。
            CleanExpiredDisabledPowerBuildings();

            // 3. 检查正在进行的带宽干扰。
            if (bandwidthInterferenceActive)
            {
                TickBandwidthInterference();
            }

            // 4. 若主脑已停止，返回。
            if (stopped)
            {
                return;
            }

            // 5. 检查自动运行条件。
            if (!CanRunAutomatically())
            {
                return;
            }

            // 6. 每 60 tick 为全部机械巢单位补充机动作战。
            mobileCombatTickCounter++;
            if (mobileCombatTickCounter >= MobileCombatCheckIntervalTicks)
            {
                mobileCombatTickCounter = 0;
                EnsureMobileCombatForAllMechanoids();
            }

            // 7. 检查增援计时。
            int now = Find.TickManager.TicksGame;
            if (now >= nextSummonTick)
            {
                TrySummonWave(force: false);
            }

            // 8. 检查 EMP 计时。
            if (now >= nextEmpTick)
            {
                TriggerEmpShockwave();
                nextEmpTick = now + Rand.RangeInclusive(EmpCooldownMinTicks, EmpCooldownMaxTicks);
            }

            // 9. 若没有正在进行的带宽干扰，检查带宽干扰计时。
            if (!bandwidthInterferenceActive && now >= nextBandwidthTick)
            {
                TryStartBandwidthInterference(force: false);
            }
        }

        private bool CanRunAutomatically()
        {
            if (!ModsConfig.OdysseyActive
                || stopped
                || !parent.Spawned
                || parent.Map == null
                || parent.Faction == null
                || Faction.OfPlayer == null
                || !parent.Faction.HostileTo(Faction.OfPlayer))
            {
                return false;
            }

            return parent.Map.mapPawns.AllPawnsSpawned.Any(
                p => p != null && !p.Dead && p.Faction == Faction.OfPlayer);
        }

        private void EnsureMobileCombatForAllMechanoids()
        {
            if (parent.Map == null)
            {
                return;
            }

            foreach (Pawn pawn in parent.Map.mapPawns.AllPawnsSpawned)
            {
                if (pawn != null && !pawn.Dead && pawn.RaceProps.IsMechanoid && pawn.Faction == Faction.OfMechanoids)
                {
                    MechanoidMechanitorWorkModeUtility.EnsureMobileCombatHediff(pawn);
                }
            }
        }

        // ----------------------------------------------------------------
        // 机械族增援
        // ----------------------------------------------------------------

        private int CountLivingSummonedMechs()
        {
            int count = 0;
            for (int i = summonedMechs.Count - 1; i >= 0; i--)
            {
                Pawn? p = summonedMechs[i];
                if (p == null || p.Dead || p.Destroyed || p.Discarded)
                {
                    summonedMechs.RemoveAt(i);
                    continue;
                }

                count++;
            }

            return count;
        }

        public void RegisterPendingSummonKind(PawnKindDef kind)
        {
            if (kind != null && !pendingSummonKinds.Contains(kind))
            {
                pendingSummonKinds.Add(kind);
            }
        }

        private void TrySummonWave(bool force)
        {
            if (parent.Map == null)
            {
                return;
            }

            Faction? mechFaction = Faction.OfMechanoids;
            if (mechFaction == null)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;

            // 先处理失败空投的重试。
            if (pendingSummonKinds.Count > 0)
            {
                if (summonDropRetryCount >= MaxDropRetryAttempts)
                {
                    pendingSummonKinds.Clear();
                    summonDropRetryCount = 0;
                    nextSummonTick = now + SummonIntervalTicks;
                    return;
                }

                EnsureAssaultLord();
                int living = CountLivingSummonedMechs();
                int avail = MaxLivingSummonedMechs - living;
                if (avail <= 0)
                {
                    summonDropRetryCount++;
                    nextSummonTick = now + DropRetryDelayTicks;
                    return;
                }

                List<PawnKindDef> toRetry = pendingSummonKinds.Take(avail).ToList();
                pendingSummonKinds.Clear();
                CerebrexBossSpawnUtility.SpawnSummonWave(parent.Map, toRetry, summonedMechs, parent.Position, mechFaction, this);
                summonDropRetryCount++;
                nextSummonTick = pendingSummonKinds.Count > 0
                    ? now + DropRetryDelayTicks
                    : now + SummonIntervalTicks;
                return;
            }

            int livingCount = CountLivingSummonedMechs();
            int availableSlots = MaxLivingSummonedMechs - livingCount;
            int count = Mathf.Min(MechsPerWave, availableSlots);
            if (count <= 0)
            {
                nextSummonTick = now + SummonIntervalTicks;
                if (force)
                {
                    Messages.Message("机械族增援已达上限（32）。", MessageTypeDefOf.RejectInput);
                }

                return;
            }

            List<PawnGenOption> pool = JusticeBossMechPoolUtility.BuildCombatPool();
            List<PawnKindDef> chosen = new List<PawnKindDef>();
            for (int i = 0; i < count; i++)
            {
                PawnKindDef? kind = JusticeBossMechPoolUtility.PickWeighted(pool);
                if (kind != null)
                {
                    chosen.Add(kind);
                }
            }

            if (chosen.Count == 0)
            {
                nextSummonTick = now + SummonIntervalTicks;
                return;
            }

            EnsureAssaultLord();
            CerebrexBossSpawnUtility.SpawnSummonWave(parent.Map, chosen, summonedMechs, parent.Position, mechFaction, this);

            nextSummonTick = pendingSummonKinds.Count > 0
                ? now + DropRetryDelayTicks
                : now + SummonIntervalTicks;
        }

        private void EnsureAssaultLord()
        {
            if (parent.Map == null)
            {
                return;
            }

            assaultLord = CerebrexBossLordUtility.EnsureAssaultLord(parent.Map, Faction.OfMechanoids, parent.thingIDNumber);
        }

        // ----------------------------------------------------------------
        // EMP 冲击波
        // ----------------------------------------------------------------

        private void TriggerEmpShockwave()
        {
            if (parent.Destroyed || parent.Map == null)
            {
                return;
            }

            PlayEmpShockwaveVisual();

            Map map = parent.Map;
            int now = Find.TickManager.TicksGame;
            HashSet<Thing> addedThisRelease = new HashSet<Thing>();

            foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
            {
                if (p == null || addedThisRelease.Contains(p) || !IsValidEmpTarget(p))
                {
                    continue;
                }

                float distance = parent.Position.DistanceTo(p.Position);
                if (distance > EmpRadius)
                {
                    continue;
                }

                int delay = Mathf.CeilToInt(distance / EmpPropagationSpeed);
                pendingEmpHits.Add(new PendingCerebrexEmpHit(p, now + delay));
                addedThisRelease.Add(p);
            }

            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (thing is Building b && !addedThisRelease.Contains(b) && IsValidEmpTarget(b))
                {
                    float distance = parent.Position.DistanceTo(b.Position);
                    if (distance > EmpRadius)
                    {
                        continue;
                    }

                    int delay = Mathf.CeilToInt(distance / EmpPropagationSpeed);
                    pendingEmpHits.Add(new PendingCerebrexEmpHit(b, now + delay));
                    addedThisRelease.Add(b);
                }
            }
        }

        private void ProcessPendingEmpHits()
        {
            if (pendingEmpHits.Count == 0 || parent.Map == null)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            for (int i = pendingEmpHits.Count - 1; i >= 0; i--)
            {
                PendingCerebrexEmpHit hit = pendingEmpHits[i];
                if (now < hit.impactTick)
                {
                    continue;
                }

                Thing? t = hit.target;
                if (t == null || t.Destroyed || t.Discarded)
                {
                    pendingEmpHits.RemoveAt(i);
                    continue;
                }

                if (t.Map != parent.Map)
                {
                    pendingEmpHits.RemoveAt(i);
                    continue;
                }

                float distance = parent.Position.DistanceTo(t.Position);
                if (distance > EmpRadius)
                {
                    pendingEmpHits.RemoveAt(i);
                    continue;
                }

                if (!IsValidEmpTarget(t))
                {
                    pendingEmpHits.RemoveAt(i);
                    continue;
                }

                ApplyEmpToTarget(t);
                pendingEmpHits.RemoveAt(i);
            }
        }

        private static bool IsValidEmpTarget(Thing t)
        {
            Faction? mechHiveFaction = Faction.OfMechanoids;
            if (t is Pawn p)
            {
                if (!p.RaceProps.IsMechanoid)
                {
                    return false;
                }

                if (p.Dead || p.Downed)
                {
                    return false;
                }

                if (!p.Spawned)
                {
                    return false;
                }

                if (mechHiveFaction != null && p.Faction == mechHiveFaction)
                {
                    return false;
                }

                return true;
            }

            if (t is Building b)
            {
                CompPowerTrader? trader = b.GetComp<CompPowerTrader>();
                if (trader == null)
                {
                    return false;
                }

                if (trader.Props.PowerConsumption <= 0f)
                {
                    return false;
                }

                if (mechHiveFaction != null && b.Faction == mechHiveFaction)
                {
                    return false;
                }

                return true;
            }

            return false;
        }

        private static int CalculateEmpDuration(Thing target)
        {
            float resistance = 0f;
            StatDef? stat = DefDatabase<StatDef>.GetNamedSilentFail("EMPResistance");
            if (stat != null && !stat.Worker.IsDisabledFor(target))
            {
                resistance = target.GetStatValue(stat);
            }

            return Mathf.RoundToInt(EmpBaseDurationTicks * Mathf.Clamp01(1f - resistance));
        }

        private void ApplyEmpToTarget(Thing target)
        {
            int duration = CalculateEmpDuration(target);
            if (duration <= 0)
            {
                return;
            }

            if (target is Pawn p)
            {
                p.stances?.stunner?.StunFor(duration, parent, addBattleLog: true);
            }
            else if (target is Building b)
            {
                int until = Find.TickManager.TicksGame + duration;
                disabledPowerBuildings[b] = until;
                CerebrexPowerDisruptionUtility.Register(b, until);
                b.GetComp<CompStunnable>()?.StunHandler.StunFor(duration, parent);
                PlayEmpArcEffect(b);
            }
        }

        private void PlayEmpShockwaveVisual()
        {
            EffecterDef? effecterDef = DefDatabase<EffecterDef>.GetNamedSilentFail("BlastMechBandShockwave");
            SoundDef? soundDef = DefDatabase<SoundDef>.GetNamedSilentFail("Explosion_MechBandShockwave");
            float visualScale = EmpRadius / OriginalShockwaveRadius;
            Effecter? effecter = effecterDef?.Spawn(parent.Position, parent.Map, visualScale);
            effecter?.Cleanup();
            soundDef?.PlayOneShot(new TargetInfo(parent.Position, parent.Map));
        }

        private void PlayEmpArcEffect(Thing target)
        {
            EffecterDef? arcDef = DefDatabase<EffecterDef>.GetNamedSilentFail("MechBandElectricityArc");
            Effecter? arc = arcDef?.Spawn(target.Position, parent.Map);
            arc?.Cleanup();
        }

        private void CleanExpiredDisabledPowerBuildings()
        {
            if (disabledPowerBuildings.Count == 0)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            List<Thing>? remove = null;
            foreach (KeyValuePair<Thing, int> kv in disabledPowerBuildings)
            {
                if (kv.Key == null || kv.Key.Destroyed || now >= kv.Value)
                {
                    if (kv.Key != null)
                    {
                        remove ??= new List<Thing>();
                        remove.Add(kv.Key);
                    }
                }
            }

            if (remove != null)
            {
                foreach (Thing t in remove)
                {
                    disabledPowerBuildings.Remove(t);
                }
            }

            disabledPowerBuildings.Remove(null!);
        }

        // ----------------------------------------------------------------
        // 带宽干扰
        // ----------------------------------------------------------------

        private bool TryStartBandwidthInterference(bool force)
        {
            if (bandwidthInterferenceActive)
            {
                if (force)
                {
                    Messages.Message("主脑带宽干扰已在运行。", MessageTypeDefOf.RejectInput);
                }

                return false;
            }

            Pawn? target = FindBandwidthTarget();
            if (target == null)
            {
                if (force)
                {
                    Messages.Message("未找到合适的带宽干扰目标。", MessageTypeDefOf.RejectInput);
                }

                return false;
            }

            Pawn? overseer = target.GetOverseer();
            if (overseer == null || overseer.mechanitor == null)
            {
                if (force)
                {
                    Messages.Message("目标监管者无效。", MessageTypeDefOf.RejectInput);
                }

                return false;
            }

            HediffDef? interferenceDef = DefDatabase<HediffDef>.GetNamedSilentFail("MAP_CerebrexBandwidthInterference");
            if (interferenceDef == null)
            {
                return false;
            }

            if (target.health.hediffSet.GetFirstHediffOfDef(interferenceDef) != null)
            {
                if (force)
                {
                    Messages.Message("目标已处于带宽干扰中。", MessageTypeDefOf.RejectInput);
                }

                return false;
            }

            List<Pawn> beforeControlled = new List<Pawn>(overseer.mechanitor.ControlledPawns);

            Hediff hediff = HediffMaker.MakeHediff(interferenceDef, target);
            target.health.AddHediff(hediff);

            bandwidthTargetsUsedThisBattle.Add(target);

            bandwidthInterferenceActive = true;
            bandwidthTarget = target;
            bandwidthOverseer = overseer;
            bandwidthInterferenceEndTick = Find.TickManager.TicksGame + BandwidthDurationTicks;

            overseer.mechanitor.Notify_BandwidthChanged();

            List<Pawn> afterControlled = overseer.mechanitor.ControlledPawns;
            foreach (Pawn p in beforeControlled)
            {
                if (p == null || p.Dead || p.Destroyed || p.Discarded)
                {
                    continue;
                }

                if (afterControlled.Contains(p))
                {
                    continue;
                }

                if (p.Faction != Faction.OfPlayer || !p.RaceProps.IsMechanoid || !p.Spawned || p.Downed)
                {
                    continue;
                }

                TryBerserk(p);
            }

            return true;
        }

        private Pawn? FindBandwidthTarget()
        {
            if (parent.Map == null)
            {
                return null;
            }

            Map map = parent.Map;
            HediffDef? interferenceDef = DefDatabase<HediffDef>.GetNamedSilentFail("MAP_CerebrexBandwidthInterference");
            float bestCost = -1f;
            List<Pawn> candidates = new List<Pawn>();

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn == null
                    || pawn.Dead
                    || pawn.Downed
                    || pawn.Destroyed
                    || pawn.Discarded)
                {
                    continue;
                }

                if (!pawn.RaceProps.IsMechanoid)
                {
                    continue;
                }

                if (pawn.Faction != Faction.OfPlayer)
                {
                    continue;
                }

                Pawn? overseer = pawn.GetOverseer();
                if (overseer == null || overseer.mechanitor == null)
                {
                    continue;
                }

                if (!overseer.mechanitor.ControlledPawns.Contains(pawn))
                {
                    continue;
                }

                if (GameComponent_MechanoidMechanitorRegistry.HasPersistentRecord(pawn))
                {
                    continue;
                }

                if (bandwidthTargetsUsedThisBattle.Contains(pawn))
                {
                    continue;
                }

                if (interferenceDef != null && pawn.health.hediffSet.GetFirstHediffOfDef(interferenceDef) != null)
                {
                    continue;
                }

                float cost = pawn.GetStatValue(StatDefOf.BandwidthCost);
                if (cost > bestCost)
                {
                    bestCost = cost;
                    candidates.Clear();
                    candidates.Add(pawn);
                }
                else if (cost == bestCost)
                {
                    candidates.Add(pawn);
                }
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            return candidates[Rand.Range(0, candidates.Count)];
        }

        private void TryBerserk(Pawn p)
        {
            bool started = p.mindState.mentalStateHandler.TryStartMentalState(
                MentalStateDefOf.BerserkMechanoid,
                reason: null,
                forced: true,
                forceWake: true);
            if (!started)
            {
                return;
            }

            HediffDef? markerDef = DefDatabase<HediffDef>.GetNamedSilentFail("MAP_CerebrexBandwidthBerserkMarker");
            if (markerDef != null && p.health.hediffSet.GetFirstHediffOfDef(markerDef) == null)
            {
                p.health.AddHediff(HediffMaker.MakeHediff(markerDef, p));
            }

            if (p.MentalState != null)
            {
                p.MentalState.forceRecoverAfterTicks = BandwidthDurationTicks;
            }

            if (!bandwidthBerserkPawns.Contains(p))
            {
                bandwidthBerserkPawns.Add(p);
            }
        }

        private void TickBandwidthInterference()
        {
            int now = Find.TickManager.TicksGame;
            bool shouldEnd = false;

            if (stopped || !parent.Spawned || parent.Destroyed)
            {
                shouldEnd = true;
            }
            else if (parent.Faction == null || Faction.OfPlayer == null || !parent.Faction.HostileTo(Faction.OfPlayer))
            {
                shouldEnd = true;
            }
            else if (bandwidthOverseer == null || bandwidthOverseer.Dead || bandwidthOverseer.Destroyed || bandwidthOverseer.mechanitor == null)
            {
                shouldEnd = true;
            }
            else if (bandwidthTarget == null || bandwidthTarget.Dead || bandwidthTarget.Destroyed || bandwidthTarget.Discarded)
            {
                shouldEnd = true;
            }
            else if (bandwidthTarget.Map != parent.Map)
            {
                shouldEnd = true;
            }
            else
            {
                HediffDef? interferenceDef = DefDatabase<HediffDef>.GetNamedSilentFail("MAP_CerebrexBandwidthInterference");
                if (interferenceDef != null && bandwidthTarget.health.hediffSet.GetFirstHediffOfDef(interferenceDef) == null)
                {
                    shouldEnd = true;
                }
            }

            if (!shouldEnd && bandwidthBerserkPawns.Count > 0)
            {
                bool allInactive = true;
                foreach (Pawn p in bandwidthBerserkPawns)
                {
                    if (p == null || p.Dead || p.Destroyed || p.Discarded)
                    {
                        continue;
                    }

                    if (p.Spawned && p.Map == parent.Map && !p.Downed)
                    {
                        allInactive = false;
                        break;
                    }
                }

                if (allInactive)
                {
                    shouldEnd = true;
                }
            }

            // 某个本轮狂暴者本人重新出现在原监管者的受控列表中 -> 结束整轮。
            if (!shouldEnd && bandwidthBerserkPawns.Count > 0 && bandwidthOverseer != null)
            {
                var tracker = bandwidthOverseer.mechanitor;
                if (tracker != null)
                {
                    foreach (Pawn p in bandwidthBerserkPawns)
                    {
                        if (p == null)
                        {
                            continue;
                        }

                        if (tracker.ControlledPawns.Contains(p))
                        {
                            shouldEnd = true;
                            break;
                        }
                    }
                }
            }

            // 目标未进入本轮狂暴名单，且目标倒地/死亡 -> 结束整轮。
            if (!shouldEnd && bandwidthTarget != null && !bandwidthBerserkPawns.Contains(bandwidthTarget))
            {
                if (bandwidthTarget.Downed || bandwidthTarget.Dead)
                {
                    shouldEnd = true;
                }
            }

            if (!shouldEnd && now >= bandwidthInterferenceEndTick)
            {
                shouldEnd = true;
            }

            if (shouldEnd)
            {
                EndBandwidthInterference(startCooldown: true, reason: "tick");
            }
            else
            {
                CleanupDownedBerserkMarkers();
            }
        }

        private void CleanupDownedBerserkMarkers()
        {
            for (int i = bandwidthBerserkPawns.Count - 1; i >= 0; i--)
            {
                Pawn? p = bandwidthBerserkPawns[i];
                if (p == null || p.Dead || p.Destroyed || p.Discarded || p.Downed)
                {
                    RemoveBerserkMarker(p);
                    bandwidthBerserkPawns.RemoveAt(i);
                }
            }
        }

        private void RemoveBerserkMarker(Pawn? p)
        {
            if (p == null)
            {
                return;
            }

            HediffDef? markerDef = DefDatabase<HediffDef>.GetNamedSilentFail("MAP_CerebrexBandwidthBerserkMarker");
            if (markerDef == null)
            {
                return;
            }

            Hediff? marker = p.health.hediffSet.GetFirstHediffOfDef(markerDef);
            if (marker != null)
            {
                p.health.RemoveHediff(marker);
            }
        }

        private void EndBandwidthInterference(bool startCooldown, string reason)
        {
            for (int i = bandwidthBerserkPawns.Count - 1; i >= 0; i--)
            {
                Pawn? p = bandwidthBerserkPawns[i];
                if (p == null)
                {
                    continue;
                }

                HediffDef? markerDef = DefDatabase<HediffDef>.GetNamedSilentFail("MAP_CerebrexBandwidthBerserkMarker");
                bool hasMarker = markerDef != null && p.health.hediffSet.GetFirstHediffOfDef(markerDef) != null;
                bool isBerserk = p.MentalState != null && p.MentalState.def == MentalStateDefOf.BerserkMechanoid;
                if (hasMarker && isBerserk)
                {
                    p.mindState.mentalStateHandler.CurState?.RecoverFromState();
                }

                RemoveBerserkMarker(p);
            }

            if (bandwidthTarget != null)
            {
                HediffDef? interferenceDef = DefDatabase<HediffDef>.GetNamedSilentFail("MAP_CerebrexBandwidthInterference");
                if (interferenceDef != null)
                {
                    Hediff? h = bandwidthTarget.health.hediffSet.GetFirstHediffOfDef(interferenceDef);
                    if (h != null)
                    {
                        bandwidthTarget.health.RemoveHediff(h);
                    }
                }
            }

            bandwidthOverseer?.mechanitor?.Notify_BandwidthChanged();

            bandwidthBerserkPawns.Clear();
            bandwidthTarget = null;
            bandwidthOverseer = null;
            bandwidthInterferenceEndTick = 0;
            bandwidthInterferenceActive = false;

            if (startCooldown)
            {
                nextBandwidthTick = Find.TickManager.TicksGame + Rand.RangeInclusive(BandwidthCooldownMinTicks, BandwidthCooldownMaxTicks);
            }
        }

        // ----------------------------------------------------------------
        // 主脑关闭 / 销毁清理
        // ----------------------------------------------------------------

        public void Notify_CoreDeactivationStarted()
        {
            stopped = true;
            EndBandwidthInterference(startCooldown: false, reason: "deactivation");
            pendingEmpHits.Clear();
            bandwidthTargetsUsedThisBattle.Clear();
            pendingSummonKinds.Clear();
            summonDropRetryCount = 0;
        }

        private void FullCleanup(string reason)
        {
            stopped = true;
            EndBandwidthInterference(startCooldown: false, reason: reason);
            pendingEmpHits.Clear();
            bandwidthTargetsUsedThisBattle.Clear();
            pendingSummonKinds.Clear();
            summonDropRetryCount = 0;
            disabledPowerBuildings.Clear();
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            FullCleanup("destroy");
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            FullCleanup("despawn");
        }

        // ----------------------------------------------------------------
        // DEV 按钮
        // ----------------------------------------------------------------

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                yield return g;
            }

            if (!ModsConfig.OdysseyActive || !DebugSettings.godMode)
            {
                yield break;
            }

            if (!parent.Spawned || parent.Map == null)
            {
                yield break;
            }

            yield return new Command_Action
            {
                defaultLabel = "DEV：立即召唤机械族",
                action = DevSummon,
            };

            yield return new Command_Action
            {
                defaultLabel = "DEV：立即进行带宽干扰",
                action = DevBandwidth,
            };

            yield return new Command_Action
            {
                defaultLabel = "DEV：停止本次带宽干扰",
                action = DevStopBandwidth,
            };

            yield return new Command_Action
            {
                defaultLabel = "DEV：释放EMP冲击",
                action = DevEmp,
            };
        }

        private void DevSummon()
        {
            if (!ModsConfig.OdysseyActive || parent.Map == null)
            {
                return;
            }

            if (pendingSummonKinds.Count > 0)
            {
                Messages.Message("存在失败空投正在重试，请等待。", MessageTypeDefOf.RejectInput);
                return;
            }

            TrySummonWave(force: true);
        }

        private void DevBandwidth()
        {
            if (!ModsConfig.OdysseyActive)
            {
                return;
            }

            TryStartBandwidthInterference(force: true);
        }

        private void DevStopBandwidth()
        {
            if (!ModsConfig.OdysseyActive)
            {
                return;
            }

            if (!bandwidthInterferenceActive)
            {
                Messages.Message("当前没有正在进行的带宽干扰。", MessageTypeDefOf.RejectInput);
                return;
            }

            EndBandwidthInterference(startCooldown: true, reason: "devstop");
        }

        private void DevEmp()
        {
            if (!ModsConfig.OdysseyActive || parent.Map == null)
            {
                return;
            }

            TriggerEmpShockwave();
        }
    }
}
