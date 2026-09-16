using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class CompProperties_GravityField : CompProperties
    {
        public float radius = 10f;
        public float activationEnergyFraction = 0.10f;
        public float minimumActivationEnergyFraction = 0.11f;
        public float minimumSustainingEnergyFraction = 0.05f;
        public int energyDrainIntervalTicks = 15;
        public float energyDrainFraction = 0.0025f;

        public CompProperties_GravityField()
        {
            compClass = typeof(CompGravityField);
        }
    }

    /// <summary>先天重力立场：自身保存开关和计时，地图索引不另存一份状态。</summary>
    public sealed class CompGravityField : ThingComp
    {
        private bool enabled;
        private int nextEnergyDrainTick = -1;
        private IntVec3 lastSweepPosition = IntVec3.Invalid;

        public CompProperties_GravityField Props => (CompProperties_GravityField)props;
        private Pawn? Pawn => parent as Pawn;
        public Vector3 Center => parent.TrueCenter();
        public bool Active => enabled && CanSustain;

        private bool Available => Pawn is Pawn pawn && pawn.Spawned
            && !pawn.Destroyed && !pawn.Dead && !pawn.Downed && !pawn.Suspended;

        private bool CanSustain => Available
            && MechanicalFlightEnergyUtility.TryGetEnergyFraction(Pawn, out float energy)
            && energy >= Props.minimumSustainingEnergyFraction;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
                yield return gizmo;
            if (Pawn?.Faction != Faction.OfPlayer)
                yield break;

            var command = new Command_Toggle
            {
                defaultLabel = "MAP_GravityField_Label".Translate(),
                defaultDesc = "MAP_GravityField_Description".Translate(),
                icon = GravityFieldPresentation.Icon,
                isActive = () => Active,
                toggleAction = Toggle
            };
            if (!enabled)
            {
                string? reason = ActivationDisabledReason();
                if (reason != null)
                    command.Disable(reason);
            }
            yield return command;
        }

        private string? ActivationDisabledReason()
        {
            if (!Available)
                return "MAP_GravityField_Unavailable".Translate();
            if (!MechanicalFlightEnergyUtility.TryGetEnergyFraction(Pawn, out float energy)
                || energy < Props.minimumActivationEnergyFraction)
                return "MAP_GravityField_NotEnoughEnergy".Translate();
            return null;
        }

        private void Toggle()
        {
            if (enabled)
            {
                Deactivate();
                return;
            }
            string? reason = ActivationDisabledReason();
            if (reason != null)
            {
                Messages.Message(reason, parent, MessageTypeDefOf.RejectInput, false);
                return;
            }
            // 复用通用比例扣能入口，不使用飞行耗能倍率或机械体耗能倍率。
            if (!MechanicalFlightEnergyUtility.TryConsumeMaximumEnergyFraction(
                    Pawn, Props.activationEnergyFraction))
                return;
            enabled = true;
            nextEnergyDrainTick = GenTicks.TicksGame + Props.energyDrainIntervalTicks;
            if (!CanSustain)
            {
                // 按指定阈值：11%～不足15%允许展开，但扣能后立即关闭。
                Deactivate();
                return;
            }
            parent.Map.GetComponent<MapComponent_GravityFieldTracker>().Register(this);
            SweepExistingProjectiles();
        }

        public override void CompTick()
        {
            if (!enabled)
                return;
            if (!CanSustain)
            {
                Deactivate();
                return;
            }
            if (GenTicks.TicksGame >= nextEnergyDrainTick)
            {
                // 正常 Pawn 每 tick 调用一次；读档沿用原到期时间，不重复收取启动费用。
                nextEnergyDrainTick = GenTicks.TicksGame + Props.energyDrainIntervalTicks;
                if (!MechanicalFlightEnergyUtility.TryConsumeMaximumEnergyFraction(
                        Pawn, Props.energyDrainFraction) || !CanSustain)
                {
                    Deactivate();
                    return;
                }
            }
            if (lastSweepPosition != parent.Position)
                SweepExistingProjectiles();
        }

        private void SweepExistingProjectiles()
        {
            lastSweepPosition = parent.Position;
            parent.Map.GetComponent<MapComponent_GravityFieldTracker>()
                .SweepExistingProjectiles(this);
        }

        private void Deactivate()
        {
            enabled = false;
            nextEnergyDrainTick = -1;
            lastSweepPosition = IntVec3.Invalid;
            parent.Map?.GetComponent<MapComponent_GravityFieldTracker>().Deregister(this);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            lastSweepPosition = IntVec3.Invalid;
            // 此阶段不读取 needs：读档时其他 Pawn 状态可能尚未恢复。
            if (enabled)
                parent.Map.GetComponent<MapComponent_GravityFieldTracker>().Register(this);
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            map.GetComponent<MapComponent_GravityFieldTracker>().Deregister(this);
            Deactivate();
            base.PostDeSpawn(map, mode);
        }

        public override void Notify_Killed(Map prevMap, DamageInfo? dinfo = null)
        {
            prevMap?.GetComponent<MapComponent_GravityFieldTracker>().Deregister(this);
            Deactivate();
            base.Notify_Killed(prevMap, dinfo);
        }

        public override void Notify_Downed()
        {
            Deactivate();
            base.Notify_Downed();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref enabled, "gravityFieldEnabled", false);
            Scribe_Values.Look(ref nextEnergyDrainTick, "gravityFieldNextEnergyDrainTick", -1);
        }

        public override void PostDraw()
        {
            if (Active)
                GravityFieldPresentation.Draw(Center, Props.radius);
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            if (parent.Spawned)
                GenDraw.DrawCircleOutline(Center, Props.radius);
        }
    }
}
