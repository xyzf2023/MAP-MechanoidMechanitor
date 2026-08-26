using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 主脑带宽干扰的纯视觉层。只复用原版 Mote / Fleck / Effecter / SoundDef，
    /// 不保存状态，也不参与技能的目标选择、带宽计算、狂暴或结束判定。
    /// 原版 Mote 不写入存档，因此读档后由 Tick 自动重建持续链路。
    /// 现支持同时维护多个目标的瞬态视觉状态。
    /// </summary>
    internal sealed class CerebrexBandwidthVisuals
    {
        private const int LinkPulseIntervalTicks = 45;
        private const int TargetPulseIntervalTicks = 90;
        private const int ElectricityArcIntervalTicks = 120;

        private static readonly Color InterferenceColor = new Color(1f, 0.12f, 0.08f, 0.95f);

        private sealed class TargetVisualState
        {
            public MoteDualAttached? linkMote;
            public int nextLinkPulseTick;
            public int nextTargetPulseTick;
            public int nextElectricityArcTick;
        }

        private readonly Dictionary<Pawn, TargetVisualState> states =
            new Dictionary<Pawn, TargetVisualState>();

        public void ResetTransientState()
        {
            // 不主动销毁旧 Mote：停止 Maintain 后，原版会按自身 fadeOutTime 自然淡出。
            states.Clear();
        }

        public void Start(Thing core, IEnumerable<Pawn?> targets)
        {
            ResetTransientState();
            if (!TryGetSharedMap(core, targets, out Map map))
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            foreach (Pawn? target in targets)
            {
                if (target == null || target.Destroyed || !target.Spawned || target.Map != map)
                {
                    continue;
                }

                TargetVisualState state = GetOrCreateState(target);
                EnsureLink(core, target, map, state);
                state.linkMote?.Maintain();
                SpawnLinkPulse(core, target);
                SpawnRedFlash(core, map, 2f);
                SpawnRedFlash(target, map, 1.2f);
                SpawnBandPulse(target, map, recovery: false);
                SpawnElectricityArc(target, map);
                state.nextLinkPulseTick = now + LinkPulseIntervalTicks;
                state.nextTargetPulseTick = now + TargetPulseIntervalTicks;
                state.nextElectricityArcTick = now + ElectricityArcIntervalTicks;
            }

            SoundDef? signalSound = DefDatabase<SoundDef>.GetNamedSilentFail("MechbandDishUsed");
            signalSound?.PlayOneShot(new TargetInfo(core.Position, map));
        }

        public void Tick(Thing core, IEnumerable<Pawn?> targets)
        {
            if (!TryGetSharedMap(core, targets, out Map map))
            {
                ResetTransientState();
                return;
            }

            HashSet<Pawn> valid = new HashSet<Pawn>();
            int now = Find.TickManager.TicksGame;
            foreach (Pawn? target in targets)
            {
                if (target == null || target.Destroyed || !target.Spawned || target.Map != map)
                {
                    continue;
                }

                valid.Add(target);
                TargetVisualState state = GetOrCreateState(target);
                EnsureLink(core, target, map, state);
                state.linkMote?.Maintain();

                if (now >= state.nextLinkPulseTick)
                {
                    SpawnLinkPulse(core, target);
                    state.nextLinkPulseTick = now + LinkPulseIntervalTicks;
                }

                if (now >= state.nextTargetPulseTick)
                {
                    SpawnBandPulse(target, map, recovery: false);
                    state.nextTargetPulseTick = now + TargetPulseIntervalTicks;
                }

                if (now >= state.nextElectricityArcTick)
                {
                    SpawnElectricityArc(target, map);
                    state.nextElectricityArcTick = now + ElectricityArcIntervalTicks;
                }
            }

            // 目标失效或离开地图时移除其瞬态视觉记录，让旧 Mote 自然淡出。
            List<Pawn> removeKeys = states.Keys
                .Where(p => p == null || p.Destroyed || !valid.Contains(p))
                .ToList();
            foreach (Pawn key in removeKeys)
            {
                states.Remove(key);
            }
        }

        public void NotifyBerserk(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || !pawn.Spawned || pawn.Map == null)
            {
                return;
            }

            Map map = pawn.Map;
            SpawnRedFlash(pawn, map, 1.45f);
            SpawnBandPulse(pawn, map, recovery: false);
            SpawnElectricityArc(pawn, map);
        }

        public void Stop(
            IEnumerable<Pawn?> targets,
            IEnumerable<Pawn?> berserkPawns,
            bool playRecovery)
        {
            ResetTransientState();
            if (!playRecovery)
            {
                return;
            }

            HashSet<Pawn> recoveryPawns = new HashSet<Pawn>();
            if (targets != null)
            {
                foreach (Pawn? pawn in targets)
                {
                    if (pawn != null)
                    {
                        recoveryPawns.Add(pawn);
                    }
                }
            }

            if (berserkPawns != null)
            {
                foreach (Pawn? pawn in berserkPawns)
                {
                    if (pawn != null)
                    {
                        recoveryPawns.Add(pawn);
                    }
                }
            }

            foreach (Pawn pawn in recoveryPawns)
            {
                if (pawn.Destroyed || pawn.Dead || !pawn.Spawned || pawn.Map == null)
                {
                    continue;
                }

                SpawnBandPulse(pawn, pawn.Map, recovery: true);
            }
        }

        private TargetVisualState GetOrCreateState(Pawn target)
        {
            if (!states.TryGetValue(target, out TargetVisualState? state) || state == null)
            {
                state = new TargetVisualState();
                states[target] = state;
            }

            return state;
        }

        private void EnsureLink(Thing core, Pawn target, Map map, TargetVisualState state)
        {
            if (state.linkMote != null
                && !state.linkMote.Destroyed
                && state.linkMote.Spawned
                && state.linkMote.Map == map)
            {
                state.linkMote.UpdateTargets(core, target, Vector3.zero, Vector3.zero);
                return;
            }

            ThingDef? linkDef = DefDatabase<ThingDef>.GetNamedSilentFail("Mote_PsychicLinkLine");
            if (linkDef == null)
            {
                state.linkMote = null;
                return;
            }

            state.linkMote = MoteMaker.MakeInteractionOverlay(linkDef, core, target);
            state.linkMote.instanceColor = InterferenceColor;
        }

        private static void SpawnLinkPulse(Thing core, Pawn target)
        {
            if (!TryGetSharedMap(core, new[] { target }, out _))
            {
                return;
            }

            ThingDef? pulseDef = DefDatabase<ThingDef>.GetNamedSilentFail("Mote_PsychicLinkPulse");
            if (pulseDef == null)
            {
                return;
            }

            MoteDualAttached pulse = MoteMaker.MakeInteractionOverlay(pulseDef, core, target);
            pulse.instanceColor = InterferenceColor;
        }

        private static void SpawnRedFlash(Thing thing, Map map, float scale)
        {
            if (thing == null || thing.Destroyed || !thing.Spawned || thing.Map != map)
            {
                return;
            }

            ThingDef? flashDef = DefDatabase<ThingDef>.GetNamedSilentFail("Mote_RedFlashStrong");
            if (flashDef == null)
            {
                return;
            }

            Mote? flash = MoteMaker.MakeStaticMote(thing.DrawPos, map, flashDef, scale);
            if (flash != null)
            {
                flash.instanceColor = InterferenceColor;
            }
        }

        private static void SpawnBandPulse(Pawn pawn, Map map, bool recovery)
        {
            if (pawn == null || pawn.Destroyed || !pawn.Spawned || pawn.Map != map)
            {
                return;
            }

            string defName = recovery ? "BandNodeGreenPulse" : "BandNodeRedPulse";
            FleckDef? pulseDef = DefDatabase<FleckDef>.GetNamedSilentFail(defName);
            if (pulseDef != null)
            {
                FleckMaker.Static(pawn.DrawPos, map, pulseDef, recovery ? 0.55f : 0.65f);
            }
        }

        private static void SpawnElectricityArc(Thing thing, Map map)
        {
            if (thing == null || thing.Destroyed || !thing.Spawned || thing.Map != map)
            {
                return;
            }

            EffecterDef? arcDef = DefDatabase<EffecterDef>.GetNamedSilentFail("MechBandElectricityArc");
            Effecter? arc = arcDef?.Spawn(thing.Position, map, 1f);
            arc?.Cleanup();
        }

        private static bool TryGetSharedMap(
            Thing core,
            IEnumerable<Pawn?> targets,
            out Map map)
        {
            map = core.Map;
            if (core == null || core.Destroyed || !core.Spawned || map == null)
            {
                return false;
            }

            if (targets == null)
            {
                return false;
            }

            foreach (Pawn? target in targets)
            {
                if (target == null || target.Destroyed || !target.Spawned || target.Map != map)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
