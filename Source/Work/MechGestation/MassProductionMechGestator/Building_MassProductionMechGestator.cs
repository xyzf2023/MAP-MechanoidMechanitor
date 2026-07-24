using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public class Building_MassProductionMechGestator : Building_MechGestator
    {
        public const int FixedFormingTicks = 15000;

        private const string DevAdvanceFormingLabel = "DEV: Forming cycle +25%";

        private const string DevCompleteCycleLabel = "DEV: Complete cycle";

        private const string DevCompleteAllCyclesLabel = "DEV: Complete all cycles";

        public CompMassProductionMechGestator? MassProductionGestationComp =>
            GetComp<CompMassProductionMechGestator>();

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                // 原版开发者 Gizmo 操作 Bill_Mech.formingTicks/gestationCycles；
                // 量产型培育仓真实计时源是 CompMassProductionMechGestator.remainingTicks，故仅替换 action。
                if (gizmo is Command_Action command)
                {
                    string label = command.defaultLabel;
                    if (label == DevAdvanceFormingLabel)
                    {
                        command.action = () => MassProductionGestationComp?.DebugAdvanceForming(0.25f);
                    }
                    else if (label == DevCompleteCycleLabel
                        || label == DevCompleteAllCyclesLabel)
                    {
                        command.action = () => MassProductionGestationComp?.DebugCompleteForming();
                    }
                }

                yield return gizmo;
            }
        }

        protected override string GetInspectStringExtra()
        {
            if (ActiveMechBill is not Bill_ProductionMech { State: FormingState.Forming })
            {
                return base.GetInspectStringExtra();
            }

            CompMassProductionMechGestator? comp = MassProductionGestationComp;
            if (comp == null || !comp.TimerInitialized)
            {
                return base.GetInspectStringExtra();
            }

            return string.Format(
                "{0}: {1}",
                "GestatingInspect".Translate(),
                Mathf.CeilToInt(comp.RemainingTicks).ToStringTicksToPeriod());
        }

        // 培育期不播放原版 Tick 特效：跳过 Building_MechGestator.Tick 中的 gestator*Mote 与环境音
        // (MechGestator_Ambience)。直接复制 ThingWithComps.Tick（仅 ticks comps）+
        // Building_WorkTableAutonomous.Tick（推进 activeBill）+ 原功率逻辑，省略 Mote/持续音分支。
        protected override void Tick()
        {
            if (AllComps != null)
            {
                for (int i = 0; i < AllComps.Count; i++)
                {
                    AllComps[i].CompTick();
                }
            }

            if (activeBill != null && CanWork())
            {
                activeBill.BillTick();
            }

            if (this.IsHashIntervalTick(250))
            {
                if (activeBill != null && activeBill.State == FormingState.Forming)
                {
                    Power.PowerOutput = 0f - Power.Props.PowerConsumption;
                }
                else
                {
                    Power.PowerOutput = 0f - Power.Props.idlePowerDraw;
                }
            }
        }

        // 仅绘制建筑本体；不绘制正在成形的机械族贴图，也不绘制透明缸体/顶盖分层。
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            this.Graphic.Draw(drawLoc, Rotation, this);
        }
    }
}
