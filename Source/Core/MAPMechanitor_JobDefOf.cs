using RimWorld;
using Verse;

namespace MAP_MechanoidMechanitor
{
    [DefOf]
    public static class MAPMechanitor_JobDefOf
    {
        public static JobDef MAP_MechanoidMechanitorUseBossChipForBandwidth = null!;
        public static JobDef MAP_UseAutonomousDirectiveCore = null!;
        public static JobDef MAP_UseBionicCompanionModule = null!;
        public static JobDef MAP_TransferMechanicalConsciousness = null!;
        public static JobDef MAP_SyntheticGiveBirth = null!;
        public static JobDef MAP_ContactMechanoidOvermind = null!;
        public static JobDef MAP_MechanoidMechanitorSelfRepair = null!;

        // 新任务使用此 JobDef。
        public static JobDef MAP_HumanImplantInstall = null!;

        public static JobDef MAP_BroadcastSymbiosisDeclarationPortable = null!;

        // 仅供旧存档 Def 兼容，新任务不再使用。
        // 旧存档中保存的 MAP_LoverInstallImplant 任务通过 Jobs_LoverImplants.xml 的 driverClass
        // 重定向到 JobDriver_HumanImplantInstall，可正常 resolve。
        public static JobDef MAP_LoverInstallImplant = null!;

        static MAPMechanitor_JobDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(MAPMechanitor_JobDefOf));
        }
    }
}
