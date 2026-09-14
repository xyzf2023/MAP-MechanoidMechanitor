using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    ///     数据处理分配 Gizmo 的显示判断与命令创建逻辑集中处，供机械族与人类机械师两套补丁共用。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class DataProcessingAllocationGizmoUtility
    {
        private static readonly Texture2D DataProcessingAllocationIcon =
            ContentFinder<Texture2D>.Get("UI/MM_DataProcessingAllocation");

        /// <summary>
        ///     机械族机械师是否显示“数据处理分配” Gizmo。
        /// </summary>
        public static bool ShouldShowForMechanoid(Pawn? mech)
        {
            return ModsConfig.BiotechActive
                   && ResearchFeatureUnlockUtility.IsDataProcessingAllocationUnlocked()
                   && mech != null
                   && !mech.Dead
                   && !mech.Destroyed
                   && mech.RaceProps.IsMechanoid
                   && mech.Faction != null
                   && mech.Faction.IsPlayerSafe()
                   && mech.mechanitor != null
                   && DataProcessingAllocatorEligibilityUtility.IsMechanoidDataProcessingOverseer(mech);
        }

        /// <summary>
        ///     安装了并行思维接口的人类机械师是否显示“意识分配” Gizmo。
        /// </summary>
        public static bool ShouldShowForHumanInterfaceMechanitor(Pawn? pawn)
        {
            return DataProcessingAllocatorEligibilityUtility.IsHumanInterfaceMechanitor(pawn)
                   && ResearchFeatureUnlockUtility.IsDataProcessingAllocationUnlocked();
        }

        /// <summary>
        ///     创建打开分配仪表盘窗口的通用命令。标题文本根据目标类型动态替换。
        /// </summary>
        public static Command_Action MakeCommand(Pawn overseer)
        {
            Pawn localOverseer = overseer;
            string labelKey = DataProcessingTerminologyUtility.GetAllocationLabel(overseer);
            string descKey = DataProcessingTerminologyUtility.GetAllocationDescription(overseer);
            return new Command_Action
            {
                defaultLabel = labelKey.Translate(),
                defaultDesc = descKey.Translate(),
                icon = DataProcessingAllocationIcon,
                action = delegate
                {
                    Find.WindowStack.Add(
                        new Dialog_DataProcessingAllocationDashboard(localOverseer));
                }
            };
        }
    }
}
