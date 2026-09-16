using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class MechanicalFlightProfileDef : Def
    {
        public float minimumTakeoffEnergy = 0.20f;
        public float automaticLandingEnergy = 0.15f;
        public float energyDrainFraction = 0.005f;
        public float lightEnergyDrainMultiplier = 0.75f;
        public float mediumEnergyDrainMultiplier = 1f;
        public float heavyEnergyDrainMultiplier = 1.5f;
        public float ultraHeavyEnergyDrainMultiplier = 2f;
        public int energyDrainIntervalTicks = 60;
        public float flightCellsPerSecond = 30f;

        public float lowEnergyWarningThreshold = 0.10f;
        public int emergencyLandingSearchRadius = 3;
        public int crashDamagePerWeightClass = 30;
        public int crashExplosionRadius = 1;
        public int crashRoofCollapseRadius = 2;

        public float hoverExtraVisualHeight = 2.5f;
        public float hoverBobAmplitude = 0.15f;
        public float hoverBobPeriodTicks = 100f;
        public float maximumTiltAngle = 45f;
        public float minimumTiltStep = 0.7f;
        public float tiltSpeed = 130f;
        public float thrusterAngleOffset = 5f;
        public string thrusterFlameTexture =
            "Things/Mote/MechanicalFlight/ThrusterFlame";
        public string thrusterGlowTexture =
            "Things/Mote/MechanicalFlight/ThrusterGlow";
        public FleckDef? thrusterSparkFleck;
        public int thrusterSparkIntervalTicks = 11;
        public float thrusterCruiseWidthFactor = 0.62f;
        public float thrusterCruiseLengthFactor = 1.62f;
        public FleckDef? landingGlowFleck;
        public bool drawThruster = true;
        public bool drawGroundWash = true;
        public bool allowTilt = true;
        public bool useHoverGlow = true;
        public float hoverGlowRadius = 4.5f;
        public ColorInt hoverGlowColor = new ColorInt(105, 190, 255, 0);

        public bool breakThinRoofOnTakeoff = true;
        public bool breakThinRoofOnLanding = true;
        public int roofBreakRadius = 1;
        public bool clearBuildRoofArea = true;
        public bool markNoRoofArea = true;
    }

    public enum MechanicalFlightPhase
    {
        Grounded,
        TakingOff,
        Hovering,
        Landing,
        EmergencyApproach,
        EmergencyLanding,
        Crashing,
        FusionAscent,
        FusionDescent
    }

    /// <summary>
    /// 机械飞行的运行用途。普通飞行与合体快速转移共用同一套授权、起飞和
    /// 落地规则，但两者互不接管对方的运行语义。
    /// </summary>
    public enum MechanicalFlightPurpose
    {
        Normal,
        FusionRelocation
    }
}
