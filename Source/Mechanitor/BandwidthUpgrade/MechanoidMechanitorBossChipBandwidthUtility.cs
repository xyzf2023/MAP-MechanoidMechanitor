using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class MechanoidMechanitorBossChipBandwidthUtility
    {
        private const string SignalChipDefName = "SignalChip";
        private const string PowerfocusChipDefName = "PowerfocusChip";
        private const string NanostructuringChipDefName = "NanostructuringChip";
        private const string QuantumComputingChipDefName = "MAP_QuantumComputingChip";

        public static bool TryGetBandwidthPerChip(ThingDef def, out int bandwidth)
        {
            bandwidth = 0;
            if (def == null)
            {
                return false;
            }

            switch (def.defName)
            {
                case SignalChipDefName: //同步信号芯片
                    bandwidth = 5;
                    return true;
                case PowerfocusChipDefName: //能量汇聚芯片
                    bandwidth = 10;
                    return true;
                case NanostructuringChipDefName: //纳米结构芯片
                    bandwidth = 15;
                    return true;
                case QuantumComputingChipDefName: //量子运算芯片
                    bandwidth = 30;   
                    return true;
                default:
                    return false;
            }
        }

        public static bool CanUpgradeBandwidth(Pawn? pawn)
        {
            if (!ModsConfig.BiotechActive
                || pawn == null
                || !MechanoidMechanitorCapabilityUtility.HasCapability(pawn, MechanoidMechanitorCapability.BandwidthUpgrade))
            {
                return false;
            }

            return true;
        }

        public static int GetRemainingIntrinsicBandwidth(Pawn? pawn)
        {
            return MechanoidMechanitorRoleUtility.GetRemainingIntrinsicBandwidth(pawn);
        }

        public static int GetMaxIntrinsicBandwidth(Pawn? pawn)
        {
            return MechanoidMechanitorRoleUtility.GetMaxIntrinsicBandwidth(pawn);
        }

        public static int AddChipBandwidth(Pawn? pawn, int requestedAmount)
        {
            return MechanoidMechanitorRoleUtility.AddChipBandwidth(pawn, requestedAmount);
        }

        public static string UseOneLabel(string chipLabel, int bandwidthPerChip) =>
            "MAP_MechanoidMechanitor.BandwidthUpgrade.UseOne".Translate(
                chipLabel, bandwidthPerChip);

        public static string UseAllLabel(string chipLabel, int theoreticalTotal) =>
            "MAP_MechanoidMechanitor.BandwidthUpgrade.UseAll".Translate(
                chipLabel, theoreticalTotal);

        public static string AtCapLabel() =>
            "MAP_MechanoidMechanitor.BandwidthUpgrade.AtCap".Translate();

        public static string WasteConfirmText(string pawnLabel, int maxIntrinsic) =>
            "MAP_MechanoidMechanitor.BandwidthUpgrade.WasteConfirm".Translate(
                pawnLabel, maxIntrinsic);

        public static string SuccessMessage(string pawnLabel, int actualAdded) =>
            "MAP_MechanoidMechanitor.BandwidthUpgrade.Success".Translate(
                pawnLabel, actualAdded);
    }
}
