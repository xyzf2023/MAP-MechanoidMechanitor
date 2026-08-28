using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 通用主动安装控制模块属性。默认安装耗时 600 tick。
    /// </summary>
    public class CompProperties_InstallableMechanoidModule : CompProperties
    {
        public int installDurationTicks = 600;

        public CompProperties_InstallableMechanoidModule()
        {
            compClass = typeof(CompInstallableMechanoidModule);
        }
    }

    /// <summary>
    /// 机械族模块通用主动安装控制组件。
    /// 不依赖具体模块的 ThingDef defName，通过具体安装效果组件分发行为。
    /// </summary>
    public class CompInstallableMechanoidModule : ThingComp
    {
        private const string LogPrefix =
            "[MAP-机械族机械师] CompInstallableMechanoidModule：";

        public CompProperties_InstallableMechanoidModule Props
            => (CompProperties_InstallableMechanoidModule)props;

        public int InstallDurationTicks => Props.installDurationTicks;

        /// <summary>
        /// 通用右键安装标签，由物品 label 生成：安装{label}。
        /// </summary>
        public string InstallOptionLabel => "安装" + parent.LabelNoCount;

        /// <summary>
        /// 通用机械族使用者条件：仅玩家安全的机械族可以使用。
        /// 不扩大到普通人类 / 非玩家机械族 / 敌对机械族 / 无派系机械族 / 其他非血肉 Pawn。
        /// </summary>
        public bool IsValidInstaller(Pawn? pawn)
        {
            return ModsConfig.BiotechActive
                && pawn != null
                && !pawn.Dead
                && !pawn.Destroyed
                && pawn.RaceProps.IsMechanoid
                && pawn.Faction != null
                && pawn.Faction.IsPlayerSafe()
                && pawn.health?.hediffSet != null;
        }

        /// <summary>
        /// 查找该物品唯一的具体安装效果组件。
        /// 缺少或存在多个效果组件时安全返回 null，并在开发模式/一次性日志中输出明确错误。
        /// </summary>
        public CompMechanoidModuleInstallEffect? TryGetInstallEffect()
        {
            if (parent == null)
            {
                return null;
            }

            List<CompMechanoidModuleInstallEffect> effects =
                parent.GetComps<CompMechanoidModuleInstallEffect>().ToList();
            int count = effects.Count;

            if (count == 0)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}{parent.def.defName} 缺少具体安装效果组件，安装流程中止。",
                    99123001);
                return null;
            }

            if (count > 1)
            {
                Log.ErrorOnce(
                    $"{LogPrefix}{parent.def.defName} 拥有 {count} 个具体安装效果组件，" +
                    "安装流程中止以避免歧义。",
                    99123002);
                return null;
            }

            return effects[0];
        }
    }
}
