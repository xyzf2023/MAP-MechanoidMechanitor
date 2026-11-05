using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class HediffComp_MechRepair : HediffComp
    {
        private const int RepairEligibilityCheckIntervalTicks = 30;
        private int ticksUntilRepair;
        private int pendingRepairTicks;

        private HediffCompProperties_MechRepair Props =>
            (HediffCompProperties_MechRepair)props;

        public override void CompPostMake()
        {
            base.CompPostMake();
            ticksUntilRepair = Mathf.Max(1, Props.repairIntervalTicks);
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            if (!Pawn.RaceProps.IsMechanoid)
            {
                return;
            }

            if (Props.requiresAutoRepair)
            {
                CompMechRepairable? repairable = Pawn.TryGetComp<CompMechRepairable>();
                if (repairable?.autoRepair != true)
                {
                    pendingRepairTicks = 0;
                    return;
                }

                // 按短窗口累计有效时间；无可修复内容时保留倒计时，不积攒跨周期额度。
                pendingRepairTicks++;
                int checkInterval = Mathf.Min(RepairEligibilityCheckIntervalTicks, Mathf.Max(1, ticksUntilRepair));
                if (pendingRepairTicks < checkInterval)
                {
                    return;
                }

                int elapsedTicks = pendingRepairTicks;
                pendingRepairTicks = 0;
                if (!MechRepairUtility.CanRepair(Pawn))
                {
                    return;
                }

                ticksUntilRepair -= elapsedTicks;
            }
            else
            {
                pendingRepairTicks = 0;
                ticksUntilRepair--;
            }

            if (ticksUntilRepair > 0)
            {
                return;
            }

            ticksUntilRepair = Mathf.Max(1, Props.repairIntervalTicks);
            int repairAmount = Mathf.Max(0, Props.repairAmount);
            for (int i = 0; i < repairAmount; i++)
            {
                if (!MechRepairUtility.CanRepair(Pawn))
                {
                    break;
                }

                MechRepairUtility.RepairTick(Pawn);
            }
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref ticksUntilRepair, "ticksUntilRepair", 0);
            Scribe_Values.Look(ref pendingRepairTicks, "pendingRepairTicks", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                int checkInterval = Mathf.Min(RepairEligibilityCheckIntervalTicks, Mathf.Max(1, ticksUntilRepair));
                pendingRepairTicks = Mathf.Clamp(pendingRepairTicks, 0, checkInterval - 1);
            }
        }
    }

    public sealed class HediffCompProperties_MechRepair : HediffCompProperties
    {
        public int repairIntervalTicks = 120;
        public int repairAmount = 10;
        public bool requiresAutoRepair = true;

        public HediffCompProperties_MechRepair()
        {
            compClass = typeof(HediffComp_MechRepair);
        }
    }
}
