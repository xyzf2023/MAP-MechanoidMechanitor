using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// “机控同调”的权威会话快照。所有值只在合体开始前捕获一次；
    /// Hediff 与运行时 Tracker 只消费本记录，不自行保存第二份状态。
    /// </summary>
    public sealed class MechFusionMechanitorSnapshot : IExposable
    {
        public bool captured;
        public bool wearerWasMechanitor;
        public bool wearerHadMechlink;
        public bool implantEffectsCaptured;
        public bool grantsQuantumCommunicator;
        public bool grantsProxySubchain;
        public int bandwidthBonus;
        public int controlGroupBonus;
        public int destinationStartGroupIndex;
        public bool destinationGroupsCaptured;
        public bool transferApplied;
        public bool controlsRestored;
        public bool destinationGroupsRestored;
        public List<MechFusionControlGroupSnapshot> sourceGroups =
            new List<MechFusionControlGroupSnapshot>();
        public List<MechFusionControlGroupSnapshot> destinationGroupBackups =
            new List<MechFusionControlGroupSnapshot>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref captured, "captured");
            Scribe_Values.Look(ref wearerWasMechanitor, "wearerWasMechanitor");
            Scribe_Values.Look(ref wearerHadMechlink, "wearerHadMechlink");
            Scribe_Values.Look(
                ref implantEffectsCaptured,
                "implantEffectsCaptured");
            Scribe_Values.Look(
                ref grantsQuantumCommunicator,
                "grantsQuantumCommunicator");
            Scribe_Values.Look(
                ref grantsProxySubchain,
                "grantsProxySubchain");
            Scribe_Values.Look(ref bandwidthBonus, "bandwidthBonus");
            Scribe_Values.Look(ref controlGroupBonus, "controlGroupBonus");
            Scribe_Values.Look(
                ref destinationStartGroupIndex,
                "destinationStartGroupIndex");
            Scribe_Values.Look(
                ref destinationGroupsCaptured,
                "destinationGroupsCaptured");
            Scribe_Values.Look(ref transferApplied, "transferApplied");
            Scribe_Values.Look(ref controlsRestored, "controlsRestored");
            Scribe_Values.Look(
                ref destinationGroupsRestored,
                "destinationGroupsRestored");
            Scribe_Collections.Look(
                ref sourceGroups,
                "sourceGroups",
                LookMode.Deep);
            Scribe_Collections.Look(
                ref destinationGroupBackups,
                "destinationGroupBackups",
                LookMode.Deep);
            sourceGroups ??= new List<MechFusionControlGroupSnapshot>();
            destinationGroupBackups ??=
                new List<MechFusionControlGroupSnapshot>();
        }
    }

    public sealed class MechFusionControlGroupSnapshot : IExposable
    {
        public int groupIndex = -1;
        public MechWorkModeDef? workMode;
        public GlobalTargetInfo target = GlobalTargetInfo.Invalid;
        public FloatRange rechargeThresholds =
            MechanitorControlGroup.DefaultMechRechargeThresholds;
        public List<MechFusionControlledMechSnapshot> mechs =
            new List<MechFusionControlledMechSnapshot>();
        public List<MechFusionControlGroupTagSnapshot> tags =
            new List<MechFusionControlGroupTagSnapshot>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref groupIndex, "groupIndex", -1);
            Scribe_Defs.Look(ref workMode, "workMode");
            Scribe_TargetInfo.Look(ref target, "target");
            Scribe_Values.Look(
                ref rechargeThresholds,
                "rechargeThresholds",
                MechanitorControlGroup.DefaultMechRechargeThresholds);
            Scribe_Collections.Look(ref mechs, "mechs", LookMode.Deep);
            Scribe_Collections.Look(ref tags, "tags", LookMode.Deep);
            mechs ??= new List<MechFusionControlledMechSnapshot>();
            tags ??= new List<MechFusionControlGroupTagSnapshot>();
        }
    }

    public sealed class MechFusionControlledMechSnapshot : IExposable
    {
        public Pawn? pawn;
        public Faction? originalFaction;
        public bool originalFactionCaptured;
        public int sourceGroupIndex = -1;
        public int assignedTick;
        public int assignedOrder;
        public bool drafted;
        public bool autoRepairAvailable;
        public bool autoRepair;
        public bool transferFailed;
        public bool restoreResolved;
        public List<MechFusionAllowedAreaSnapshot> allowedAreas =
            new List<MechFusionAllowedAreaSnapshot>();

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_References.Look(ref originalFaction, "originalFaction");
            Scribe_Values.Look(
                ref originalFactionCaptured,
                "originalFactionCaptured");
            Scribe_Values.Look(
                ref sourceGroupIndex,
                "sourceGroupIndex",
                -1);
            Scribe_Values.Look(ref assignedTick, "assignedTick");
            Scribe_Values.Look(ref assignedOrder, "assignedOrder");
            Scribe_Values.Look(ref drafted, "drafted");
            Scribe_Values.Look(
                ref autoRepairAvailable,
                "autoRepairAvailable");
            Scribe_Values.Look(ref autoRepair, "autoRepair");
            Scribe_Values.Look(ref transferFailed, "transferFailed");
            Scribe_Values.Look(ref restoreResolved, "restoreResolved");
            Scribe_Collections.Look(
                ref allowedAreas,
                "allowedAreas",
                LookMode.Deep);
            allowedAreas ??= new List<MechFusionAllowedAreaSnapshot>();
        }
    }

    public sealed class MechFusionAllowedAreaSnapshot : IExposable
    {
        public Map? map;
        public Area? area;

        public void ExposeData()
        {
            Scribe_References.Look(ref map, "map");
            Scribe_References.Look(ref area, "area");
        }
    }

    public sealed class MechFusionControlGroupTagSnapshot : IExposable
    {
        public Pawn? pawn;
        public string? tag;

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref tag, "tag");
        }
    }
}
