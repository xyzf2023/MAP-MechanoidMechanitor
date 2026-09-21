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
        public bool HasMappedPersonality;

        // 后天机械族机械师“技能兴趣度最低为好奇”策略的一次性应用标记。
        // 运行时新建记录默认 false（尚未应用）；Scribe 缺省值取 true，保证旧存档中
        // 已有记录在读取后一律视为“已处理”，绝不因新增字段而追溯修改既有机械族机械师。
        // 该字段不表达工作设置或人格映射的任何既有语义，也不得用
        // RoleWorkSettingsInitialized / HasMappedPersonality 替代。
        public bool InitialPassionPolicyApplied;

        // 每个机械族机械师自己的充电阈值（0~1 比例）。
        // 默认值必须直接来自原版 MechanitorControlGroup.DefaultMechRechargeThresholds，
        // 不复制另一份“权威默认值”。旧存档没有该字段时由 Scribe 默认值自动补齐。
        // 旧档导入字段。运行时权威值已迁至 AutonomousMechAuthorizationRecord，勿再写入。
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
            Scribe_Values.Look(ref HasMappedPersonality, "hasMappedPersonality", false);
            // 旧存档缺字段时按 true（已处理）读入，避免读档后把已有机械族机械师的无兴趣
            // 追溯提升为好奇；运行时新建记录（未经过 ExposeData）保持默认 false。
            Scribe_Values.Look(
                ref InitialPassionPolicyApplied,
                "initialPassionPolicyApplied",
                true);

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
