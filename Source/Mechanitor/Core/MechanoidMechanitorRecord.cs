using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public sealed class MechanoidMechanitorRecord : IExposable
    {
        public Pawn? Pawn;
        public MechanoidMechanitorOrigin Origin;
        public int ChipBandwidthBonus;
        public MechWorkModeDef? SelfWorkMode;
        public bool RoleWorkSettingsInitialized;

        public MechanoidMechanitorRecord()
        {
        }

        public MechanoidMechanitorRecord(Pawn pawn, MechanoidMechanitorOrigin origin)
        {
            Pawn = pawn;
            Origin = origin;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn");
            Scribe_Values.Look(ref Origin, "origin", MechanoidMechanitorOrigin.Native);
            Scribe_Values.Look(ref ChipBandwidthBonus, "chipBandwidthBonus", 0);
            Scribe_Defs.Look(ref SelfWorkMode, "selfWorkMode");
            Scribe_Values.Look(
                ref RoleWorkSettingsInitialized,
                "roleWorkSettingsInitialized",
                false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                SanitizeAfterLoad();
            }
        }

        public void SanitizeAfterLoad()
        {
            if (ChipBandwidthBonus < 0)
            {
                ChipBandwidthBonus = 0;
            }

            if (Pawn != null && !Pawn.Destroyed)
            {
                int maxBonus = GetMaxChipBandwidthBonus(Pawn, Origin);
                ChipBandwidthBonus = Mathf.Clamp(ChipBandwidthBonus, 0, maxBonus);
            }

            SelfWorkMode = MechanoidMechanitorSelfWorkModeUtility.SanitizeWorkMode(SelfWorkMode);
        }

        public static int GetMaxChipBandwidthBonus(
            Pawn pawn,
            MechanoidMechanitorOrigin origin)
        {
            if (origin == MechanoidMechanitorOrigin.Acquired)
            {
                return Mathf.Max(
                    0,
                    MechanoidMechanitorRoleUtility.AcquiredMaxIntrinsicBandwidth
                        - MechanoidMechanitorRoleUtility.AcquiredBaseExtraBandwidth);
            }

            if (CompMAPMechanitorNode.TryGetNodeComp(pawn, out CompMAPMechanitorNode? nodeComp)
                && nodeComp?.NodeProps != null)
            {
                return Mathf.Max(
                    0,
                    nodeComp.NodeProps.maxIntrinsicBandwidth
                        - nodeComp.NodeProps.extraMechBandwidth);
            }

            return 0;
        }
    }
}
