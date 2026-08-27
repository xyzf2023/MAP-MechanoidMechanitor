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

        // 每个机械族机械师自己的充电阈值（0~1 比例）。
        // 默认值必须直接来自原版 MechanitorControlGroup.DefaultMechRechargeThresholds，
        // 不复制另一份“权威默认值”。旧存档没有该字段时由 Scribe 默认值自动补齐。
        public FloatRange RechargeThresholds =
            MechanitorControlGroup.DefaultMechRechargeThresholds;

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

            // 个人充电阈值：使用与原版 MechanitorControlGroup 保存
            // mechRechargeThresholds 相同的 Scribe_Values 方式，旧档缺字段时自动取默认值。
            Scribe_Values.Look(
                ref RechargeThresholds,
                "rechargeThresholds",
                MechanitorControlGroup.DefaultMechRechargeThresholds);

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

            // 阈值合法化规则唯一实现在 MechanoidMechanitorRechargeUtility，
            // 与 UI 写入入口 TrySetRechargeThresholds 共用同一套规则，禁止在此复制第二份。
            RechargeThresholds =
                MechanoidMechanitorRechargeUtility.SanitizeRechargeThresholds(RechargeThresholds);
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
