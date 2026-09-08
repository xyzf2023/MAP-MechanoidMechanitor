using RimWorld;
using UnityEngine;
using Verse;

namespace MAP_MechanoidMechanitor
{
    /// <summary>
    /// 自律指令核心保存的心智快照详情页。
    ///
    /// 本 Tab 只负责：
    /// - 取数据（SelThing 上 Comp 持有的 MindMappingData 快照）
    /// - IsVisible / UpdateSize / FillTab
    /// - 持有“左栏滚动位置”（以 ref 传入角色卡绘制器）
    ///
    /// 实际的只读“原版 Pawn 角色卡”式绘制全部委托给
    /// <see cref="MindMappingCharacterCardUtility"/>，不创建/依赖任何 Pawn。
    /// </summary>
    public sealed class ITab_MindMappingDetails : ITab
    {
        /// <summary>角色卡左栏内部滚动位置（由本 Tab 持有）。</summary>
        private Vector2 leftRectScrollPos;

        public ITab_MindMappingDetails()
        {
            labelKey = "MAP_MindMapping.Tab.Details";
            tutorTag = "MindMappingDetails";
        }

        public override bool IsVisible
        {
            get
            {
                Thing? selectedThing = SelThing;
                CompMindMappingAutonomousDirectiveCore? core =
                    selectedThing?.TryGetComp<CompMindMappingAutonomousDirectiveCore>();
                return core?.Data != null;
            }
        }

        protected override void UpdateSize()
        {
            base.UpdateSize();
            size = MindMappingCharacterCardUtility.TotalCardSize;
        }

        protected override void FillTab()
        {
            Thing? selectedThing = SelThing;
            CompMindMappingAutonomousDirectiveCore? core =
                selectedThing?.TryGetComp<CompMindMappingAutonomousDirectiveCore>();
            MindMappingData? data = core?.Data;
            if (data == null)
            {
                return;
            }

            UpdateSize();

            Rect cardRect = new Rect(
                MindMappingCharacterCardUtility.CardMargin,
                MindMappingCharacterCardUtility.CardMargin,
                MindMappingCharacterCardUtility.BaseCardSize.x,
                MindMappingCharacterCardUtility.BaseCardSize.y);

            MindMappingCharacterCardUtility.DrawCharacterCard(cardRect, data, ref leftRectScrollPos);
        }
    }
}
