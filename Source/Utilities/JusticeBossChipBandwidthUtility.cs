using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    public static class JusticeBossChipBandwidthUtility
    {
        private const string SignalChipDefName = "SignalChip";
        private const string PowerfocusChipDefName = "PowerfocusChip";
        private const string NanostructuringChipDefName = "NanostructuringChip";

        public static bool TryGetBandwidthPerChip(ThingDef def, out int bandwidth)
        {
            bandwidth = 0;
            if (def == null)
            {
                return false;
            }

            switch (def.defName)
            {
                case SignalChipDefName:
                    bandwidth = 5;
                    return true;
                case PowerfocusChipDefName:
                    bandwidth = 10;
                    return true;
                case NanostructuringChipDefName:
                    bandwidth = 15;
                    return true;
                default:
                    return false;
            }
        }

        public static bool TryGetUpgradeNode(Pawn? pawn, out CompMAPMechanitorNode comp)
        {
            comp = null!;
            if (!ModsConfig.BiotechActive || pawn == null)
            {
                return false;
            }

            if (CompJusticeSelfWorkMode.GetFor(pawn) == null)
            {
                return false;
            }

            if (!CompMAPMechanitorNode.TryGetNodeComp(pawn, out CompMAPMechanitorNode? nodeComp)
                || nodeComp == null)
            {
                return false;
            }

            CompProperties_MAPMechanitorNode? nodeProps = nodeComp.NodeProps;
            if (nodeProps == null
                || !nodeProps.allowBossChipBandwidthUpgrade
                || nodeProps.maxIntrinsicBandwidth <= 0)
            {
                return false;
            }

            comp = nodeComp;
            return true;
        }

        public static string UseOneLabel(string chipLabel, int bandwidthPerChip) =>
            $"提升带宽：使用一个{chipLabel}（带宽+{bandwidthPerChip}）";

        public static string UseAllLabel(string chipLabel, int theoreticalTotal) =>
            $"提升带宽：使用全部{chipLabel} （带宽+{theoreticalTotal}）";

        public static string AtCapLabel() => "当前带宽已达上限。";

        public static string WasteConfirmText(string pawnLabel, int maxIntrinsic) =>
            $"{pawnLabel}的带宽上限为{maxIntrinsic}。使用全部芯片会导致浪费。\n\n仍要继续吗？";

        public static string SuccessMessage(string pawnLabel, int actualAdded) =>
            $"{pawnLabel}的带宽上限成功提升{actualAdded}点。";
    }
}
