using System.Collections.Generic;
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
    /// </summary>
    internal sealed class CerebrexBandwidthVisuals
    {
        private const int LinkPulseIntervalTicks = 45;
        private const int TargetPulseIntervalTicks = 90;
        private const int ElectricityArcIntervalTicks = 120;

        private static readonly Color InterferenceColor = new Color(1f, 0.12f, 0.08f, 0.95f);

        private MoteDualAttached? linkMote;
        private int nextLinkPulseTick;
        private int nextTargetPulseTick;
        private int nextElectricityArcTick;

        public void ResetTransientState()
        {
            // 不主动销毁旧 Mote：停止 Maintain 后，原版会按自身 fadeOutTime 自然淡出。
            linkMote = null;
            nextLinkPulseTick = 0;
            nextTargetPulseTick = 0;
            nextElectricityArcTick = 0;
        }

        public void Start(Thing core, Pawn target)
        {
            ResetTransientState();
            if (!TryGetSharedMap(core, target, out Map map))
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            EnsureLink(core, target, map);
            linkMote?.Maintain();

            SpawnLinkPulse(core, target);
            SpawnRedFlash(core, map, 2f);
            SpawnRedFlash(target, map, 1.2f);
            SpawnBandPulse(target, map, recovery: false);
            SpawnElectricityArc(target, map);

            SoundDef? signalSound = DefDatabase<SoundDef>.GetNamedSilentFail("MechbandDishUsed");
            signalSound?.PlayOneShot(new TargetInfo(core.Position, map));

            nextLinkPulseTick = now + LinkPulseIntervalTicks;
            nextTargetPulseTick = now + TargetPulseIntervalTicks;
            nextElectricityArcTick = now + ElectricityArcIntervalTicks;
        }

        public void Tick(Thing core, Pawn? target)
        {
            if (target == null || !TryGetSharedMap(core, target, out Map map))
            {
                ResetTransientState();
                return;
            }

            EnsureLink(core, target, map);
            linkMote?.Maintain();

            int now = Find.TickManager.TicksGame;
            if (now >= nextLinkPulseTick)
            {
                SpawnLinkPulse(core, target);
                nextLinkPulseTick = now + LinkPulseIntervalTicks;
            }

            if (now >= nextTargetPulseTick)
            {
                SpawnBandPulse(target, map, recovery: false);
                nextTargetPulseTick = now + TargetPulseIntervalTicks;
            }

            if (now >= nextElectricityArcTick)
            {
                SpawnElectricityArc(target, map);
                nextElectricityArcTick = now + ElectricityArcIntervalTicks;
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

        public void Stop(Pawn? target, IEnumerable<Pawn> berserkPawns, bool playRecovery)
        {
            ResetTransientState();
            if (!playRecovery)
            {
                return;
            }

            HashSet<Pawn> recoveryPawns = new HashSet<Pawn>();
            if (target != null)
            {
                recoveryPawns.Add(target);
            }

            if (berserkPawns != null)
            {
                foreach (Pawn pawn in berserkPawns)
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

        private void EnsureLink(Thing core, Pawn target, Map map)
        {
            if (linkMote != null && !linkMote.Destroyed && linkMote.Spawned && linkMote.Map == map)
            {
                linkMote.UpdateTargets(core, target, Vector3.zero, Vector3.zero);
                return;
            }

            ThingDef? linkDef = DefDatabase<ThingDef>.GetNamedSilentFail("Mote_PsychicLinkLine");
            if (linkDef == null)
            {
                linkMote = null;
                return;
            }

            linkMote = MoteMaker.MakeInteractionOverlay(linkDef, core, target);
            linkMote.instanceColor = InterferenceColor;
        }

        private static void SpawnLinkPulse(Thing core, Pawn target)
        {
            if (!TryGetSharedMap(core, target, out _))
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

        private static bool TryGetSharedMap(Thing core, Pawn target, out Map map)
        {
            map = core.Map;
            return core != null
                && target != null
                && !core.Destroyed
                && !target.Destroyed
                && core.Spawned
                && target.Spawned
                && map != null
                && target.Map == map;
        }
    }
}
